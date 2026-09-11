using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using TheBluesland.Data.Entities;
using TheBluesland.Web;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// docs/specs/visitor-and-playlist-click-analytics.md Testing Strategy: a real request to `/` and
/// to a known-published `/playlists/{slug}` each produce exactly one new page_view_events row with
/// the expected Path/PlaylistSlug; an unknown `/playlists/{slug}` (404) and `/health/live` produce
/// none. Also covers the `/out/{slug}` redirect endpoint: a known slug returns a 302 to the correct
/// open.spotify.com URL and records exactly one spotify_click row; an unknown slug 404s and records
/// nothing. Uses a real, disposable Testcontainers Postgres for the Analytics connection (never a
/// mocked DbContext), same approach as PlaylistCacheLookupTests/PlaylistDetailPageIntegrationTests.
/// The SpotifyPlaylistCache connection is deliberately left unreachable (mirrors
/// WebHostIntegrationTests): none of these assertions depend on cache data rendering successfully.
/// </summary>
public sealed class PageViewAnalyticsIntegrationTests : IAsyncLifetime
{
    private const string UnreachableCacheConnectionString =
        "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=thebluesland;Timeout=2";
    private const string KnownSlug = "masterpieces-of-erkin-the-father";
    private const string KnownSpotifyPlaylistId = "0iJt9LMebhOY0KSHSJw3cS";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private WebApplication _app = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var optionsBuilder = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());
        await using (var dbContext = new AnalyticsDbContext(optionsBuilder.Options))
        {
            await dbContext.Database.MigrateAsync();
        }

        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists");

        _app = WebHostFactory.Create([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] = contentDirectory;
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] = UnreachableCacheConnectionString;
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.AnalyticsConnectionStringName}"] = _postgres.GetConnectionString();
        });

        await _app.StartAsync();

        var addressesFeature = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var address = addressesFeature!.Addresses.First();

        // Redirects must be asserted directly (status + Location), never silently followed.
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _httpClient.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task HomePage_request_records_exactly_one_page_view_row()
    {
        var response = await _httpClient.GetAsync("/");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rows = await WaitForRowsAsync(row => row.Path == "/");

        rows.Count.ShouldBe(1);
        rows[0].EventType.ShouldBe("page_view");
        rows[0].PlaylistSlug.ShouldBeNull();
    }

    [Fact]
    public async Task Known_playlist_detail_page_records_exactly_one_page_view_row_with_the_slug()
    {
        var response = await _httpClient.GetAsync($"/playlists/{KnownSlug}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rows = await WaitForRowsAsync(row => row.Path == $"/playlists/{KnownSlug}");

        rows.Count.ShouldBe(1);
        rows[0].EventType.ShouldBe("page_view");
        rows[0].PlaylistSlug.ShouldBe(KnownSlug);
    }

    [Fact]
    public async Task Unknown_playlist_slug_404_records_no_row()
    {
        var response = await _httpClient.GetAsync("/playlists/does-not-exist");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var rows = await ReadAllRowsAsync();

        rows.ShouldNotContain(row => row.Path == "/playlists/does-not-exist");
    }

    [Fact]
    public async Task HealthLive_records_no_row()
    {
        var response = await _httpClient.GetAsync("/health/live");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var rows = await ReadAllRowsAsync();

        rows.ShouldNotContain(row => row.Path == "/health/live");
    }

    [Fact]
    public async Task Known_slug_out_redirect_returns_302_to_spotify_and_records_exactly_one_spotify_click_row()
    {
        var response = await _httpClient.GetAsync($"/out/{KnownSlug}");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe($"https://open.spotify.com/playlist/{KnownSpotifyPlaylistId}");

        var rows = await WaitForRowsAsync(row => row.Path == $"/out/{KnownSlug}");

        rows.Count.ShouldBe(1);
        rows[0].EventType.ShouldBe("spotify_click");
        rows[0].PlaylistSlug.ShouldBe(KnownSlug);
    }

    [Fact]
    public async Task Unknown_slug_out_redirect_404s_and_records_nothing()
    {
        var response = await _httpClient.GetAsync("/out/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var rows = await ReadAllRowsAsync();

        rows.ShouldNotContain(row => row.Path == "/out/does-not-exist");
    }

    // The write is genuinely fire-and-forget (spec Design section 4), so assertions poll for a
    // short window rather than assuming the row exists the instant the HTTP response returns.
    private async Task<List<PageViewEvent>> WaitForRowsAsync(Func<PageViewEvent, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var rows = (await ReadAllRowsAsync()).Where(predicate).ToList();
            if (rows.Count > 0)
            {
                return rows;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return [];
    }

    private async Task<List<PageViewEvent>> ReadAllRowsAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());
        await using var dbContext = new AnalyticsDbContext(optionsBuilder.Options);
        return await dbContext.PageViewEvents.AsNoTracking().ToListAsync();
    }
}
