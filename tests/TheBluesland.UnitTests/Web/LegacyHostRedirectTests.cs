using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using TheBluesland.Web;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// 2026-09-09 domain migration (SPEC-custom-domain-migration.md): a request that arrives under a
/// legacy hostname must be permanently redirected to the canonical <c>Site:PublicOrigin</c>, path
/// and query string intact - not rejected by <c>AddHostFiltering</c>'s allowlist (its only other
/// possible outcome, since the allowlist otherwise names exactly one public host) and not served
/// directly (which would leave two live, competing canonical identities for the same content).
/// </summary>
public sealed class LegacyHostRedirectTests
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Username=test;Password=test;Database=test;Timeout=1";
    private const string CanonicalOrigin = "https://thebluesland.com";

    [Theory]
    [InlineData("thebluesland.onrender.com")]
    [InlineData("www.thebluesland.com")]
    [InlineData("THEBLUESLAND.ONRENDER.COM")] // Host headers are case-insensitive (RFC 9110 §4.2.3)
    public async Task Legacy_host_is_redirected_permanently_to_the_canonical_origin_with_path_and_query(
        string legacyHost)
    {
        await using var app = CreateApp();
        await app.StartAsync();
        using var client = CreateClient(app, followRedirects: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/playlists/dear-mr-fantasy?listen=true");
        request.Headers.Host = legacyHost;

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.Location!.ToString().ShouldBe(
            $"{CanonicalOrigin}/playlists/dear-mr-fantasy?listen=true");
    }

    [Fact]
    public async Task Canonical_host_is_served_directly_with_no_redirect()
    {
        await using var app = CreateApp();
        await app.StartAsync();
        using var client = CreateClient(app, followRedirects: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/robots.txt");
        request.Headers.Host = "thebluesland.com";

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Guards the default/local/test configuration, where <c>Site:PublicOrigin</c> is unset and
    /// therefore equal to <see cref="TheBluesland.Web.Seo.SiteUrl.DefaultPublicOrigin"/>'s own
    /// onrender.com host - one of the two legacy hostnames. Without the canonical-host check in
    /// the redirect middleware, this exact configuration would redirect to itself forever.
    /// </summary>
    [Fact]
    public async Task Default_configuration_serves_its_own_onrender_host_with_no_self_redirect()
    {
        await using var app = WebHostFactory.Create([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] =
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists");
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] = UnreachableConnectionString;
            // No Site:PublicOrigin override - the point of this test.
        });
        await app.StartAsync();
        using var client = CreateClient(app, followRedirects: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/robots.txt");
        request.Headers.Host = "thebluesland.onrender.com";

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static WebApplication CreateApp() => WebHostFactory.Create([], builder =>
    {
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] =
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists");
        builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] = UnreachableConnectionString;
        builder.Configuration[TheBluesland.Web.Seo.SiteUrl.PublicOriginConfigKey] = CanonicalOrigin;
    });

    private static HttpClient CreateClient(WebApplication app, bool followRedirects) =>
        new(new HttpClientHandler { AllowAutoRedirect = followRedirects }) { BaseAddress = new Uri(app.Urls.Single()) };
}
