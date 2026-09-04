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
/// US-011 AC1/FR-030: proves each indexable page (<c>/</c>, <c>/playlists/{slug}</c>, <c>/about</c>,
/// <c>/privacy</c>, <c>/terms</c>) renders its own unique &lt;title&gt;, a meta description, a
/// canonical link and Open Graph/Twitter card tags, via the real in-process host (same approach as
/// WebHostIntegrationTests). Also proves spec 14's "canonicalise filtered catalogue views to the
/// base catalogue" rule for the home page's query-string-driven filter (US-009).
/// </summary>
public sealed class PageMetadataIntegrationTests : IAsyncLifetime
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

    [Theory]
    [InlineData("/", "<title>TheBluesland</title>")]
    [InlineData("/playlists/masterpieces-of-erkin-the-father", "<title>Masterpieces of Erkin the Father - TheBluesland</title>")]
    [InlineData("/about", "<title>About - TheBluesland</title>")]
    [InlineData("/privacy", "<title>Privacy - TheBluesland</title>")]
    [InlineData("/terms", "<title>Terms - TheBluesland</title>")]
    public async Task IndexablePage_returns_its_own_unique_title_description_canonical_and_social_tags(
        string path, string expectedTitleTag)
    {
        var response = await _httpClient.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain(expectedTitleTag);
        body.ShouldContain("<meta name=\"description\"");
        body.ShouldContain("<link rel=\"canonical\" href=\"http://");
        body.ShouldContain("property=\"og:title\"");
        body.ShouldContain("property=\"og:description\"");
        body.ShouldContain("property=\"og:type\" content=\"website\"");
        body.ShouldContain("property=\"og:url\"");
        body.ShouldContain("property=\"og:image\"");
        body.ShouldContain("name=\"twitter:card\" content=\"summary_large_image\"");
        body.ShouldContain("name=\"twitter:title\"");
        body.ShouldContain("name=\"twitter:description\"");
        body.ShouldContain("name=\"twitter:image\"");
    }

    /// <summary>US-011 AC3/FR-031: never the Spotify-hosted cover image URL as og:image/twitter:image.</summary>
    [Fact]
    public async Task PlaylistDetailPage_social_image_is_a_TheBluesland_owned_endpoint_not_a_spotify_url()
    {
        var response = await _httpClient.GetAsync("/playlists/masterpieces-of-erkin-the-father");
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("property=\"og:image\" content=\"http://127.0.0.1");
        body.ShouldContain("/playlists/masterpieces-of-erkin-the-father/og-image.png");
        body.ShouldNotContain("scdn.co");
    }

    /// <summary>
    /// Regression test: App.razor previously had both a static &lt;title&gt; and &lt;HeadOutlet /&gt;,
    /// which rendered two &lt;title&gt; elements per page (one static, one from the page's own
    /// &lt;PageTitle&gt;) - a real problem for AC1's "unique &lt;title&gt;" once every page also
    /// started rendering its own PageTitle. Only the page's own PageTitle content must appear now.
    /// </summary>
    [Fact]
    public async Task IndexablePage_renders_exactly_one_title_element()
    {
        var response = await _httpClient.GetAsync("/about");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var titleCount = body.Split("<title>", StringSplitOptions.None).Length - 1;
        titleCount.ShouldBe(1);
    }

    /// <summary>Spec 14: a filtered home page URL must canonicalise to the bare base catalogue, not itself.</summary>
    [Fact]
    public async Task HomePage_canonical_link_stays_the_bare_home_page_even_with_active_filter_query_params()
    {
        var response = await _httpClient.GetAsync("/?mood=warm&genre=blues&occasion=late-night&era=1970s");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var expectedCanonical = $"<link rel=\"canonical\" href=\"{_httpClient.BaseAddress}\" />";
        body.ShouldContain(expectedCanonical);
    }
}
