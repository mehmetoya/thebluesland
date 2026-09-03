using Microsoft.EntityFrameworkCore;
using TheBluesland.Data;

namespace TheBluesland.Web.Cache;

/// <summary>
/// Read-only, best-effort lookup of a single <c>spotify_playlist_cache</c> row. Never throws:
/// spec 16.1 requires that a database (Neon) outage degrade the page to editorial-only content
/// rather than a 500, so every failure here (connection refused, timeout, unexpected schema, ...)
/// is logged and reported the same way as "no row"/"not available" rather than propagated.
/// </summary>
public sealed class PlaylistCacheLookup
{
    private readonly IDbContextFactory<TheBlueslandDbContext> _dbContextFactory;
    private readonly ILogger<PlaylistCacheLookup> _logger;

    public PlaylistCacheLookup(IDbContextFactory<TheBlueslandDbContext> dbContextFactory, ILogger<PlaylistCacheLookup> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<PlaylistCacheSnapshot> GetSnapshotAsync(string spotifyPlaylistId, CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entry = await dbContext.SpotifyPlaylistCache
                .AsNoTracking()
                .SingleOrDefaultAsync(row => row.SpotifyPlaylistId == spotifyPlaylistId, cancellationToken);

            if (entry is null || !entry.IsAvailable)
            {
                return PlaylistCacheSnapshot.Unavailable;
            }

            return new PlaylistCacheSnapshot(IsPlayable: true, entry.TrackCount, entry.CoverImageUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Spotify playlist cache lookup failed for {SpotifyPlaylistId}; degrading to unavailable.",
                spotifyPlaylistId);
            return PlaylistCacheSnapshot.Unavailable;
        }
    }

    /// <summary>
    /// Batched form of <see cref="GetSnapshotAsync"/> for the home page catalogue (US-008): looks up
    /// every requested playlist in a single query instead of one query per card (core-rules.md: no
    /// DB calls in a loop). Same never-throws degradation as the single-item lookup - any failure
    /// yields <see cref="PlaylistCacheSnapshot.Unavailable"/> for every requested id.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, PlaylistCacheSnapshot>> GetSnapshotsAsync(
        IReadOnlyCollection<string> spotifyPlaylistIds,
        CancellationToken cancellationToken)
    {
        // Deduplicated up front: two content files could (invalidly) share a spotifyPlaylistId -
        // US-006 validation catches that in CI, but only as an advisory PR check until Mehmet
        // enables branch protection (US-007), so it must not be able to crash this lookup at
        // runtime. Building dictionaries from a plain ToDictionary over a duplicate-keyed
        // collection throws ArgumentException, which would defeat the never-throws contract below.
        var distinctIds = spotifyPlaylistIds.Distinct(StringComparer.Ordinal).ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<string, PlaylistCacheSnapshot>();
        }

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entries = await dbContext.SpotifyPlaylistCache
                .AsNoTracking()
                .Where(row => distinctIds.Contains(row.SpotifyPlaylistId))
                .ToListAsync(cancellationToken);

            return distinctIds.ToDictionary(
                id => id,
                id =>
                {
                    var entry = entries.SingleOrDefault(row => row.SpotifyPlaylistId == id);
                    return entry is null || !entry.IsAvailable
                        ? PlaylistCacheSnapshot.Unavailable
                        : new PlaylistCacheSnapshot(IsPlayable: true, entry.TrackCount, entry.CoverImageUrl);
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Batched Spotify playlist cache lookup failed; degrading all to unavailable.");
            return distinctIds.ToDictionary(id => id, _ => PlaylistCacheSnapshot.Unavailable);
        }
    }
}
