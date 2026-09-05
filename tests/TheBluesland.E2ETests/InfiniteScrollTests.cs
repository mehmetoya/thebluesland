using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Shouldly;
using TheBluesland.Web;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.E2ETests;

/// <summary>
/// US-019 follow-up: the original "Show more playlists" link only loaded more content on a real
/// click, which is not what "sonsuz scroll" (infinite scroll) means - the whole point is loading
/// with no pointer interaction at all. wwwroot/js/infinite-scroll.js progressively enhances the
/// link with IntersectionObserver + fetch; this is the one test tier that can actually prove that
/// (a real Chromium browser executing real JavaScript), same rationale as SmokeTests. The 26-file
/// fixture set (PageSize=24 + 2) mirrors TheBluesland.UnitTests' pagination fixture.
/// </summary>
public sealed class InfiniteScrollTests : IAsyncLifetime
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=thebluesland;Timeout=2";

    private WebApplication _app = null!;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private string _baseAddress = null!;

    public async Task InitializeAsync()
    {
        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists-infinite-scroll");

        // See WebHostFactory.Create's webRootPath parameter doc: a referenced project's wwwroot
        // isn't copied into this test project's own output directory, so without this the browser
        // would get a real 404 for /js/infinite-scroll.js instead of exercising it.
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
        _baseAddress = addressesFeature!.Addresses.First();

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Scrolling_the_show_more_link_into_view_loads_the_rest_with_no_click()
    {
        var page = await _browser.NewPageAsync();
        await page.GotoAsync(_baseAddress);

        (await page.GetByText("Infinite Scroll Fixture 25").CountAsync()).ShouldBe(0);
        (await page.Locator(".playlist-card-link").CountAsync()).ShouldBe(24);

        // Never click - scrolling the trigger into view is the only interaction. The
        // IntersectionObserver's rootMargin fires before it is fully in the viewport, matching
        // real scroll-triggered loading rather than a click.
        await page.Locator(".load-more").ScrollIntoViewIfNeededAsync();

        await page.GetByText("Infinite Scroll Fixture 26").WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        (await page.Locator(".playlist-card-link").CountAsync()).ShouldBe(26);
        (await page.Locator(".load-more").CountAsync()).ShouldBe(0);
    }

    /// <summary>
    /// The enhancement must never be the only way to load more content - with JavaScript fully
    /// disabled (not just "the script failed to run" but disabled at the browser level), a real
    /// click on the same link must still work exactly as it did before infinite-scroll existed.
    /// </summary>
    [Fact]
    public async Task Show_more_link_still_works_by_clicking_when_javascript_is_disabled()
    {
        await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions { JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_baseAddress);

        (await page.Locator(".playlist-card-link").CountAsync()).ShouldBe(24);

        await page.Locator(".load-more").ClickAsync();

        // Waiting for this locator (rather than an explicit navigation wait, which is obsolete on
        // IPage) inherently waits out the full-page reload the click triggers with JS disabled -
        // the text can't exist until the new page has actually loaded.
        await page.GetByText("Infinite Scroll Fixture 26").WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        (await page.Locator(".playlist-card-link").CountAsync()).ShouldBe(26);
    }
}
