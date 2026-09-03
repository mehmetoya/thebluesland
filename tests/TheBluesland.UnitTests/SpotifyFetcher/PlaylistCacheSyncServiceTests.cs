using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using TheBluesland.SpotifyFetcher.Spotify;
using TheBluesland.SpotifyFetcher.Sync;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// US-003 acceptance criteria: a successful response upserts spotify_playlist_cache with
/// is_available = true; a 404/inaccessible response marks an existing row is_available = false
/// without deleting it (and creates one if none existed yet); running sync twice with the same
/// fixture leaves exactly one row per playlist (idempotent upsert). Uses a disposable
/// Testcontainers Postgres instance (spec section 17.2/17.4); all Spotify responses come from
/// FakeHttpMessageHandler, never a live API call.
/// </summary>
public sealed class PlaylistCacheSyncServiceTests : IAsyncLifetime
{
    private const string AvailablePlaylistId = "0iJt9LMebhOY0KSHSJw3cS";
    private const string MissingPlaylistId = "2m8X8fsMWor8A5AnmOHwzy";
    private const string AccessToken = "mocked-access-token";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task SyncAsync_upserts_row_with_is_available_true_for_a_successful_playlist_response()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistCacheSyncService(CreateAvailablePlaylistClient(), dbContext);

        var summary = await service.SyncAsync([AvailablePlaylistId], AccessToken, CancellationToken.None);

        summary.Created.ShouldBe(1);

        var entry = await dbContext.SpotifyPlaylistCache.AsNoTracking()
            .SingleAsync(e => e.SpotifyPlaylistId == AvailablePlaylistId);
        entry.Name.ShouldBe("Masterpieces of Erkin the Father");
        entry.Description.ShouldBe("Anadolu rock, straight from the source.");
        entry.CoverImageUrl.ShouldBe("https://i.scdn.co/image/cover.jpg");
        entry.TrackCount.ShouldBe(1);
        entry.Artists.ShouldBe(["Erkin Koray"]);
        entry.SpotifySnapshotId.ShouldBe("snapshot-available");
        entry.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task SyncAsync_marks_an_existing_row_unavailable_without_deleting_it_on_404()
    {
        await using (var seedContext = await CreateMigratedDbContextAsync())
        {
            seedContext.SpotifyPlaylistCache.Add(new()
            {
                SpotifyPlaylistId = MissingPlaylistId,
                Name = "Dear Mr. Fantasy",
                TrackCount = 12,
                Artists = ["Traffic", "Eric Clapton"],
                SyncedAt = DateTimeOffset.UtcNow.AddDays(-30),
                IsAvailable = true,
            });
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistCacheSyncService(CreateNotFoundPlaylistClient(), dbContext);

        var summary = await service.SyncAsync([MissingPlaylistId], AccessToken, CancellationToken.None);

        summary.Unavailable.ShouldBe(1);

        var entry = await dbContext.SpotifyPlaylistCache.AsNoTracking()
            .SingleAsync(e => e.SpotifyPlaylistId == MissingPlaylistId);
        entry.IsAvailable.ShouldBeFalse();
        entry.Name.ShouldBe("Dear Mr. Fantasy"); // last known value preserved; the row is not deleted
    }

    [Fact]
    public async Task SyncAsync_creates_an_unavailable_row_when_a_playlist_has_never_been_found()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistCacheSyncService(CreateNotFoundPlaylistClient(), dbContext);

        await service.SyncAsync([MissingPlaylistId], AccessToken, CancellationToken.None);

        var entry = await dbContext.SpotifyPlaylistCache.AsNoTracking()
            .SingleAsync(e => e.SpotifyPlaylistId == MissingPlaylistId);
        entry.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task SyncAsync_run_twice_with_the_same_fixture_leaves_exactly_one_row_per_playlist()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistCacheSyncService(CreateAvailablePlaylistClient(), dbContext);
        string[] playlistIds = [AvailablePlaylistId];

        await service.SyncAsync(playlistIds, AccessToken, CancellationToken.None);
        var secondRunSummary = await service.SyncAsync(playlistIds, AccessToken, CancellationToken.None);

        secondRunSummary.Created.ShouldBe(0);
        secondRunSummary.Updated.ShouldBe(1);

        var rowCount = await dbContext.SpotifyPlaylistCache.AsNoTracking()
            .CountAsync(e => e.SpotifyPlaylistId == AvailablePlaylistId);
        rowCount.ShouldBe(1);
    }

    [Fact]
    public async Task SyncAsync_with_no_playlist_ids_makes_no_spotify_calls_and_writes_nothing()
    {
        var handler = new FakeHttpMessageHandler(
            _ => throw new InvalidOperationException("Spotify must not be called when there are no playlist ids to sync."));
        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistCacheSyncService(new SpotifyPlaylistClient(new HttpClient(handler)), dbContext);

        var summary = await service.SyncAsync([], AccessToken, CancellationToken.None);

        summary.ShouldBe(new SyncSummary(0, 0, 0));
        var rowCount = await dbContext.SpotifyPlaylistCache.AsNoTracking().CountAsync();
        rowCount.ShouldBe(0);
    }

    private async Task<TheBlueslandDbContext> CreateMigratedDbContextAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TheBlueslandDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());
        var dbContext = new TheBlueslandDbContext(optionsBuilder.Options);
        await dbContext.Database.MigrateAsync();
        return dbContext;
    }

    private static SpotifyPlaylistClient CreateAvailablePlaylistClient()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var absolutePath = request.RequestUri!.AbsolutePath;

            if (absolutePath == $"/v1/playlists/{AvailablePlaylistId}")
            {
                return JsonResponse(
                    """
                    {
                      "name": "Masterpieces of Erkin the Father",
                      "description": "Anadolu rock, straight from the source.",
                      "images": [{ "url": "https://i.scdn.co/image/cover.jpg" }],
                      "tracks": { "total": 1 },
                      "snapshot_id": "snapshot-available"
                    }
                    """);
            }

            if (absolutePath == $"/v1/playlists/{AvailablePlaylistId}/tracks")
            {
                return JsonResponse(
                    """
                    {
                      "items": [
                        { "track": { "artists": [{ "name": "Erkin Koray" }] } }
                      ],
                      "next": null
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request path '{absolutePath}'.");
        });

        return new SpotifyPlaylistClient(new HttpClient(handler));
    }

    private static SpotifyPlaylistClient CreateNotFoundPlaylistClient()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        return new SpotifyPlaylistClient(new HttpClient(handler));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
