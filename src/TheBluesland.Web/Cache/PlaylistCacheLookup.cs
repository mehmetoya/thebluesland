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
}
