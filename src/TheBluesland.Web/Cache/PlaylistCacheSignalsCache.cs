using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheBluesland.Data;

namespace TheBluesland.Web.Cache;

/// <summary>
/// One batched signal lookup per five minutes, with a safe empty fallback on database failure.
///
/// Was <c>PlaylistEraCache</c> (computed eras only) until 2026-09-10
/// (docs/specs/catalogue-priority-and-follower-sort.md), when follower-count-based home page
/// sorting needed the exact same shape of thing: a <c>spotify_playlist_cache</c>-derived signal
/// that must be fresher than a process restart, merged onto the static Markdown catalogue at read
/// time. Rather than add a second 5-minute poller, the one query now returns both fields - one
/// round trip does what two otherwise would.
/// </summary>
public sealed class PlaylistCacheSignalsCache : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private readonly object _cacheKey = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly IDbContextFactory<TheBlueslandDbContext> _dbContextFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PlaylistCacheSignalsCache> _logger;

    public PlaylistCacheSignalsCache(
        IDbContextFactory<TheBlueslandDbContext> dbContextFactory,
        IMemoryCache cache,
        ILogger<PlaylistCacheSignalsCache> logger)
    {
        _dbContextFactory = dbContextFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, PlaylistCacheSignals>> GetAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<IReadOnlyDictionary<string, PlaylistCacheSignals>>(_cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<IReadOnlyDictionary<string, PlaylistCacheSignals>>(_cacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            IReadOnlyDictionary<string, PlaylistCacheSignals> signals;
            try
            {
                await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                signals = await dbContext.SpotifyPlaylistCache.AsNoTracking()
                    .Where(row => row.IsAvailable)
                    .Select(row => new { row.SpotifyPlaylistId, row.ComputedEras, row.FollowerCount })
                    .ToDictionaryAsync(
                        row => row.SpotifyPlaylistId,
                        row => new PlaylistCacheSignals(row.ComputedEras, row.FollowerCount),
                        cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Playlist cache signal lookup failed; using editorial era tags and no follower count.");
                signals = new Dictionary<string, PlaylistCacheSignals>();
            }

            _cache.Set(_cacheKey, signals, RefreshInterval);
            return signals;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose() => _refreshLock.Dispose();
}

/// <param name="ComputedEras">Null when never measured or data was insufficient (US-023/US-026).</param>
/// <param name="FollowerCount">Null when never synced; an aggregate, not track-level data.</param>
public sealed record PlaylistCacheSignals(string[]? ComputedEras, int? FollowerCount);
