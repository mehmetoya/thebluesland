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
/// US-009: end-to-end proof that the home page's query-string-driven filter (see HomePage.razor's
/// design-decision comment) actually restores state from a directly-opened URL and shows the
/// empty-state/clear-filters affordance, via a real in-process host (same approach as
/// WebHostIntegrationTests). Uses a dedicated fixture set (Fixtures/content-playlists-filtering)
/// rather than the shared content-playlists fixtures, so filter combinations are unambiguous and
/// other tests reading content-playlists (exact-id-count assertions) stay unaffected.
/// </summary>
public sealed class HomePageFilterIntegrationTests : IAsyncLifetime
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=thebluesland;Timeout=2";

    private WebApplication _app = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists-filtering");

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
    public async Task HomePage_with_no_query_string_lists_every_published_playlist_sorted_by_displayOrder()
    {
        var response = await _httpClient.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("Energetic Headphones Fixture");
        body.ShouldContain("Moody Blues Night Fixture");
        body.ShouldContain("Warm Road Trip Fixture");
        // displayOrder 0, 1, 2: Energetic Headphones must render before Moody Blues Night, which
        // must render before Warm Road Trip.
        body.IndexOf("Energetic Headphones Fixture", StringComparison.Ordinal)
            .ShouldBeLessThan(body.IndexOf("Moody Blues Night Fixture", StringComparison.Ordinal));
        body.IndexOf("Moody Blues Night Fixture", StringComparison.Ordinal)
            .ShouldBeLessThan(body.IndexOf("Warm Road Trip Fixture", StringComparison.Ordinal));
    }

    /// <summary>
    /// US-009 AC2/AC3: opening a URL with a mood+occasion query string directly must restore
    /// exactly the same filtered result a form submission to that same URL would produce - mood
    /// "warm" OR-matches two fixtures, but AND-ing in occasion "road-trip" narrows it to one.
    /// </summary>
    [Fact]
    public async Task HomePage_with_a_query_string_restores_the_same_filtered_set_a_fresh_navigation_would_produce()
    {
        var response = await _httpClient.GetAsync("/?mood=warm&occasion=road-trip");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("Warm Road Trip Fixture");
        body.ShouldNotContain("Energetic Headphones Fixture");
        body.ShouldNotContain("Moody Blues Night Fixture");
    }

    [Fact]
    public async Task HomePage_with_an_unmatched_filter_combination_shows_the_empty_state_message()
    {
        // mood=warm matches Energetic Headphones and Warm Road Trip; occasion=late-night matches
        // only Moody Blues Night - AND-ing the two active dimensions leaves zero playlists.
        var response = await _httpClient.GetAsync("/?mood=warm&occasion=late-night");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("No playlists match the selected filters.");
        body.ShouldNotContain("Energetic Headphones Fixture");
        body.ShouldNotContain("Moody Blues Night Fixture");
        body.ShouldNotContain("Warm Road Trip Fixture");
    }

    [Fact]
    public async Task HomePage_empty_state_includes_a_one_click_clear_filters_link_back_to_an_unfiltered_home_page()
    {
        var response = await _httpClient.GetAsync("/?mood=warm&occasion=late-night");
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("href=\"/\" class=\"clear-filters\"");
    }
}
