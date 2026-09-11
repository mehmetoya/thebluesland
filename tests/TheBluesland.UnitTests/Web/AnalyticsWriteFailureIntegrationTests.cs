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
/// docs/specs/visitor-and-playlist-click-analytics.md Testing Strategy: an unreachable Analytics
/// connection string must never throw an exception out of a request - mirrors
/// PlaylistCacheLookupTests' DB-unreachable graceful-degradation tests, but for the write path. The
/// page must still render/redirect normally even though the fire-and-forget write silently fails.
/// </summary>
public sealed class AnalyticsWriteFailureIntegrationTests : IAsyncLifetime
{
    // Nothing listens on loopback port 1 (tcpmux); connection attempts fail fast and reliably.
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=thebluesland;Timeout=2";
    private const string KnownSlug = "masterpieces-of-erkin-the-father";
    private const string KnownSpotifyPlaylistId = "0iJt9LMebhOY0KSHSJw3cS";

    private WebApplication _app = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists");

        _app = WebHostFactory.Create([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] = contentDirectory;
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] = UnreachableConnectionString;
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.AnalyticsConnectionStringName}"] = UnreachableConnectionString;
        });

        await _app.StartAsync();

        var addressesFeature = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var address = addressesFeature!.Addresses.First();

        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _httpClient.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task HomePage_renders_normally_when_the_analytics_database_is_unreachable()
    {
        var response = await _httpClient.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("TheBluesland");

        // Gives the fire-and-forget write a moment to fail in the background; the request above
        // must already have completed successfully regardless of what happens here.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task Out_redirect_still_redirects_when_the_analytics_database_is_unreachable()
    {
        var response = await _httpClient.GetAsync($"/out/{KnownSlug}");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe($"https://open.spotify.com/playlist/{KnownSpotifyPlaylistId}");
    }

    [Fact]
    public async Task Out_redirect_for_an_unknown_slug_still_404s_when_the_analytics_database_is_unreachable()
    {
        var response = await _httpClient.GetAsync("/out/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
