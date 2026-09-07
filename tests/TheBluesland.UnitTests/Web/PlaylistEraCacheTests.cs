using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using TheBluesland.Web.Cache;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

public sealed class PlaylistEraCacheTests
{
    [Fact]
    public async Task Repository_applies_synced_eras_to_catalogue_and_detail_and_refreshes_after_eviction()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<TheBlueslandDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<TheBlueslandDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync();
        await dbContext.Database.MigrateAsync();
        dbContext.SpotifyPlaylistCache.Add(new()
        {
            SpotifyPlaylistId = "0iJt9LMebhOY0KSHSJw3cS",
            Name = "Test",
            Artists = [],
            TrackCount = 10,
            SyncedAt = DateTimeOffset.UtcNow,
            IsAvailable = true,
            ComputedEras = ["1970s"],
        });
        await dbContext.SaveChangesAsync();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        using var eraCache = new PlaylistEraCache(factory, memoryCache, NullLogger<PlaylistEraCache>.Instance);
        var directory = Path.Combine(Path.GetTempPath(), $"era-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var fixture = await File.ReadAllTextAsync(Path.Combine(
                AppContext.BaseDirectory, "Fixtures", "content-playlists-detail", "primary-playlist.md"));
            await File.WriteAllTextAsync(Path.Combine(directory, "primary.md"), fixture.Replace("  - 1970s", "  - mixed-era"));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PlaylistContentRepository.ContentDirectoryConfigKey] = directory,
            }).Build();
            var repository = new PlaylistContentRepository(configuration, new PlaylistContentReader(), eraCache);

            var all = await repository.FindAllPublishedAsync(CancellationToken.None);
            all.Single().Eras.ShouldBe(["1970s"]);
            PlaylistFilter.Apply(all, new([], [], [], ["1970s"])).Count.ShouldBe(1);
            var detail = await repository.FindBySlugAsync("primary-playlist", CancellationToken.None);
            detail.ShouldNotBeNull().Eras.ShouldBe(["1970s"]);

            var row = await dbContext.SpotifyPlaylistCache.SingleAsync();
            row.ComputedEras = ["1980s-1990s"];
            await dbContext.SaveChangesAsync();
            (await eraCache.GetAsync(CancellationToken.None))[row.SpotifyPlaylistId].ShouldBe(["1970s"]);
            memoryCache.Compact(1);
            detail = await repository.FindBySlugAsync("primary-playlist", CancellationToken.None);
            detail.ShouldNotBeNull().Eras.ShouldBe(["1980s-1990s"]);

            row.IsAvailable = false;
            await dbContext.SaveChangesAsync();
            memoryCache.Compact(1);
            detail = await repository.FindBySlugAsync("primary-playlist", CancellationToken.None);
            detail.ShouldNotBeNull().Eras.ShouldBe(["mixed-era"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetAsync_database_failure_is_cached_and_returns_empty_fallback()
    {
        var factory = new FailingFactory();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        using var cache = new PlaylistEraCache(factory, memoryCache, NullLogger<PlaylistEraCache>.Instance);

        (await cache.GetAsync(CancellationToken.None)).ShouldBeEmpty();
        (await cache.GetAsync(CancellationToken.None)).ShouldBeEmpty();
        factory.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task GetAsync_cancellation_is_not_converted_to_an_editorial_fallback()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        using var cache = new PlaylistEraCache(new FailingFactory(), memoryCache, NullLogger<PlaylistEraCache>.Instance);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => cache.GetAsync(cancellation.Token));
    }

    private sealed class FailingFactory : IDbContextFactory<TheBlueslandDbContext>
    {
        public int Calls { get; private set; }

        public TheBlueslandDbContext CreateDbContext()
        {
            Calls++;
            throw new InvalidOperationException("Database unavailable");
        }
    }
}
