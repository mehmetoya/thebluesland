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
/// US-005 acceptance criteria 3-4: starts the real ASP.NET Core host (Kestrel, health checks,
/// the /playlists/{slug} Razor component route) in-process on an ephemeral loopback port with a
/// deliberately unreachable database connection string, then hits it with a real HttpClient. This
/// is a genuine integration test without adding a WebApplicationFactory/TestHost NuGet package
/// (see WebHostFactory).
/// </summary>
public sealed class WebHostIntegrationTests : IAsyncLifetime
{
    // Nothing listens on loopback port 1 (tcpmux); connection attempts fail fast and reliably.
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
    public async Task HealthLive_always_reports_healthy()
    {
        var response = await _httpClient.GetAsync("/health/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_reports_healthy_even_though_the_database_is_unreachable()
    {
        var response = await _httpClient.GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The /health/cache diagnostic endpoint: unlike /health/ready (FR-024), this one is
    /// deliberately DB-dependent, so an unreachable database must be reported as
    /// reachable:false (with a safe, generic error type, never the connection string or a raw
    /// exception message) rather than crashing the request.
    /// </summary>
    [Fact]
    public async Task HealthCache_reports_unreachable_when_the_database_cannot_be_reached()
    {
        var response = await _httpClient.GetAsync("/health/cache");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("\"reachable\":false");
        body.ShouldNotContain("127.0.0.1");
        body.ShouldNotContain("postgres");
    }

    [Fact]
    public async Task PlaylistDetailPage_returns_200_with_editorial_content_when_the_database_is_unreachable()
    {
        var response = await _httpClient.GetAsync("/playlists/masterpieces-of-erkin-the-father");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("Masterpieces of Erkin the Father");
        body.ShouldContain("currently unavailable");
    }

    /// <summary>
    /// US-008 AC4: an unknown slug must 404, not silently 200 (and not 500). The body is empty by
    /// ASP.NET Core Razor Components framework design: a non-streaming static SSR response whose
    /// status code is set to a non-2xx value has its rendered HTML discarded (verified empirically -
    /// setting the same branch's status to 200 instead renders "Playlist not found." normally).
    /// </summary>
    [Fact]
    public async Task PlaylistDetailPage_returns_404_for_an_unknown_slug()
    {
        var response = await _httpClient.GetAsync("/playlists/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>US-008 AC1/AC2: home page is readable static SSR content, not an empty shell.</summary>
    [Fact]
    public async Task HomePage_returns_200_with_published_playlist_content()
    {
        var response = await _httpClient.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("TheBluesland");
        body.ShouldContain("Masterpieces of Erkin the Father");
    }

    [Theory]
    [InlineData("/about")]
    [InlineData("/privacy")]
    [InlineData("/terms")]
    public async Task StaticPage_returns_200_with_readable_content(string path)
    {
        var response = await _httpClient.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("<p>");
    }

    /// <summary>US-008 AC1: an unregistered route must still 404 through Router's NotFound branch.</summary>
    [Fact]
    public async Task UnknownRoute_returns_404()
    {
        var response = await _httpClient.GetAsync("/this-route-does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>US-012 AC1/spec 13 SEC-002/SEC-005: every response - including a 200 - carries the
    /// security-header set, and the CSP grants Spotify only the `frame-src` it needs for the
    /// click-to-load embed (spec 12.4(a)), never a broad/wildcard origin.</summary>
    [Fact]
    public async Task HomePage_response_includes_the_required_security_headers()
    {
        var response = await _httpClient.GetAsync("/");

        response.Headers.TryGetValues("X-Content-Type-Options", out var contentTypeOptions).ShouldBeTrue();
        contentTypeOptions!.ShouldContain("nosniff");

        response.Headers.TryGetValues("Referrer-Policy", out var referrerPolicy).ShouldBeTrue();
        referrerPolicy!.ShouldContain("strict-origin-when-cross-origin");

        response.Headers.TryGetValues("Permissions-Policy", out _).ShouldBeTrue();

        response.Headers.TryGetValues("Content-Security-Policy", out var csp).ShouldBeTrue();
        var cspValue = csp!.Single();
        cspValue.ShouldContain("frame-src https://open.spotify.com");
        cspValue.ShouldNotContain("frame-src *");
        cspValue.ShouldContain("default-src 'self'");
    }

    /// <summary>
    /// Regression test (2026-09-05): production cover art started 404-ing under CSP because
    /// Spotify serves it from `image-cdn-ak/fa.spotifycdn.com` and `mosaic.scdn.co`, not only the
    /// originally-pinned `i.scdn.co` - confirmed live via a real Chromium console error
    /// ("Refused to load the image ... violates ... img-src"). Asserts the wildcarded form so a
    /// future accidental narrowing back to a single subdomain fails this test instead of silently
    /// breaking cover art again.
    /// </summary>
    [Fact]
    public async Task HomePage_csp_allows_every_current_spotify_cover_art_cdn_host()
    {
        var response = await _httpClient.GetAsync("/");

        response.Headers.TryGetValues("Content-Security-Policy", out var csp).ShouldBeTrue();
        var cspValue = csp!.Single();
        cspValue.ShouldContain("img-src 'self' https://*.scdn.co https://*.spotifycdn.com;");
    }

    /// <summary>US-012 AC1: the security headers apply to a 404 response too, not only 2xx ones.</summary>
    [Fact]
    public async Task NotFoundResponse_still_includes_the_security_headers()
    {
        var response = await _httpClient.GetAsync("/this-route-does-not-exist");

        response.Headers.Contains("Content-Security-Policy").ShouldBeTrue();
    }
}
