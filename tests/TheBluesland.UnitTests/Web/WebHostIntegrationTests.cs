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

    [Fact]
    public async Task PlaylistDetailPage_returns_200_with_editorial_content_when_the_database_is_unreachable()
    {
        var response = await _httpClient.GetAsync("/playlists/masterpieces-of-erkin-the-father");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("Masterpieces of Erkin the Father");
        body.ShouldContain("currently unavailable");
    }
}
