using System.Net;
using System.Text;
using Shouldly;
using TheBluesland.SpotifyFetcher.Spotify;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// US-003: maps a mocked Spotify Web API response to <see cref="SpotifyPlaylistSummary"/> - never
/// the live API in tests (spec section 17.4). Covers the normal-playlist and playlist-not-found
/// cases from 17.4, plus a regression proving no track-level field ever survives the mapping.
/// </summary>
public sealed class SpotifyPlaylistClientTests
{
    private const string PlaylistId = "2m8X8fsMWor8A5AnmOHwzy";
    private const string AccessToken = "mocked-access-token";

    [Fact]
    public async Task FetchAsync_maps_playlist_summary_and_collects_distinct_artist_names_across_pages()
    {
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(BuildTwoPageFoundResponder()));
        var client = new SpotifyPlaylistClient(httpClient);

        var result = await client.FetchAsync(PlaylistId, AccessToken, knownSnapshotId: null, CancellationToken.None);

        var found = result.ShouldBeOfType<SpotifyPlaylistFetchResult.Found>();
        found.Summary.Name.ShouldBe("Dear Mr. Fantasy");
        found.Summary.Description.ShouldBe("Blues rock for late nights.");
        found.Summary.CoverImageUrl.ShouldBe("https://i.scdn.co/image/cover.jpg");
        found.Summary.TrackCount.ShouldBe(2);
        found.Summary.SnapshotId.ShouldBe("snapshot-abc");
        found.Summary.Artists.ShouldBe(["Eric Clapton", "Traffic"], ignoreOrder: true);
    }

    [Fact]
    public async Task FetchAsync_returns_not_found_when_spotify_returns_404()
    {
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var client = new SpotifyPlaylistClient(httpClient);

        var result = await client.FetchAsync(PlaylistId, AccessToken, knownSnapshotId: null, CancellationToken.None);

        result.ShouldBeOfType<SpotifyPlaylistFetchResult.NotFound>();
    }

    [Fact]
    public async Task FetchAsync_throws_on_an_unexpected_server_error_instead_of_reporting_not_found()
    {
        // spec section 16.1: only an explicit "not found" sets is_available = false; a transient
        // failure must propagate instead of silently marking the playlist unavailable.
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var client = new SpotifyPlaylistClient(httpClient);

        await Should.ThrowAsync<HttpRequestException>(() =>
            client.FetchAsync(PlaylistId, AccessToken, knownSnapshotId: null, CancellationToken.None));
    }

    [Fact]
    public async Task FetchAsync_throws_on_403_instead_of_reporting_not_found()
    {
        // Regression test: a 403 from Spotify means insufficient scope/token, not "playlist
        // removed" - it must not be treated the same as a 404 (spec section 16.1). Only an
        // explicit 404 may set is_available = false; a 403 must propagate and fail the sync run.
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var client = new SpotifyPlaylistClient(httpClient);

        await Should.ThrowAsync<HttpRequestException>(() =>
            client.FetchAsync(PlaylistId, AccessToken, knownSnapshotId: null, CancellationToken.None));
    }

    [Fact]
    public async Task FetchAsync_honors_retry_after_and_retries_a_rate_limited_request()
    {
        var attempts = 0;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == $"/v1/playlists/{PlaylistId}")
            {
                attempts++;
                if (attempts == 1)
                {
                    var rateLimitedResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    rateLimitedResponse.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                        TimeSpan.Zero);
                    return rateLimitedResponse;
                }
            }

            return BuildTwoPageFoundResponder()(request);
        }));
        var client = new SpotifyPlaylistClient(httpClient);

        var result = await client.FetchAsync(PlaylistId, AccessToken, knownSnapshotId: null, CancellationToken.None);

        result.ShouldBeOfType<SpotifyPlaylistFetchResult.Found>();
        attempts.ShouldBe(2);
    }

    [Fact]
    public async Task FetchAsync_uses_a_fallback_delay_when_rate_limit_response_omits_retry_after()
    {
        var attempts = 0;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == $"/v1/playlists/{PlaylistId}")
            {
                attempts++;
                if (attempts == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                }
            }

            return BuildTwoPageFoundResponder()(request);
        }));
        var client = new SpotifyPlaylistClient(httpClient, rateLimitRetryDelay: TimeSpan.Zero);

        var result = await client.FetchAsync(PlaylistId, AccessToken, knownSnapshotId: null, CancellationToken.None);

        result.ShouldBeOfType<SpotifyPlaylistFetchResult.Found>();
        attempts.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FetchAsync_stops_without_retrying_when_spotify_requests_an_excessive_cooldown(bool useDate)
    {
        var attempts = 0;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ =>
        {
            attempts++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = useDate
                ? new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddHours(3))
                : new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(3));
            return response;
        }));
        var client = new SpotifyPlaylistClient(httpClient);

        var exception = await Should.ThrowAsync<HttpRequestException>(() =>
            client.FetchAsync(PlaylistId, AccessToken, knownSnapshotId: null, CancellationToken.None));

        exception.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        exception.Message.ShouldContain("cooldown");
        attempts.ShouldBe(1);
    }

    [Fact]
    public async Task FetchAsync_throws_after_exhausting_rate_limit_retries()
    {
        var attempts = 0;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        }));
        var client = new SpotifyPlaylistClient(httpClient, rateLimitRetryDelay: TimeSpan.Zero);

        var exception = await Should.ThrowAsync<HttpRequestException>(() =>
            client.FetchAsync(PlaylistId, AccessToken, knownSnapshotId: null, CancellationToken.None));

        exception.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        attempts.ShouldBe(5);
    }

    [Fact]
    public async Task FetchAsync_never_surfaces_a_track_level_field_even_though_the_raw_response_carries_one()
    {
        // Regression test for spec section 9.4/11.2: the raw Spotify "item" payload below carries
        // track id, duration and ISRC on purpose - SpotifyPlaylistSummary must never expose them,
        // only the distinct artist names and the aggregate track count.
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(BuildTwoPageFoundResponder()));
        var client = new SpotifyPlaylistClient(httpClient);

        var result = await client.FetchAsync(PlaylistId, AccessToken, knownSnapshotId: null, CancellationToken.None);

        var found = result.ShouldBeOfType<SpotifyPlaylistFetchResult.Found>();
        found.Summary.Artists.ShouldBe(["Eric Clapton", "Traffic"], ignoreOrder: true);
        found.Summary.TrackCount.ShouldBe(2); // the aggregate from the playlist endpoint, not a track array length
        typeof(SpotifyPlaylistSummary).GetProperties().Select(p => p.Name).ShouldBe(
            ["Name", "Description", "CoverImageUrl", "TrackCount", "Artists", "ComputedEras", "EraBucketCounts", "SnapshotId"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task FetchAsync_reads_artists_from_the_item_field_and_ignores_the_deprecated_empty_track_field()
    {
        // Regression test for the February 2026 Spotify API migration: "track" is still present on
        // each page item but is always an empty, deprecated object - the real artist data lives
        // under "item". If the mapping ever regresses to reading "track", this must fail because
        // Traffic/Eric Clapton would no longer be found (an empty object has no "artists").
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(BuildTwoPageFoundResponder()));
        var client = new SpotifyPlaylistClient(httpClient);

        var result = await client.FetchAsync(PlaylistId, AccessToken, knownSnapshotId: null, CancellationToken.None);

        var found = result.ShouldBeOfType<SpotifyPlaylistFetchResult.Found>();
        found.Summary.Artists.ShouldBe(["Eric Clapton", "Traffic"], ignoreOrder: true);
    }

    [Fact]
    public async Task FetchAsync_reduces_release_dates_to_year_and_merges_multiple_pages()
    {
        // US-023: exercises year-only, year+month and year+month+day precision on page one, and a
        // second page reached only via "next", proving pagination is followed to completion. The
        // years themselves never leave the client (spec 9.4/11.2), so the bucket counts it stores
        // are what proves each date was read and placed correctly: 1971 and 1978 into the 1970s,
        // 1985 into 1980s-1990s, 2010 into 2000s-present.
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(BuildReleaseYearResponder()));
        var client = new SpotifyPlaylistClient(httpClient);

        var result = await client.FetchAsync(PlaylistId, AccessToken, knownSnapshotId: null, CancellationToken.None);

        var found = result.ShouldBeOfType<SpotifyPlaylistFetchResult.Found>();
        found.Summary.EraBucketCounts.ShouldBe([0, 2, 1, 1]);
    }

    [Fact]
    public async Task FetchAsync_skips_tracks_with_a_missing_empty_or_absent_release_date()
    {
        // US-023: a missing "album", a missing "release_date" and an empty "release_date" (all
        // real Spotify shapes - podcast episodes/local files have no album at all) must not be
        // counted as a dated track, and must never surface as year 0.
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(request =>
        {
            var absolutePath = request.RequestUri!.AbsolutePath;
            if (absolutePath == $"/v1/playlists/{PlaylistId}/items")
            {
                return JsonResponse(
                    """
                    {
                      "items": [
                        { "item": { "artists": [] } },
                        { "item": { "album": {} } },
                        { "item": { "album": { "release_date": "" } } },
                        { "item": { "album": { "release_date": "1992" } } }
                      ],
                      "next": null
                    }
                    """);
            }

            if (absolutePath == $"/v1/playlists/{PlaylistId}")
            {
                return JsonResponse(PlaylistResponseJson);
            }

            throw new InvalidOperationException($"Unexpected request path '{absolutePath}'.");
        }));
        var client = new SpotifyPlaylistClient(httpClient);

        var result = await client.FetchAsync(PlaylistId, AccessToken, knownSnapshotId: null, CancellationToken.None);

        var found = result.ShouldBeOfType<SpotifyPlaylistFetchResult.Found>();
        found.Summary.EraBucketCounts.ShouldBe([0, 0, 1, 0]); // only 1992 counted, no year-zero entries
    }

    [Fact]
    public async Task FetchAsync_computes_eras_with_artists_in_one_paginated_pass_even_after_rate_limiting()
    {
        var firstPageCalls = 0;
        var secondPageCalls = 0;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == $"/v1/playlists/{PlaylistId}")
            {
                return JsonResponse(PlaylistResponseJson);
            }

            var secondPage = request.RequestUri.Query.Contains("offset=100");
            if (secondPage && ++secondPageCalls == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            }

            if (!secondPage)
            {
                firstPageCalls++;
                Uri.UnescapeDataString(request.RequestUri.Query)
                    .ShouldContain("artists(name),album(release_date)");
            }

            var date = secondPage ? "1985-01-01" : "1971-02-03";
            var items = Enumerable.Range(0, 6).Select(_ => new
            {
                item = new { artists = new[] { new { name = "Traffic" } }, album = new { release_date = date } },
            });
            var next = secondPage ? null : $"https://api.spotify.com/v1/playlists/{PlaylistId}/items?offset=100";
            return JsonResponse(System.Text.Json.JsonSerializer.Serialize(new { items, next }));
        }));
        var client = new SpotifyPlaylistClient(httpClient, rateLimitRetryDelay: TimeSpan.Zero);

        var result = await client.FetchAsync(PlaylistId, AccessToken, knownSnapshotId: null, CancellationToken.None);

        var found = result.ShouldBeOfType<SpotifyPlaylistFetchResult.Found>();
        found.Summary.Artists.ShouldBe(["Traffic"]);
        found.Summary.ComputedEras.ShouldBe(["1970s", "1980s-1990s", "mixed-era"]);
        firstPageCalls.ShouldBe(1);
        secondPageCalls.ShouldBe(2);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> BuildReleaseYearResponder() => request =>
    {
        var absolutePath = request.RequestUri!.AbsolutePath;
        var query = request.RequestUri.Query;

        if (absolutePath == $"/v1/playlists/{PlaylistId}")
        {
            return JsonResponse(PlaylistResponseJson);
        }

        if (absolutePath == $"/v1/playlists/{PlaylistId}/items" && query.Contains("offset=100"))
        {
            return JsonResponse(
                """
                {
                  "items": [
                    { "item": { "album": { "release_date": "2010-06" } } }
                  ],
                  "next": null
                }
                """);
        }

        if (absolutePath == $"/v1/playlists/{PlaylistId}/items")
        {
            return JsonResponse(
                """
                {
                  "items": [
                    { "item": { "album": { "release_date": "1971" } } },
                    { "item": { "album": { "release_date": "1978-05" } } },
                    { "item": { "album": { "release_date": "1985-11-02" } } }
                  ],
                  "next": "https://api.spotify.com/v1/playlists/2m8X8fsMWor8A5AnmOHwzy/items?offset=100&limit=100&fields=items(item(album(release_date))),next"
                }
                """);
        }

        throw new InvalidOperationException($"Unexpected request path '{absolutePath}{query}'.");
    };

    private static Func<HttpRequestMessage, HttpResponseMessage> BuildTwoPageFoundResponder() => request =>
    {
        var absolutePath = request.RequestUri!.AbsolutePath;
        var query = request.RequestUri.Query;

        if (absolutePath == $"/v1/playlists/{PlaylistId}")
        {
            return JsonResponse(PlaylistResponseJson);
        }

        if (absolutePath == $"/v1/playlists/{PlaylistId}/items" && query.Contains("offset=100"))
        {
            return JsonResponse("""{"items":[],"next":null}""");
        }

        if (absolutePath == $"/v1/playlists/{PlaylistId}/items")
        {
            return JsonResponse(ItemsPageOneJson);
        }

        throw new InvalidOperationException($"Unexpected request path '{absolutePath}{query}'.");
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private const string PlaylistResponseJson =
        """
        {
          "name": "Dear Mr. Fantasy",
          "description": "Blues rock for late nights.",
          "images": [{ "url": "https://i.scdn.co/image/cover.jpg", "height": 640, "width": 640 }],
          "items": { "total": 2 },
          "snapshot_id": "snapshot-abc"
        }
        """;

    // Post-February-2026 shape: "track" is present but deprecated and always empty ({}); the
    // real track/episode payload lives under "item" instead (see SpotifyPlaylistClient's XML doc).
    private const string ItemsPageOneJson =
        """
        {
          "items": [
            {
              "item": {
                "id": "track-id-1",
                "name": "Dear Mr. Fantasy",
                "duration_ms": 322000,
                "external_ids": { "isrc": "GBUM71029601" },
                "artists": [{ "name": "Traffic" }]
              },
              "track": {}
            },
            {
              "item": {
                "id": "track-id-2",
                "name": "Presence of the Lord",
                "duration_ms": 275000,
                "external_ids": { "isrc": "GBAYE0601234" },
                "artists": [{ "name": "Eric Clapton" }, { "name": "Traffic" }]
              },
              "track": {}
            }
          ],
          "next": "https://api.spotify.com/v1/playlists/2m8X8fsMWor8A5AnmOHwzy/items?offset=100&limit=100&fields=items(item(artists(name))),next"
        }
        """;
}
