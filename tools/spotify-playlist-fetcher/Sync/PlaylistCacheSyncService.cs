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
        var unavailable = 0;

        foreach (var spotifyPlaylistId in spotifyPlaylistIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fetchResult = await _playlistClient.FetchAsync(spotifyPlaylistId, accessToken, cancellationToken);
            var existingEntry = await _dbContext.SpotifyPlaylistCache.FindAsync([spotifyPlaylistId], cancellationToken);
            var syncedAt = DateTimeOffset.UtcNow;

            if (fetchResult is SpotifyPlaylistFetchResult.Found found)
            {
                if (existingEntry is null)
                {
                    _dbContext.SpotifyPlaylistCache.Add(CreateEntry(spotifyPlaylistId, found.Summary, syncedAt));
                    created++;
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

        return new SyncSummary(created, updated, unavailable);
    }

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
            SpotifySnapshotId = summary.SnapshotId,
            SyncedAt = syncedAt,
            IsAvailable = true,
        };

    private static void ApplySummary(SpotifyPlaylistCacheEntry entry, SpotifyPlaylistSummary summary, DateTimeOffset syncedAt)
    {
        entry.Name = summary.Name;
        entry.Description = summary.Description;
        entry.CoverImageUrl = summary.CoverImageUrl;
        entry.TrackCount = summary.TrackCount;
        entry.Artists = summary.Artists;
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
