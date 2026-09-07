using System.Net;
using System.Text;
using Shouldly;
using TheBluesland.SpotifyFetcher.Content;
using TheBluesland.SpotifyFetcher.EraReport;
using TheBluesland.SpotifyFetcher.Spotify;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// US-023: <see cref="PlaylistEraReportService"/> builds its Markdown report only from
/// <see cref="TheBluesland.SpotifyFetcher.EraReport.PlaylistEraDistributionResult"/>'s aggregate
/// numbers - the regression test below proves that even though the mocked Spotify response
/// carries track id/title/ISRC/exact release date on purpose (the same style of fixture as
/// SpotifyPlaylistClientTests' own track-level regression test), none of it survives into the
/// report text (ADR-0002/spec section 9.4/11.2: no track-level data is ever persisted or output).
/// </summary>
public sealed class PlaylistEraReportServiceTests
{
    private const string PlaylistId = "2m8X8fsMWor8A5AnmOHwzy";
    private const string AccessToken = "mocked-access-token";

    [Fact]
    public async Task BuildReportAsync_never_surfaces_any_track_title_id_isrc_or_raw_release_date()
    {
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(BuildDominantSeventiesResponder()));
        var playlistClient = new SpotifyPlaylistClient(httpClient);
        var service = new PlaylistEraReportService(playlistClient);

        var playlists = new[]
        {
            new PlaylistFrontMatterEntry(PlaylistId, "dear-mr-fantasy", ["mixed-era"]),
        };

        var report = await service.BuildReportAsync(playlists, AccessToken, CancellationToken.None);

        // Track-level fields present on purpose in the mocked response - must never leak.
        report.ShouldNotContain("track-id-1");
        report.ShouldNotContain("Presence of the Lord (Live)");
        report.ShouldNotContain("GBUM71099999");
        report.ShouldNotContain("1978-05-12"); // the exact, unaggregated release date

        // Only the aggregate suggestion (slug, dated-track count, bucket percentages, suggested
        // eras) may appear.
        report.ShouldContain("dear-mr-fantasy");
        report.ShouldContain("Dated tracks read: 10");
        report.ShouldContain(EraBucketMapper.Seventies);
        report.ShouldContain("Suggested eras:");
    }

    [Fact]
    public async Task BuildReportAsync_reports_insufficient_data_and_no_suggestion_below_ten_dated_tracks()
    {
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(request =>
        {
            var absolutePath = request.RequestUri!.AbsolutePath;
            if (absolutePath == $"/v1/playlists/{PlaylistId}/items")
            {
                return JsonResponse(
                    """
                    {
                      "items": [
                        { "item": { "album": { "release_date": "1978" } } }
                      ],
                      "next": null
                    }
                    """);
            }

            throw new InvalidOperationException($"Unexpected request path '{absolutePath}'.");
        }));
        var playlistClient = new SpotifyPlaylistClient(httpClient);
        var service = new PlaylistEraReportService(playlistClient);

        var playlists = new[]
        {
            new PlaylistFrontMatterEntry(PlaylistId, "one-track-wonder", []),
        };

        var report = await service.BuildReportAsync(playlists, AccessToken, CancellationToken.None);

        report.ShouldContain("one-track-wonder");
        report.ShouldContain("Insufficient data");
        report.ShouldNotContain("Suggested eras:");
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> BuildDominantSeventiesResponder() => request =>
    {
        var absolutePath = request.RequestUri!.AbsolutePath;
        if (absolutePath != $"/v1/playlists/{PlaylistId}/items")
        {
            throw new InvalidOperationException($"Unexpected request path '{absolutePath}'.");
        }

        // 7 of 10 tracks land in the 1970s (>=60% majority - mixed-era must not be suggested);
        // the other 3 land in 2000s-present (>=20% - suggested alongside the 1970s).
        var items = new StringBuilder();
        items.Append(
            """
            { "item": { "id": "track-id-1", "name": "Presence of the Lord (Live)",
              "external_ids": { "isrc": "GBUM71099999" },
              "album": { "release_date": "1978-05-12" } } },
            """);
        for (var i = 0; i < 6; i++)
        {
            items.Append($$"""{ "item": { "album": { "release_date": "197{{i % 10}}" } } },""");
        }

        for (var i = 0; i < 3; i++)
        {
            items.Append($$"""{ "item": { "album": { "release_date": "201{{i}}" } } },""");
        }

        var json = $$"""{ "items": [ {{items.ToString().TrimEnd(',')}} ], "next": null }""";
        return JsonResponse(json);
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
