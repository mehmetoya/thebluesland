using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TheBluesland.Web;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// docs/specs/playlists-json-feed.md Testing Strategy: starts the real host in-process (same pattern
/// as WebHostIntegrationTests.cs) over a temp content directory the test writes itself - three
/// published playlists (two sharing a date, to exercise the title tie-break) and one draft - with
/// deliberately unreachable databases, so the cache-backed fields degrade to "omitted" exactly as
/// they do on the cards. The mapping details live in PlaylistsFeedBuilderTests; this class proves
/// the wiring: status, headers, method handling, sitemap agreement and that every URL resolves.
/// </summary>
public sealed partial class PlaylistsFeedIntegrationTests : IAsyncLifetime
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=thebluesland;Timeout=2";
    private const string PublicOrigin = "https://thebluesland.com";

    private string _contentDirectory = null!;
    private WebApplication _app = null!;
    private HttpClient _httpClient = null!;

    [GeneratedRegex("<loc>[^<]*/playlists/[^<]*</loc>")]
    private static partial Regex SitemapPlaylistLocation();

    public async Task InitializeAsync()
    {
        _contentDirectory = Path.Combine(Path.GetTempPath(), $"playlists-feed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentDirectory);
        WriteContent("alpha-blues", "aaaaaaaaaaaaaaaaaaaaaa", "Alpha Blues", "blues", "published", "2026-03-01");
        WriteContent("bravo-warm", "bbbbbbbbbbbbbbbbbbbbbb", "bravo warm", "jazz", "published", "2026-03-01", mood: "warm");
        WriteContent("charlie-old", "cccccccccccccccccccccc", "Charlie's Old Ones", "folk", "published", "2025-12-24");
        WriteContent("delta-draft", "dddddddddddddddddddddd", "Delta Draft", "rock", "draft", publishedAt: null);

        _app = WebHostFactory.Create([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] = _contentDirectory;
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] = UnreachableConnectionString;
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.AnalyticsConnectionStringName}"] = UnreachableConnectionString;
            builder.Configuration["Site:PublicOrigin"] = PublicOrigin;
        });

        await _app.StartAsync();

        var addressesFeature = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        _httpClient = new HttpClient { BaseAddress = new Uri(addressesFeature!.Addresses.First()) };
    }

    public async Task DisposeAsync()
    {
        _httpClient.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        Directory.Delete(_contentDirectory, recursive: true);
    }

    [Fact]
    public async Task Feed_returns_200_json_with_the_contract_headers_and_no_cors()
    {
        using var response = await _httpClient.GetAsync("/playlists.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().ShouldBe("application/json; charset=utf-8");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("public, max-age=300");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task Feed_lists_only_published_playlists_newest_first_with_the_canonical_origin()
    {
        using var json = await GetFeedAsync();

        var items = json.RootElement.GetProperty("playlists").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("slug").GetString()).ShouldBe(["alpha-blues", "bravo-warm", "charlie-old"]);
        items.Select(item => item.GetProperty("addedAt").GetString()).ShouldBe(["2026-03-01", "2026-03-01", "2025-12-24"]);
        items.Select(item => item.GetProperty("url").GetString()).ShouldBe(
        [
            $"{PublicOrigin}/playlists/alpha-blues",
            $"{PublicOrigin}/playlists/bravo-warm",
            $"{PublicOrigin}/playlists/charlie-old",
        ]);
        items.Select(item => item.GetProperty("title").GetString()).ShouldContain("Charlie's Old Ones");
    }

    [Fact]
    public async Task Feed_degrades_to_omitting_cache_backed_fields_when_the_database_is_unreachable()
    {
        using var json = await GetFeedAsync();

        foreach (var item in json.RootElement.GetProperty("playlists").EnumerateArray())
        {
            item.TryGetProperty("trackCount", out _).ShouldBeFalse();
            item.TryGetProperty("image", out _).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Feed_reports_collection_membership_for_playlists_in_a_collection()
    {
        using var json = await GetFeedAsync();

        var bySlug = json.RootElement.GetProperty("playlists").EnumerateArray()
            .ToDictionary(item => item.GetProperty("slug").GetString()!);
        bySlug["alpha-blues"].GetProperty("collections").EnumerateArray().Select(value => value.GetString())
            .ShouldBe(["blues"]);
        bySlug["bravo-warm"].GetProperty("collections").EnumerateArray().Select(value => value.GetString())
            .ShouldBe(["warm"]);
        bySlug["charlie-old"].TryGetProperty("collections", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Feed_count_equals_the_number_of_playlist_urls_in_the_sitemap()
    {
        using var json = await GetFeedAsync();
        var sitemap = await _httpClient.GetStringAsync("/sitemap.xml");

        var feedCount = json.RootElement.GetProperty("playlists").GetArrayLength();

        feedCount.ShouldBe(3);
        SitemapPlaylistLocation().Matches(sitemap).Count.ShouldBe(feedCount);
    }

    [Fact]
    public async Task Every_feed_url_resolves_to_a_real_page()
    {
        using var json = await GetFeedAsync();

        foreach (var item in json.RootElement.GetProperty("playlists").EnumerateArray())
        {
            var path = new Uri(item.GetProperty("url").GetString()!).AbsolutePath;
            using var page = await _httpClient.GetAsync(path);
            page.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    public async Task Feed_keeps_the_sites_get_only_behavior_for_other_methods(string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/playlists.json");

        using var response = await _httpClient.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    private async Task<JsonDocument> GetFeedAsync()
    {
        var body = await _httpClient.GetStringAsync("/playlists.json");
        return JsonDocument.Parse(body);
    }

    private void WriteContent(
        string slug,
        string spotifyPlaylistId,
        string title,
        string genre,
        string status,
        string? publishedAt,
        string? mood = null)
    {
        var moodBlock = mood is null ? "moods: []\n" : $"moods:\n  - {mood}\n";
        var publishedBlock = publishedAt is null ? string.Empty : $"publishedAt: {publishedAt}\n";
        var content =
            $"""
            ---
            schemaVersion: 1
            slug: {slug}
            spotifyPlaylistId: {spotifyPlaylistId}
            title: {title}
            summary: A plain-text summary that is comfortably longer than forty characters.
            {moodBlock}genres:
              - {genre}
            occasions:
              - night-drive
            eras:
              - mixed-era
            status: {status}
            {publishedBlock}---

            Curator note body for the {slug} fixture.
            """;
        File.WriteAllText(Path.Combine(_contentDirectory, $"{slug}.md"), content);
    }
}
