using System.Net;
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
/// US-019: end-to-end proof that the home page's zero-JS "Show more" link actually limits the
/// initial render to PageSize playlists and reveals the rest via plain GET navigation, via a real
/// in-process host (same approach as HomePageFilterIntegrationTests). The dedicated fixture set
/// (Fixtures/content-playlists-pagination, 26 playlists - one more than PageSize=24) is its own
/// directory so exact-count assertions here stay independent of every other home page test.
/// </summary>
public sealed class HomePagePaginationIntegrationTests : IAsyncLifetime
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=thebluesland;Timeout=2";

    private WebApplication _app = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists-pagination");

        // See WebHostFactory.Create's webRootPath parameter doc: a referenced project's wwwroot
        // isn't copied into this test project's own output directory, so UseStaticFiles() needs
        // to be pointed at the real one for the infinite-scroll script test below to actually
        // exercise a served file rather than a 404.
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var webRootPath = Path.Combine(repoRoot, "src", "TheBluesland.Web", "wwwroot");

        _app = WebHostFactory.Create(
            [],
            builder =>
            {
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] = contentDirectory;
                builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] = UnreachableConnectionString;
            },
            webRootPath);

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
    public async Task HomePage_with_no_page_query_shows_only_the_first_PageSize_playlists_with_a_show_more_link()
    {
        var response = await _httpClient.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("Pagination Fixture 01");
        body.ShouldContain("Pagination Fixture 24");
        body.ShouldNotContain("Pagination Fixture 25");
        body.ShouldNotContain("Pagination Fixture 26");
        body.ShouldContain("class=\"load-more\"");
    }

    /// <summary>
    /// US-019 AC5: the "page" query string value is the amount of content loaded - opening
    /// "?page=2" directly (as if a shared deep link, or the "Show more" link's own href) must
    /// reproduce exactly the cumulative set a visitor clicking "Show more" once would see.
    /// </summary>
    [Fact]
    public async Task HomePage_with_page_2_shows_every_remaining_playlist_and_hides_the_show_more_link()
    {
        var response = await _httpClient.GetAsync("/?page=2");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("Pagination Fixture 01");
        body.ShouldContain("Pagination Fixture 25");
        body.ShouldContain("Pagination Fixture 26");
        body.ShouldNotContain("class=\"load-more\"");
    }

    [Fact]
    public async Task HomePage_show_more_link_points_to_page_2()
    {
        var response = await _httpClient.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("href=\"/?page=2\" class=\"load-more\"");
    }

    /// <summary>
    /// US-019: infinite-scroll is progressive enhancement over the "Show more" link above, not a
    /// replacement for it - the home page must reference the script, and the script itself must
    /// actually be served (a 404 here would silently break scroll-triggered loading for every
    /// visitor while every other test in this class kept passing, since they only exercise the
    /// zero-JS path).
    /// </summary>
    [Fact]
    public async Task HomePage_references_the_infinite_scroll_script_and_it_is_actually_served()
    {
        var pageResponse = await _httpClient.GetAsync("/");
        var pageBody = await pageResponse.Content.ReadAsStringAsync();
        pageBody.ShouldContain("<script src=\"/js/infinite-scroll.js\" defer></script>");

        var scriptResponse = await _httpClient.GetAsync("/js/infinite-scroll.js");
        scriptResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var scriptBody = await scriptResponse.Content.ReadAsStringAsync();
        scriptBody.ShouldContain("IntersectionObserver");
    }
}
