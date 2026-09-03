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
/// US-010: end-to-end proof, via a real in-process Kestrel host (same approach as
/// WebHostIntegrationTests/HomePageFilterIntegrationTests) and a real Testcontainers Postgres (same
/// approach as PlaylistCacheLookupTests), that the previousSlugs redirect (AC5), the click-to-load
/// embed (AC2), the "Open in Spotify" link (AC3) and the related-playlists strip (AC4) actually work
/// against the real Razor Components endpoint pipeline. The redirect assertion in particular is
/// exactly the kind of response-mutation-timing behavior ResponseStatusCode.razor's doc comment
/// warns about, so it is proven here (status code + Location header) rather than assumed from the
/// code compiling.
/// </summary>
public sealed class PlaylistDetailPageIntegrationTests : IAsyncLifetime
{
    private const string PrimaryPlaylistSpotifyId = "0iJt9LMebhOY0KSHSJw3cS";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private WebApplication _app = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var optionsBuilder = new DbContextOptionsBuilder<TheBlueslandDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());
        await using (var dbContext = new TheBlueslandDbContext(optionsBuilder.Options))
        {
            await dbContext.Database.MigrateAsync();
            dbContext.SpotifyPlaylistCache.Add(new SpotifyPlaylistCacheEntry
            {
                SpotifyPlaylistId = PrimaryPlaylistSpotifyId,
                Name = "Cache-owned name",
                TrackCount = 12,
                Artists = ["Some Artist"],
                CoverImageUrl = "https://i.scdn.co/image/cover.jpg",
                SyncedAt = DateTimeOffset.UtcNow,
                IsAvailable = true,
            });
            await dbContext.SaveChangesAsync();
        }

        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists-detail");

        _app = WebHostFactory.Create([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] = contentDirectory;
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] = _postgres.GetConnectionString();
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
    public async Task PlaylistDetailPage_with_a_current_slug_still_returns_200_with_editorial_content()
    {
        var response = await _httpClient.GetAsync("/playlists/primary-playlist");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("Primary Playlist Fixture");
    }

    /// <summary>US-010 AC5/FR-020.</summary>
    [Fact]
    public async Task PlaylistDetailPage_with_an_old_previousSlugs_entry_redirects_permanently_to_the_current_slug()
    {
        var response = await _httpClient.GetAsync("/playlists/legacy-primary-slug");

        response.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.Location!.OriginalString.ShouldBe("/playlists/primary-playlist");
    }

    [Fact]
    public async Task PlaylistDetailPage_with_an_unknown_slug_that_is_also_not_a_previous_slug_still_404s()
    {
        var response = await _httpClient.GetAsync("/playlists/does-not-exist-anywhere");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>US-010 AC2/spec 11.3: no iframe/Spotify contact until the visitor clicks "Listen here".</summary>
    [Fact]
    public async Task PlaylistDetailPage_does_not_include_the_iframe_before_the_listen_query_flag_is_present()
    {
        var response = await _httpClient.GetAsync("/playlists/primary-playlist");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldNotContain("<iframe");
        body.ShouldContain("href=\"?listen=true\"");
    }

    [Fact]
    public async Task PlaylistDetailPage_includes_the_iframe_once_the_listen_query_flag_is_present()
    {
        var response = await _httpClient.GetAsync("/playlists/primary-playlist?listen=true");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain($"<iframe class=\"spotify-embed\" src=\"https://open.spotify.com/embed/playlist/{PrimaryPlaylistSpotifyId}\"");
    }

    /// <summary>US-010 AC3: the exact `https://open.spotify.com/playlist/{id}` shape, distinct from the embed URL.</summary>
    [Fact]
    public async Task PlaylistDetailPage_open_in_spotify_link_points_to_the_direct_playlist_url_not_the_embed_url()
    {
        var response = await _httpClient.GetAsync("/playlists/primary-playlist");
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain($"href=\"https://open.spotify.com/playlist/{PrimaryPlaylistSpotifyId}\"");
    }

    /// <summary>US-010 AC4: the two overlapping fixtures show; the zero-overlap fixture never does.</summary>
    [Fact]
    public async Task PlaylistDetailPage_shows_related_playlists_by_shared_tag_overlap_and_excludes_unrelated_ones()
    {
        var response = await _httpClient.GetAsync("/playlists/primary-playlist");
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("Related Strong Fixture");
        body.ShouldContain("Related Weak Fixture");
        body.ShouldNotContain("Unrelated Fixture");
    }

    /// <summary>US-010 AC1/spec 9.3/SEC-003: raw HTML in the curator note is sanitized, not executed.</summary>
    [Fact]
    public async Task PlaylistDetailPage_renders_the_curator_note_as_sanitized_markdown()
    {
        var response = await _httpClient.GetAsync("/playlists/primary-playlist");
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("<strong>bold</strong>");
        body.ShouldNotContain("<script>alert");
    }
}
