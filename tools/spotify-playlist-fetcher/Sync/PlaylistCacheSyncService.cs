using Microsoft.EntityFrameworkCore;
using TheBluesland.Data;
using TheBluesland.Data.Entities;
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

    public PlaylistCacheSyncService(SpotifyPlaylistClient playlistClient, TheBlueslandDbContext dbContext)
    {
        _playlistClient = playlistClient;
        _dbContext = dbContext;
    }

    public async Task<SyncSummary> SyncAsync(
        IReadOnlyCollection<string> spotifyPlaylistIds,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var unavailable = 0;
        var processed = 0;

        foreach (var spotifyPlaylistId in spotifyPlaylistIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Console.Error.WriteLine($"Sync [{++processed}/{spotifyPlaylistIds.Count}]: {spotifyPlaylistId}");

            // Read the row first: its snapshot id is what lets the client skip the paginated track
            // pass for a playlist nobody has touched since the last run (US-024).
            var existingEntry = await _dbContext.SpotifyPlaylistCache.FindAsync([spotifyPlaylistId], cancellationToken);
            var fetchResult = await _playlistClient.FetchAsync(
                spotifyPlaylistId, accessToken, ReusableSnapshotId(existingEntry), cancellationToken);
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

        return new SyncSummary(created, updated, skipped, unavailable);
    }

    /// <summary>
    /// The snapshot id worth trusting for a skip, or null to force a full read. Only a row that is
    /// available <i>and</i> already carries computed eras qualifies: rows written before the eras
    /// column existed have a valid snapshot id but no eras, and skipping those would leave them
    /// without eras forever, since nothing else recomputes them.
    /// </summary>
    private static string? ReusableSnapshotId(SpotifyPlaylistCacheEntry? entry) =>
        entry is { IsAvailable: true, ComputedEras: not null } ? entry.SpotifySnapshotId : null;

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
            SpotifySnapshotId = summary.SnapshotId,
            SyncedAt = syncedAt,
            IsAvailable = true,
        };

    private static void ApplySummary(SpotifyPlaylistCacheEntry entry, SpotifyPlaylistSummary summary, DateTimeOffset syncedAt)
    {
        ApplyPlaylistMetadata(entry, summary, syncedAt);
        entry.Artists = summary.Artists;
        entry.ComputedEras = summary.ComputedEras;
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
