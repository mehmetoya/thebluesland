using Microsoft.EntityFrameworkCore;
using TheBluesland.Data;
using TheBluesland.Data.Entities;
using TheBluesland.SpotifyFetcher.Content;
using TheBluesland.SpotifyFetcher.Spotify;

namespace TheBluesland.SpotifyFetcher.Sync;

/// <summary>
/// Upserts <see cref="SpotifyPlaylistCacheEntry"/> rows from freshly fetched Spotify data. Never
/// deletes a row - an explicit "not found" response only flips <c>is_available</c> to false, and a
/// playlist that has never been found yet still gets a row (with unknown fields left at their
/// default) rather than silently having no row at all (spec section 9.4, 16.1, FR-024).
/// </summary>
public sealed class PlaylistCacheSyncService
{
    private readonly SpotifyPlaylistClient _playlistClient;
    private readonly TheBlueslandDbContext _dbContext;
    private readonly PlaylistFrontMatterWriter _frontMatterWriter = new();

    public PlaylistCacheSyncService(SpotifyPlaylistClient playlistClient, TheBlueslandDbContext dbContext)
    {
        _playlistClient = playlistClient;
        _dbContext = dbContext;
    }

    /// <param name="forceFullRead">
    /// US-025: when true, every playlist gets the paginated track read regardless of whether its
    /// <c>snapshot_id</c> matches the cached one - the exact opposite of US-024's short-circuit,
    /// for the one case that skip cannot handle: an era-bucket-mapping rule change (new decade
    /// boundary) needs every playlist re-evaluated even though nothing changed on Spotify. Applies
    /// only to this call; nothing is persisted, so the very next normal sync goes back to skipping.
    /// </param>
    /// <param name="contentEntries">
    /// Optional per-file status/slug/path lookup (auto-unpublish-private-playlists spec) - when a
    /// found playlist's <c>IsPublic</c> is false and the matching entry's <c>Status</c> is
    /// currently <c>published</c>, that file is rewritten to <c>draft</c> and its slug recorded in
    /// <see cref="SyncSummary.NewlyUnpublishedSlugs"/>. Omitted (or an id with no matching entry)
    /// means no rewrite is attempted for that playlist - this is additive, never required.
    /// </param>
    public async Task<SyncSummary> SyncAsync(
        IReadOnlyCollection<string> spotifyPlaylistIds,
        string accessToken,
        CancellationToken cancellationToken,
        bool forceFullRead = false,
        IReadOnlyCollection<PlaylistFrontMatterEntry>? contentEntries = null)
    {
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var unavailable = 0;
        var processed = 0;
        var newlyUnpublishedSlugs = new List<string>();

        var contentByPlaylistId = new Dictionary<string, PlaylistFrontMatterEntry>(StringComparer.Ordinal);
        foreach (var entry in contentEntries ?? [])
        {
            contentByPlaylistId.TryAdd(entry.SpotifyPlaylistId, entry);
        }

        foreach (var spotifyPlaylistId in spotifyPlaylistIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Console.Error.WriteLine($"Sync [{++processed}/{spotifyPlaylistIds.Count}]: {spotifyPlaylistId}");

            // Read the row first: its snapshot id is what lets the client skip the paginated track
            // pass for a playlist nobody has touched since the last run (US-024).
            var existingEntry = await _dbContext.SpotifyPlaylistCache.FindAsync([spotifyPlaylistId], cancellationToken);
            var knownSnapshotId = forceFullRead ? null : ReusableSnapshotId(existingEntry);
            var fetchResult = await _playlistClient.FetchAsync(
                spotifyPlaylistId, accessToken, knownSnapshotId, cancellationToken);
            var syncedAt = DateTimeOffset.UtcNow;

            if (fetchResult is SpotifyPlaylistFetchResult.Found found)
            {
                if (existingEntry is null)
                {
                    _dbContext.SpotifyPlaylistCache.Add(CreateEntry(spotifyPlaylistId, found.Summary, syncedAt));
                    created++;
                }
                else if (found.TrackAggregatesSkipped)
                {
                    // Name, description and cover can change without touching a single track, so the
                    // free summary fields are still written; artists and eras are left exactly as
                    // they are, because a skipped fetch never read the tracks they come from.
                    ApplyPlaylistMetadata(existingEntry, found.Summary, syncedAt);
                    skipped++;
                }
                else
                {
                    ApplySummary(existingEntry, found.Summary, syncedAt);
                    updated++;
                }

                // Auto-unpublish-private-playlists spec: one-directional only - a playlist already
                // draft, or one with no matching content entry, is left untouched either way.
                if (!found.Summary.IsPublic
                    && contentByPlaylistId.TryGetValue(spotifyPlaylistId, out var contentEntry)
                    && string.Equals(contentEntry.Status, "published", StringComparison.Ordinal))
                {
                    await _frontMatterWriter.UnpublishAsync(contentEntry.FilePath, cancellationToken);
                    newlyUnpublishedSlugs.Add(contentEntry.Slug);
                }
            }
            else
            {
                if (existingEntry is null)
                {
                    _dbContext.SpotifyPlaylistCache.Add(CreateUnavailableEntry(spotifyPlaylistId, syncedAt));
                }
                else
                {
                    existingEntry.IsAvailable = false;
                    existingEntry.SyncedAt = syncedAt;
                }

                unavailable++;
            }

            // Saved per playlist rather than once at the end: at 120+ playlists (some running to
            // thousands of tracks, each requiring its own paginated Spotify calls just to collect
            // artist names), a single transient failure partway through must not discard every
            // successful fetch that already happened in this run. Re-running the sync after a
            // partial failure is safe either way (idempotent upsert, spec US-003).
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var summary = new SyncSummary(created, updated, skipped, unavailable);
        return newlyUnpublishedSlugs.Count == 0
            ? summary
            : summary with { NewlyUnpublishedSlugs = newlyUnpublishedSlugs };
    }

    /// <summary>
    /// The snapshot id worth trusting for a skip, or null to force a full read. A row qualifies
    /// only when it is available and already carries every track-derived aggregate: rows written
    /// before one of those columns existed have a valid snapshot id but a gap, and skipping those
    /// would leave the gap forever, since nothing else recomputes it. Adding a new track-derived
    /// column means adding it here too - that is what makes the first sync after its migration
    /// backfill every row and then go back to skipping.
    /// </summary>
    private static string? ReusableSnapshotId(SpotifyPlaylistCacheEntry? entry) =>
        entry is { IsAvailable: true, ComputedEras: not null, EraBucketCounts: not null }
            ? entry.SpotifySnapshotId
            : null;

    private static SpotifyPlaylistCacheEntry CreateEntry(
        string spotifyPlaylistId,
        SpotifyPlaylistSummary summary,
        DateTimeOffset syncedAt) =>
        new()
        {
            SpotifyPlaylistId = spotifyPlaylistId,
            Name = summary.Name,
            Description = summary.Description,
            CoverImageUrl = summary.CoverImageUrl,
            TrackCount = summary.TrackCount,
            Artists = summary.Artists,
            ComputedEras = summary.ComputedEras,
            EraBucketCounts = summary.EraBucketCounts,
            SpotifySnapshotId = summary.SnapshotId,
            SyncedAt = syncedAt,
            IsAvailable = true,
        };

    private static void ApplySummary(SpotifyPlaylistCacheEntry entry, SpotifyPlaylistSummary summary, DateTimeOffset syncedAt)
    {
        ApplyPlaylistMetadata(entry, summary, syncedAt);
        entry.Artists = summary.Artists;
        entry.ComputedEras = summary.ComputedEras;
        entry.EraBucketCounts = summary.EraBucketCounts;
    }

    /// <summary>Everything the single playlist request returns - no track-derived field.</summary>
    private static void ApplyPlaylistMetadata(
        SpotifyPlaylistCacheEntry entry,
        SpotifyPlaylistSummary summary,
        DateTimeOffset syncedAt)
    {
        entry.Name = summary.Name;
        entry.Description = summary.Description;
        entry.CoverImageUrl = summary.CoverImageUrl;
        entry.TrackCount = summary.TrackCount;
        entry.SpotifySnapshotId = summary.SnapshotId;
        entry.SyncedAt = syncedAt;
        entry.IsAvailable = true;
    }

    private static SpotifyPlaylistCacheEntry CreateUnavailableEntry(string spotifyPlaylistId, DateTimeOffset syncedAt) =>
        new()
        {
            SpotifyPlaylistId = spotifyPlaylistId,
            Name = string.Empty,
            Description = null,
            CoverImageUrl = null,
            TrackCount = 0,
            Artists = [],
            SpotifySnapshotId = null,
            SyncedAt = syncedAt,
            IsAvailable = false,
        };
}
