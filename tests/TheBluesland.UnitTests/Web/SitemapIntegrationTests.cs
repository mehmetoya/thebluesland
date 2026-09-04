using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TheBluesland.Web;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-011 AC2/spec 14: <c>/sitemap.xml</c> must list only <c>status: published</c> playlists (never
/// a draft, never a filtered query-string variation) plus the fixed static pages. Uses the same
/// <c>Fixtures/content-playlists</c> set as WebHostIntegrationTests - one published fixture
/// (masterpieces-of-erkin-the-father) and two drafts (dear-mr-fantasy,
/// masterpieces-of-erkin-the-father-alt-slug).
/// </summary>
public sealed class SitemapIntegrationTests : IAsyncLifetime
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=thebluesland;Timeout=2";

    private WebApplication _app = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists");

        _app = WebHostFactory.Create([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration[TheBluesland.Web.Content.PlaylistContentRepository.ContentDirectoryConfigKey] = contentDirectory;
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] = UnreachableConnectionString;
        });

        await _app.StartAsync();

        var addressesFeature = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var address = addressesFeature!.Addresses.First();
        _httpClient = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _httpClient.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Sitemap_includes_the_published_playlist_and_the_static_pages()
    {
        var response = await _httpClient.GetAsync("/sitemap.xml");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/xml");
        body.ShouldContain("<loc>http://127.0.0.1");
        body.ShouldContain("/playlists/masterpieces-of-erkin-the-father</loc>");
        body.ShouldContain("/about</loc>");
        body.ShouldContain("/privacy</loc>");
        body.ShouldContain("/terms</loc>");
    }

    [Fact]
    public async Task Sitemap_excludes_draft_playlists()
    {
        var response = await _httpClient.GetAsync("/sitemap.xml");
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain("dear-mr-fantasy");
        body.ShouldNotContain("masterpieces-of-erkin-the-father-alt-slug");
    }

    [Fact]
    public async Task Sitemap_never_includes_a_filtered_query_string_variation()
    {
        var response = await _httpClient.GetAsync("/sitemap.xml");
        var body = await response.Content.ReadAsStringAsync();

        // Only the leading `<?xml ... ?>` declaration may contain a `?`; no `<loc>` entry may.
        var locations = body
            .Split("<loc>", StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(entry => entry[..entry.IndexOf("</loc>", StringComparison.Ordinal)]);

        foreach (var location in locations)
        {
            location.ShouldNotContain("?");
        }
    }

    [Fact]
    public async Task RobotsTxt_allows_crawling_and_references_the_sitemap_url()
    {
        var response = await _httpClient.GetAsync("/robots.txt");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("User-agent: *");
        body.ShouldContain("Allow: /");
        body.ShouldContain("Sitemap: http://127.0.0.1");
        body.ShouldContain("/sitemap.xml");
    }
}
