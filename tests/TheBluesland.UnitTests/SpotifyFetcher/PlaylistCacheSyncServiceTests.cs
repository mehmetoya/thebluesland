using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using TheBluesland.SpotifyFetcher.Content;
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
        // US-024: the fixture's snapshot id is unchanged between the two runs, so the second one
        // reaches the same single row without re-reading a single track page.
        secondRunSummary.Updated.ShouldBe(0);
        secondRunSummary.Skipped.ShouldBe(1);

        var rowCount = await dbContext.SpotifyPlaylistCache.AsNoTracking()
            .CountAsync(e => e.SpotifyPlaylistId == AvailablePlaylistId);
        rowCount.ShouldBe(1);
    }

    [Fact]
    public async Task SyncAsync_persists_an_earlier_playlist_even_when_a_later_one_throws()
    {
        // Regression test: at scale (100+ playlists, some running to thousands of tracks) a single
        // transient failure partway through a run must not discard every successful fetch that
        // already happened - each row is saved as it's fetched, not once at the very end.
        var handler = new FakeHttpMessageHandler(request =>
        {
            var absolutePath = request.RequestUri!.AbsolutePath;

            if (absolutePath == $"/v1/playlists/{AvailablePlaylistId}")
            {
                return JsonResponse(
                    """
                    {
                      "name": "Masterpieces of Erkin the Father",
                      "items": { "total": 0 }
                    }
                    """);
            }

            if (absolutePath == $"/v1/playlists/{AvailablePlaylistId}/items")
            {
                return JsonResponse("""{"items":[],"next":null}""");
            }

            if (absolutePath == $"/v1/playlists/{MissingPlaylistId}")
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            throw new InvalidOperationException($"Unexpected request path '{absolutePath}'.");
        });

        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistCacheSyncService(new SpotifyPlaylistClient(new HttpClient(handler)), dbContext);

        await Should.ThrowAsync<HttpRequestException>(() =>
            service.SyncAsync([AvailablePlaylistId, MissingPlaylistId], AccessToken, CancellationToken.None));

        await using var verifyContext = await CreateMigratedDbContextAsync();
        var persisted = await verifyContext.SpotifyPlaylistCache.AsNoTracking()
            .SingleOrDefaultAsync(e => e.SpotifyPlaylistId == AvailablePlaylistId);
        persisted.ShouldNotBeNull();
        persisted.Name.ShouldBe("Masterpieces of Erkin the Father");
    }

    [Fact]
    public async Task SyncAsync_with_no_playlist_ids_makes_no_spotify_calls_and_writes_nothing()
    {
        var handler = new FakeHttpMessageHandler(
            _ => throw new InvalidOperationException("Spotify must not be called when there are no playlist ids to sync."));
        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistCacheSyncService(new SpotifyPlaylistClient(new HttpClient(handler)), dbContext);

        var summary = await service.SyncAsync([], AccessToken, CancellationToken.None);

        summary.ShouldBe(new SyncSummary(0, 0, 0, 0));
        var rowCount = await dbContext.SpotifyPlaylistCache.AsNoTracking().CountAsync();
        rowCount.ShouldBe(0);
    }

    [Fact]
    public async Task SyncAsync_persists_recalculates_and_clears_eras_when_data_becomes_insufficient()
    {
        var year = "1971";
        var datedCount = 10;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == $"/v1/playlists/{AvailablePlaylistId}")
            {
                return JsonResponse("""{"name":"Era test","items":{"total":10}}""");
            }

            var items = Enumerable.Range(0, datedCount).Select(_ => new
            {
                item = new { album = new { release_date = year } },
            });
            return JsonResponse(System.Text.Json.JsonSerializer.Serialize(new { items, next = (string?)null }));
        }));
        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistCacheSyncService(new SpotifyPlaylistClient(httpClient), dbContext);

        await service.SyncAsync([AvailablePlaylistId], AccessToken, CancellationToken.None);
        var entry = await dbContext.SpotifyPlaylistCache.AsNoTracking().SingleAsync();
        entry.ComputedEras.ShouldBe(["1970s"]);

        year = "1985";
        await service.SyncAsync([AvailablePlaylistId], AccessToken, CancellationToken.None);
        entry = await dbContext.SpotifyPlaylistCache.AsNoTracking().SingleAsync();
        entry.ComputedEras.ShouldBe(["1980s-1990s"]);

        datedCount = 9;
        await service.SyncAsync([AvailablePlaylistId], AccessToken, CancellationToken.None);
        entry = await dbContext.SpotifyPlaylistCache.AsNoTracking().SingleAsync();
        entry.ComputedEras.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public async Task SyncAsync_skips_the_paginated_track_read_when_the_snapshot_id_is_unchanged()
    {
        await SeedSyncedRowAsync(
            snapshotId: "snapshot-available",
            artists: ["Erkin Koray"],
            computedEras: ["1970s"],
            eraBucketCounts: [0, 12, 0, 0]);

        var itemsRequests = 0;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(request =>
        {
            var absolutePath = request.RequestUri!.AbsolutePath;
            if (absolutePath == $"/v1/playlists/{AvailablePlaylistId}/items")
            {
                itemsRequests++;
            }

            // A renamed playlist with the same snapshot id: tracks unchanged, metadata changed.
            return JsonResponse(
                """
                {
                  "name": "Erkin Koray, renamed",
                  "items": { "total": 1 },
                  "snapshot_id": "snapshot-available"
                }
                """);
        }));
        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistCacheSyncService(new SpotifyPlaylistClient(httpClient), dbContext);

        var summary = await service.SyncAsync([AvailablePlaylistId], AccessToken, CancellationToken.None);

        itemsRequests.ShouldBe(0); // the whole point: no per-100-track request goes out at all
        summary.ShouldBe(new SyncSummary(Created: 0, Updated: 0, Skipped: 1, Unavailable: 0));

        var entry = await dbContext.SpotifyPlaylistCache.AsNoTracking()
            .SingleAsync(e => e.SpotifyPlaylistId == AvailablePlaylistId);
        entry.Artists.ShouldBe(["Erkin Koray"]);      // kept, not overwritten with the skipped read's empty
        entry.ComputedEras.ShouldBe(["1970s"]);
        entry.Name.ShouldBe("Erkin Koray, renamed");  // the free summary fields still refresh
        entry.SyncedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task SyncAsync_with_forceFullRead_reads_tracks_despite_a_matching_snapshot_id()
    {
        // US-025: an era-bucket-boundary change needs every playlist re-read even though nothing
        // changed on Spotify - forceFullRead is the runtime-only escape hatch from US-024's skip.
        await SeedSyncedRowAsync(
            snapshotId: "snapshot-available",
            artists: ["Erkin Koray"],
            computedEras: ["1970s"],
            eraBucketCounts: [0, 12, 0, 0]);

        var itemsRequests = 0;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(request =>
        {
            var absolutePath = request.RequestUri!.AbsolutePath;
            if (absolutePath == $"/v1/playlists/{AvailablePlaylistId}/items")
            {
                itemsRequests++;
                return JsonResponse(
                    """
                    {
                      "items": [
                        { "item": { "artists": [{ "name": "Erkin Koray" }] }, "track": {} }
                      ],
                      "next": null
                    }
                    """);
            }

            // Same unchanged snapshot id as the seeded row - a plain sync would skip this playlist.
            return JsonResponse(
                """
                {
                  "name": "Masterpieces of Erkin the Father",
                  "items": { "total": 1 },
                  "snapshot_id": "snapshot-available"
                }
                """);
        }));
        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistCacheSyncService(new SpotifyPlaylistClient(httpClient), dbContext);

        var summary = await service.SyncAsync(
            [AvailablePlaylistId], AccessToken, CancellationToken.None, forceFullRead: true);

        itemsRequests.ShouldBe(1); // the whole point: the paginated read runs despite the match
        summary.ShouldBe(new SyncSummary(Created: 0, Updated: 1, Skipped: 0, Unavailable: 0));
    }

    [Fact]
    public async Task SyncAsync_still_reads_tracks_in_full_when_the_snapshot_id_differs()
    {
        await SeedSyncedRowAsync(
            snapshotId: "snapshot-stale", artists: ["Stale Artist"], computedEras: ["1980s-1990s"]);

        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistCacheSyncService(CreateAvailablePlaylistClient(), dbContext);

        var summary = await service.SyncAsync([AvailablePlaylistId], AccessToken, CancellationToken.None);

        summary.ShouldBe(new SyncSummary(Created: 0, Updated: 1, Skipped: 0, Unavailable: 0));

        var entry = await dbContext.SpotifyPlaylistCache.AsNoTracking()
            .SingleAsync(e => e.SpotifyPlaylistId == AvailablePlaylistId);
        entry.Artists.ShouldBe(["Erkin Koray"]);
        entry.SpotifySnapshotId.ShouldBe("snapshot-available");
    }

    [Fact]
    public async Task SyncAsync_reads_tracks_in_full_when_a_matching_snapshot_row_has_no_computed_eras_yet()
    {
        // Rows written before the eras column existed carry a valid snapshot id and no eras.
        // Skipping those on a snapshot match would leave them era-less permanently.
        await SeedSyncedRowAsync(
            snapshotId: "snapshot-available", artists: ["Erkin Koray"], computedEras: null);

        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistCacheSyncService(CreateAvailablePlaylistClient(), dbContext);

        var summary = await service.SyncAsync([AvailablePlaylistId], AccessToken, CancellationToken.None);

        summary.Updated.ShouldBe(1);
        summary.Skipped.ShouldBe(0);

        var entry = await dbContext.SpotifyPlaylistCache.AsNoTracking()
            .SingleAsync(e => e.SpotifyPlaylistId == AvailablePlaylistId);
        entry.ComputedEras.ShouldNotBeNull();
    }

    [Fact]
    public async Task SyncAsync_unpublishes_a_published_file_when_spotify_reports_the_playlist_private()
    {
        // Auto-unpublish-private-playlists spec: CreateAvailablePlaylistClient's fixture response
        // carries no "public" field at all, which SpotifyPlaylistClient maps to IsPublic: false -
        // exactly the "playlist went private" case this behaviour reacts to.
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                filePath,
                """
                ---
                slug: masterpieces-of-erkin-the-father
                status: published
                publishedAt: 2026-09-05
                ---

                Body text.
                """);
            var contentEntries = new[]
            {
                new PlaylistFrontMatterEntry(
                    AvailablePlaylistId, "masterpieces-of-erkin-the-father", [], "published", filePath),
            };
            await using var dbContext = await CreateMigratedDbContextAsync();
            var service = new PlaylistCacheSyncService(CreateAvailablePlaylistClient(), dbContext);

            var summary = await service.SyncAsync(
                [AvailablePlaylistId], AccessToken, CancellationToken.None, contentEntries: contentEntries);

            summary.NewlyUnpublishedSlugs.ShouldBe(["masterpieces-of-erkin-the-father"]);
            var rewritten = await File.ReadAllLinesAsync(filePath);
            rewritten.ShouldContain("status: draft");
            rewritten.ShouldNotContain(line => line.StartsWith("publishedAt:", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task SyncAsync_does_not_touch_an_already_draft_file_even_when_the_playlist_is_private()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(filePath, "---\nslug: already-draft\nstatus: draft\n---\n\nBody.\n");
            var contentEntries = new[]
            {
                new PlaylistFrontMatterEntry(AvailablePlaylistId, "already-draft", [], "draft", filePath),
            };
            await using var dbContext = await CreateMigratedDbContextAsync();
            var service = new PlaylistCacheSyncService(CreateAvailablePlaylistClient(), dbContext);

            var summary = await service.SyncAsync(
                [AvailablePlaylistId], AccessToken, CancellationToken.None, contentEntries: contentEntries);

            summary.NewlyUnpublishedSlugs.ShouldBeEmpty();
            var untouched = await File.ReadAllTextAsync(filePath);
            untouched.ShouldBe("---\nslug: already-draft\nstatus: draft\n---\n\nBody.\n");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task SyncAsync_does_not_unpublish_a_published_file_when_the_playlist_is_still_public()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(filePath, "---\nslug: still-public\nstatus: published\n---\n\nBody.\n");
            var contentEntries = new[]
            {
                new PlaylistFrontMatterEntry(AvailablePlaylistId, "still-public", [], "published", filePath),
            };
            using var httpClient = new HttpClient(new FakeHttpMessageHandler(request =>
            {
                var absolutePath = request.RequestUri!.AbsolutePath;
                if (absolutePath == $"/v1/playlists/{AvailablePlaylistId}")
                {
                    return JsonResponse("""{ "name": "Still Public", "items": { "total": 0 }, "public": true }""");
                }

                return JsonResponse("""{ "items": [], "next": null }""");
            }));
            await using var dbContext = await CreateMigratedDbContextAsync();
            var service = new PlaylistCacheSyncService(new SpotifyPlaylistClient(httpClient), dbContext);

            var summary = await service.SyncAsync(
                [AvailablePlaylistId], AccessToken, CancellationToken.None, contentEntries: contentEntries);

            summary.NewlyUnpublishedSlugs.ShouldBeEmpty();
            var untouched = await File.ReadAllTextAsync(filePath);
            untouched.ShouldBe("---\nslug: still-public\nstatus: published\n---\n\nBody.\n");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private async Task SeedSyncedRowAsync(
        string snapshotId,
        string[] artists,
        string[]? computedEras,
        int[]? eraBucketCounts = null)
    {
        await using var seedContext = await CreateMigratedDbContextAsync();
        seedContext.SpotifyPlaylistCache.Add(new()
        {
            SpotifyPlaylistId = AvailablePlaylistId,
            Name = "Masterpieces of Erkin the Father",
            TrackCount = 1,
            Artists = artists,
            ComputedEras = computedEras,
            // Defaults to null so a caller that says nothing gets a row that still needs a full
            // read - the same shape as a row written before the counts column existed.
            EraBucketCounts = eraBucketCounts,
            SpotifySnapshotId = snapshotId,
            SyncedAt = DateTimeOffset.UtcNow.AddDays(-30),
            IsAvailable = true,
        });
        await seedContext.SaveChangesAsync();
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
                      "items": { "total": 1 },
                      "snapshot_id": "snapshot-available"
                    }
                    """);
            }

            if (absolutePath == $"/v1/playlists/{AvailablePlaylistId}/items")
            {
                // "track" is post-Feb-2026 deprecated and always empty; artists live under "item".
                return JsonResponse(
                    """
                    {
                      "items": [
                        { "item": { "artists": [{ "name": "Erkin Koray" }] }, "track": {} }
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
