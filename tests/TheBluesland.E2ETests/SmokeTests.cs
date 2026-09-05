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
/// US-013 AC1 (spec 12.2, "Browser tests"): a real Chromium browser drives the compiled app end to
/// end - deliberately minimal ("CI's smoke step", per the story), not a full E2E suite (that stays
/// test-engineer's job per CLAUDE.md's agent budget). Deeper HTTP-level scenarios (404s, security
/// headers, DB-unreachable degradation) already live in
/// TheBluesland.UnitTests/Web/WebHostIntegrationTests.cs; this project only proves the same
/// in-process host (WebHostFactory, no WebApplicationFactory/TestHost package) also renders
/// correctly and is navigable in a real browser.
/// </summary>
public sealed class SmokeTests : IAsyncLifetime
{
    // Same pattern as WebHostIntegrationTests: nothing listens on loopback port 1, so the DB
    // connection fails fast without a real Postgres instance - this project never needs Testcontainers.
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=thebluesland;Timeout=2";

    private WebApplication _app = null!;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private string _baseAddress = null!;

    public async Task InitializeAsync()
    {
        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists");
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var webRootPath = Path.Combine(repoRoot, "src", "TheBluesland.Web", "wwwroot");

        _app = WebHostFactory.Create([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] = contentDirectory;
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] = UnreachableConnectionString;
        }, webRootPath);

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
    public async Task Share_links_and_clipboard_fallback_use_the_canonical_playlist_url()
    {
        var page = await _browser.NewPageAsync();
        await page.AddInitScriptAsync("Object.defineProperty(navigator, 'clipboard', { value: { writeText: async () => { throw new Error('Denied'); } } });");
        var url = _baseAddress + "/playlists/masterpieces-of-erkin-the-father";
        await page.GotoAsync(url + "?utm_source=test");
        await page.GetByText("Share playlist", new() { Exact = true }).ClickAsync();
        var share = page.Locator(".playlist-share");
        (await share.Locator("a").First.GetAttributeAsync("href") ?? string.Empty).ShouldContain(Uri.EscapeDataString(url));
        (await share.Locator("a").Last.GetAttributeAsync("href") ?? string.Empty).ShouldContain(Uri.EscapeDataString(url));
        await page.GetByRole(AriaRole.Button, new() { Name = "Copy link" }).ClickAsync();
        await Assertions.Expect(share.Locator("[role=status]")).ToHaveTextAsync("Copy the selected link to share this playlist.");
        (await share.Locator("input").InputValueAsync()).ShouldBe(url);
        (await share.Locator("input").EvaluateAsync<bool>("input => document.activeElement === input && input.selectionEnd === input.value.length")).ShouldBeTrue();
    }

    [Fact]
    public async Task Native_share_cancellation_is_quiet_and_copy_reports_success()
    {
        var page = await _browser.NewPageAsync();
        await page.AddInitScriptAsync("""
            Object.defineProperty(navigator, 'share', { value: async data => {
                window.sharedPlaylist = data;
                throw new DOMException('Cancelled', 'AbortError');
            } });
            Object.defineProperty(navigator, 'clipboard', { value: { writeText: async text => { window.copiedPlaylist = text; } } });
            """);
        var url = _baseAddress + "/playlists/masterpieces-of-erkin-the-father";
        await page.GotoAsync(url);
        await page.GetByText("Share playlist", new() { Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Share via device" }).ClickAsync();
        (await page.EvaluateAsync<string>("window.sharedPlaylist.url")).ShouldBe(url);
        await Assertions.Expect(page.Locator("[data-share-status]")).ToBeEmptyAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Copy link" }).ClickAsync();
        await Assertions.Expect(page.Locator("[data-share-status]")).ToHaveTextAsync("Link copied.");
        (await page.EvaluateAsync<string>("window.copiedPlaylist")).ShouldBe(url);
    }

    [Fact]
    public async Task HomePage_renders_the_published_catalogue_in_a_real_browser()
    {
        var page = await _browser.NewPageAsync();

        var response = await page.GotoAsync(_baseAddress);

        response.ShouldNotBeNull();
        response!.Status.ShouldBe(200);
        (await page.Locator("h1").TextContentAsync()).ShouldBe("TheBluesland");
        (await page.TextContentAsync("body") ?? string.Empty).ShouldContain("Masterpieces of Erkin the Father");
    }

    [Fact]
    public async Task Visitor_can_click_from_the_home_page_into_a_playlist_detail_page()
    {
        var page = await _browser.NewPageAsync();
        await page.GotoAsync(_baseAddress);

        await page.GetByText("Masterpieces of Erkin the Father").ClickAsync();

        page.Url.ShouldEndWith("/playlists/masterpieces-of-erkin-the-father");
        (await page.TextContentAsync("body") ?? string.Empty).ShouldContain("Curator note body");
    }
}
