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
/// docs/specs/analytics-dashboard.md Testing Strategy: mirrors WebHostIntegrationTests.cs's
/// /health/cache key-gating tests exactly (missing/wrong key -> 404, correct key -> 200 with
/// text/html), then seeds a few PageViewEvent rows (same Testcontainers pattern as
/// PageViewAnalyticsIntegrationTests.cs) and asserts the daily-unique-visitor and top-playlist
/// aggregates come back correct for a known fixture.
/// </summary>
public sealed class AnalyticsDashboardIntegrationTests : IAsyncLifetime
{
    private const string UnreachableCacheConnectionString =
        "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=thebluesland;Timeout=2";
    private const string DashboardKey = "test-dashboard-key";

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

            var today = DateTimeOffset.UtcNow;
            var yesterday = today.AddDays(-1);

            dbContext.PageViewEvents.AddRange(
                // Two distinct visitors today -> Unique = 2 for today.
                new PageViewEvent { OccurredAt = today, EventType = "page_view", Path = "/", PlaylistSlug = null, VisitorHash = "visitor-a" },
                new PageViewEvent { OccurredAt = today, EventType = "page_view", Path = "/", PlaylistSlug = null, VisitorHash = "visitor-b" },
                // Same visitor hash twice yesterday -> Unique = 1 for yesterday.
                new PageViewEvent { OccurredAt = yesterday, EventType = "page_view", Path = "/", PlaylistSlug = null, VisitorHash = "visitor-a" },
                new PageViewEvent { OccurredAt = yesterday, EventType = "page_view", Path = "/", PlaylistSlug = null, VisitorHash = "visitor-a" },
                // Playlist page views: fixture-slug seen twice, other-slug once.
                new PageViewEvent { OccurredAt = today, EventType = "page_view", Path = "/playlists/fixture-slug", PlaylistSlug = "fixture-slug", VisitorHash = "visitor-a" },
                new PageViewEvent { OccurredAt = today, EventType = "page_view", Path = "/playlists/fixture-slug", PlaylistSlug = "fixture-slug", VisitorHash = "visitor-b" },
                new PageViewEvent { OccurredAt = today, EventType = "page_view", Path = "/playlists/other-slug", PlaylistSlug = "other-slug", VisitorHash = "visitor-a" },
                // Spotify clicks: fixture-slug clicked three times.
                new PageViewEvent { OccurredAt = today, EventType = "spotify_click", Path = "/out/fixture-slug", PlaylistSlug = "fixture-slug", VisitorHash = "visitor-a" },
                new PageViewEvent { OccurredAt = today, EventType = "spotify_click", Path = "/out/fixture-slug", PlaylistSlug = "fixture-slug", VisitorHash = "visitor-a" },
                new PageViewEvent { OccurredAt = today, EventType = "spotify_click", Path = "/out/fixture-slug", PlaylistSlug = "fixture-slug", VisitorHash = "visitor-b" });

            await dbContext.SaveChangesAsync();
        }

        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists");

        _app = WebHostFactory.Create([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] = contentDirectory;
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] = UnreachableCacheConnectionString;
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.AnalyticsConnectionStringName}"] = _postgres.GetConnectionString();
            builder.Configuration["Diagnostics:AnalyticsDashboardKey"] = DashboardKey;
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
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Dashboard_is_not_available_to_a_request_with_no_key()
    {
        using var response = await _httpClient.GetAsync("/dashboard");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dashboard_is_not_available_to_a_request_with_the_wrong_key()
    {
        using var response = await _httpClient.GetAsync("/dashboard?key=wrong-key");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dashboard_returns_html_tables_with_the_correct_aggregates_for_the_correct_key()
    {
        using var response = await _httpClient.GetAsync($"/dashboard?key={DashboardKey}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");

        // Daily unique visitors: two distinct visitor hashes today, one distinct hash yesterday.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);
        body.ShouldContain($"<td>{today:yyyy-MM-dd}</td><td>2</td>");
        body.ShouldContain($"<td>{yesterday:yyyy-MM-dd}</td><td>1</td>");

        // Top playlists by view: fixture-slug (2) ranks above other-slug (1).
        var fixtureSlugIndex = body.IndexOf("fixture-slug", StringComparison.Ordinal);
        var otherSlugIndex = body.IndexOf("other-slug", StringComparison.Ordinal);
        fixtureSlugIndex.ShouldBeGreaterThanOrEqualTo(0, body);
        otherSlugIndex.ShouldBeGreaterThan(fixtureSlugIndex, body);
        body.ShouldContain("<td>fixture-slug</td><td>2</td>");
        body.ShouldContain("<td>other-slug</td><td>1</td>");

        // Top playlists by click-through: fixture-slug clicked 3 times.
        body.ShouldContain("<td>fixture-slug</td><td>3</td>");

        // Boundary: raw visitor_hash values are never printed.
        body.ShouldNotContain("visitor-a");
        body.ShouldNotContain("visitor-b");
    }
}
