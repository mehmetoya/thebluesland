using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using TheBluesland.Data.Entities;
using TheBluesland.Web.Cache;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-005 acceptance criteria 1-3: a missing cache row, a row marked is_available = false and a
/// completely unreachable database must all degrade to the identical "unavailable" snapshot
/// without PlaylistCacheLookup ever throwing. Uses a real, disposable Testcontainers Postgres
/// instance (spec section 17.2/17.4), never a mocked DbContext.
/// </summary>
public sealed class PlaylistCacheLookupTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var optionsBuilder = new DbContextOptionsBuilder<TheBlueslandDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());
        await using var dbContext = new TheBlueslandDbContext(optionsBuilder.Options);
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task GetSnapshotAsync_no_row_exists_returns_unavailable()
    {
        var lookup = CreateLookup(_postgres.GetConnectionString());

        var snapshot = await lookup.GetSnapshotAsync("no-such-playlist-id", CancellationToken.None);

        snapshot.ShouldBe(PlaylistCacheSnapshot.Unavailable);
    }

    [Fact]
    public async Task GetSnapshotAsync_row_is_available_returns_playable_snapshot_with_cache_fields()
    {
        const string playlistId = "available-playlist-id";
        await SeedRowAsync(playlistId, isAvailable: true, trackCount: 12, coverImageUrl: "https://i.scdn.co/image/cover.jpg");
        var lookup = CreateLookup(_postgres.GetConnectionString());

        var snapshot = await lookup.GetSnapshotAsync(playlistId, CancellationToken.None);

        snapshot.IsPlayable.ShouldBeTrue();
        snapshot.TrackCount.ShouldBe(12);
        snapshot.CoverImageUrl.ShouldBe("https://i.scdn.co/image/cover.jpg");
    }

    [Fact]
    public async Task GetSnapshotAsync_row_marked_unavailable_returns_unavailable_snapshot()
    {
        const string playlistId = "unavailable-playlist-id";
        await SeedRowAsync(playlistId, isAvailable: false, trackCount: 8, coverImageUrl: "https://i.scdn.co/image/gone.jpg");
        var lookup = CreateLookup(_postgres.GetConnectionString());

        var snapshot = await lookup.GetSnapshotAsync(playlistId, CancellationToken.None);

        snapshot.ShouldBe(PlaylistCacheSnapshot.Unavailable);
    }

    [Fact]
    public async Task GetSnapshotAsync_database_unreachable_returns_unavailable_without_throwing()
    {
        // GetConnectionString() must be captured before stopping the container - once stopped, the
        // mapped port is gone and GetConnectionString() itself throws.
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Timeout = 2,
        };
        await _postgres.StopAsync();
        var lookup = CreateLookup(connectionStringBuilder.ConnectionString);

        var snapshot = await lookup.GetSnapshotAsync("any-playlist-id", CancellationToken.None);

        snapshot.ShouldBe(PlaylistCacheSnapshot.Unavailable);
    }

    private async Task SeedRowAsync(string playlistId, bool isAvailable, int trackCount, string coverImageUrl)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TheBlueslandDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());
        await using var dbContext = new TheBlueslandDbContext(optionsBuilder.Options);
        dbContext.SpotifyPlaylistCache.Add(new SpotifyPlaylistCacheEntry
        {
            SpotifyPlaylistId = playlistId,
            Name = "Cache-owned name",
            TrackCount = trackCount,
            Artists = ["Some Artist"],
            CoverImageUrl = coverImageUrl,
            SyncedAt = DateTimeOffset.UtcNow,
            IsAvailable = isAvailable,
        });
        await dbContext.SaveChangesAsync();
    }

    private static PlaylistCacheLookup CreateLookup(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<TheBlueslandDbContext>(options => options.UseNpgsql(connectionString));
        var provider = services.BuildServiceProvider();
        var dbContextFactory = provider.GetRequiredService<IDbContextFactory<TheBlueslandDbContext>>();
        return new PlaylistCacheLookup(dbContextFactory, NullLogger<PlaylistCacheLookup>.Instance);
    }
}
