using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheBluesland.Data;

namespace TheBluesland.Web.Cache;

/// <summary>One batched era lookup per five minutes, with editorial fallback on database failure.</summary>
public sealed class PlaylistEraCache : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private readonly object _cacheKey = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly IDbContextFactory<TheBlueslandDbContext> _dbContextFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PlaylistEraCache> _logger;

    public PlaylistEraCache(
        IDbContextFactory<TheBlueslandDbContext> dbContextFactory,
        IMemoryCache cache,
        ILogger<PlaylistEraCache> logger)
    {
        _dbContextFactory = dbContextFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, string[]>> GetAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<IReadOnlyDictionary<string, string[]>>(_cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<IReadOnlyDictionary<string, string[]>>(_cacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            IReadOnlyDictionary<string, string[]> eras;
            try
            {
                await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                eras = await dbContext.SpotifyPlaylistCache.AsNoTracking()
                    .Where(row => row.IsAvailable && row.ComputedEras != null)
                    .Select(row => new { row.SpotifyPlaylistId, row.ComputedEras })
                    .ToDictionaryAsync(row => row.SpotifyPlaylistId, row => row.ComputedEras!, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Era cache lookup failed; using editorial era tags.");
                eras = new Dictionary<string, string[]>();
            }

            _cache.Set(_cacheKey, eras, RefreshInterval);
            return eras;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose() => _refreshLock.Dispose();
}
