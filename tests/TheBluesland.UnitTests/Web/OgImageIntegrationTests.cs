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
/// US-011 AC3/FR-031: the generated social card endpoints return a real, non-trivial PNG, are
/// server-generated per playlist (not one static file reused everywhere), and 404 for an unknown
/// slug rather than silently serving a default image. Uses the same
/// <c>Fixtures/content-playlists-detail</c> set as PlaylistDetailPageIntegrationTests, which has two
/// published fixtures with different titles.
/// </summary>
public sealed class OgImageIntegrationTests : IAsyncLifetime
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=thebluesland;Timeout=2";

    private WebApplication _app = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists-detail");

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
    public async Task Default_og_image_returns_a_real_png()
    {
        var response = await _httpClient.GetAsync("/og-image.png");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        bytes.Length.ShouldBeGreaterThan(1000);
    }

    [Fact]
    public async Task Playlist_og_image_returns_a_real_png_for_a_known_slug()
    {
        var response = await _httpClient.GetAsync("/playlists/primary-playlist/og-image.png");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        bytes.Length.ShouldBeGreaterThan(1000);
    }

    /// <summary>Proves the endpoint is not a single static file reused for every playlist.</summary>
    [Fact]
    public async Task Playlist_og_image_differs_between_two_playlists_with_different_titles()
    {
        var first = await (await _httpClient.GetAsync("/playlists/primary-playlist/og-image.png")).Content.ReadAsByteArrayAsync();
        var second = await (await _httpClient.GetAsync("/playlists/related-strong/og-image.png")).Content.ReadAsByteArrayAsync();

        first.SequenceEqual(second).ShouldBeFalse();
    }

    [Fact]
    public async Task Playlist_og_image_returns_404_for_an_unknown_slug()
    {
        var response = await _httpClient.GetAsync("/playlists/does-not-exist/og-image.png");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
