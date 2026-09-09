using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using TheBluesland.Web;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

public sealed class SearchReadinessIntegrationTests : IAsyncLifetime
{
    private const string Origin = "https://thebluesland.onrender.com";
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _app = WebHostFactory.Create([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] =
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists");
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] =
                "Host=127.0.0.1;Port=1;Username=test;Password=test;Database=test;Timeout=1";
            builder.Configuration["SearchVerification:Google"] = "test-google-verification";
            builder.Configuration["SearchVerification:Bing"] = "test-bing-verification";
        });
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.Single()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Verification_tags_are_present_in_the_server_rendered_head()
    {
        var html = await _client.GetStringAsync("/");
        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];
        head.ShouldContain("name=\"google-site-verification\" content=\"test-google-verification\"");
        head.ShouldContain("name=\"msvalidate.01\" content=\"test-bing-verification\"");
        head.ShouldContain("rel=\"canonical\"");
    }

    [Theory]
    [InlineData("anadolu-rock", "Anadolu Rock Playlists")]
    [InlineData("blues", "Blues Playlists")]
    [InlineData("late-night", "Late-Night Playlists")]
    public async Task Collection_has_its_own_canonical_title_and_description(string slug, string title)
    {
        using var response = await _client.GetAsync($"/collections/{slug}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain($"<h1>{title}</h1>");
        html.ShouldContain($"href=\"{Origin}/collections/{slug}\"");
        html.ShouldContain($"<title>{title} - TheBluesland</title>");
        html.ShouldNotContain("Dear Mr. Fantasy");
    }

    [Fact]
    public async Task Collection_matches_editorial_tags_and_never_lists_drafts()
    {
        var html = await _client.GetStringAsync("/collections/anadolu-rock");
        html.ShouldContain("Masterpieces of Erkin the Father");
        html.ShouldNotContain("Dear Mr. Fantasy");
        var blues = await _client.GetStringAsync("/collections/blues");
        blues.ShouldNotContain("Masterpieces of Erkin the Father");
    }

    [Fact]
    public async Task Unknown_collection_returns_404()
    {
        using var response = await _client.GetAsync("/collections/not-a-collection");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Collections_are_linked_from_the_hub_and_included_once_in_sitemap()
    {
        var hub = await _client.GetStringAsync("/collections");
        var sitemap = XDocument.Parse(await _client.GetStringAsync("/sitemap.xml"));
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var urls = sitemap.Descendants(ns + "loc").Select(node => node.Value).ToList();
        urls.Count.ShouldBe(urls.Distinct().Count());
        urls.ShouldContain(Origin + "/collections");
        urls.Count(url => url.StartsWith($"{Origin}/collections/", StringComparison.Ordinal))
            .ShouldBe(PlaylistCollections.All.Count);
        foreach (var collection in PlaylistCollections.All)
        {
            hub.ShouldContain($"href=\"/collections/{collection.Slug}\"");
            urls.ShouldContain($"{Origin}/collections/{collection.Slug}");
        }
    }
}
