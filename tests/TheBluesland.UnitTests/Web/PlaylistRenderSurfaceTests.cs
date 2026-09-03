using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TheBluesland.Web.Cache;
using TheBluesland.Web.Components.Shared;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-005 acceptance criteria 1-2 at the component level: PlaylistCard and PlaylistDetailView must
/// render fully from editorial content alone when there is no playable cache data (missing row,
/// is_available = false, or a DB error - all collapse to PlaylistCacheSnapshot.Unavailable), and
/// the detail view must show a readable fallback message instead of ever emitting a broken iframe.
/// Renders the real Razor components via the built-in Microsoft.AspNetCore.Components HtmlRenderer
/// (no host, no bUnit package).
/// </summary>
public sealed class PlaylistRenderSurfaceTests
{
    private static readonly PlaylistContent SamplePlaylist = new(
        Slug: "test-slug",
        SpotifyPlaylistId: "0iJt9LMebhOY0KSHSJw3cS",
        Title: "Masterpieces of Erkin the Father",
        Summary: "Anadolu rock energy from a founding Turkish psychedelic voice.",
        Moods: ["energetic"],
        Genres: ["rock"],
        Occasions: ["night-drive"],
        Era: "1970s",
        CuratorNote: "Curator note body used only for rendering tests.",
        IsPublished: true,
        Featured: false,
        DisplayOrder: 0,
        PublishedAt: new DateOnly(2026, 1, 1),
        PreviousSlugs: []);

    [Fact]
    public async Task PlaylistCard_renders_editorial_fields_without_error_when_cache_is_unavailable()
    {
        var html = await RenderAsync<PlaylistCard>(new Dictionary<string, object?>
        {
            [nameof(PlaylistCard.Content)] = SamplePlaylist,
            [nameof(PlaylistCard.CacheSnapshot)] = PlaylistCacheSnapshot.Unavailable,
        });

        html.ShouldContain(SamplePlaylist.Title);
        html.ShouldContain(SamplePlaylist.Summary);
        html.ShouldNotContain("track-count");
    }

    [Fact]
    public async Task PlaylistDetailView_renders_editorial_content_only_when_no_cache_row_exists()
    {
        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = PlaylistCacheSnapshot.Unavailable,
        });

        html.ShouldContain(SamplePlaylist.Title);
        html.ShouldContain(SamplePlaylist.CuratorNote);
        html.ShouldContain("energetic");
    }

    [Fact]
    public async Task PlaylistDetailView_shows_player_unavailable_message_instead_of_an_iframe()
    {
        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = PlaylistCacheSnapshot.Unavailable,
        });

        html.ShouldContain("currently unavailable");
        html.ShouldNotContain("<iframe");
    }

    [Fact]
    public async Task PlaylistDetailView_shows_cache_fields_and_no_unavailable_message_when_playable()
    {
        var playableSnapshot = new PlaylistCacheSnapshot(IsPlayable: true, TrackCount: 34, CoverImageUrl: "https://i.scdn.co/image/cover.jpg");

        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = playableSnapshot,
        });

        html.ShouldContain("34 tracks");
        html.ShouldNotContain("currently unavailable");
    }

    /// <summary>US-010 AC1: the cache-sourced cover image renders when present and playable.</summary>
    [Fact]
    public async Task PlaylistDetailView_shows_cover_image_when_present_and_playable()
    {
        var playableSnapshot = new PlaylistCacheSnapshot(IsPlayable: true, TrackCount: 34, CoverImageUrl: "https://i.scdn.co/image/cover.jpg");

        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = playableSnapshot,
        });

        html.ShouldContain("<img class=\"cover-image\" src=\"https://i.scdn.co/image/cover.jpg\"");
    }

    /// <summary>US-010 AC2/spec 11.3: no iframe in the markup until ShowEmbed is explicitly true.</summary>
    [Fact]
    public async Task PlaylistDetailView_shows_a_click_to_load_link_and_no_iframe_by_default()
    {
        var playableSnapshot = new PlaylistCacheSnapshot(IsPlayable: true, TrackCount: 34, CoverImageUrl: null);

        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = playableSnapshot,
        });

        html.ShouldNotContain("<iframe");
        html.ShouldContain("href=\"?listen=true\"");
    }

    /// <summary>US-010 AC2: once ShowEmbed is true, the iframe is present with the spec 12.4(a) embed URL shape.</summary>
    [Fact]
    public async Task PlaylistDetailView_shows_the_iframe_with_the_embed_url_when_ShowEmbed_is_true()
    {
        var playableSnapshot = new PlaylistCacheSnapshot(IsPlayable: true, TrackCount: 34, CoverImageUrl: null);

        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = playableSnapshot,
            [nameof(PlaylistDetailView.ShowEmbed)] = true,
        });

        html.ShouldContain($"<iframe class=\"spotify-embed\" src=\"https://open.spotify.com/embed/playlist/{SamplePlaylist.SpotifyPlaylistId}\"");
    }

    /// <summary>US-010 AC3/FR-024: present even when the cache is unavailable, built only from the playlist ID.</summary>
    [Fact]
    public async Task PlaylistDetailView_always_shows_the_open_in_spotify_link_even_when_the_cache_is_unavailable()
    {
        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = PlaylistCacheSnapshot.Unavailable,
        });

        html.ShouldContain($"href=\"https://open.spotify.com/playlist/{SamplePlaylist.SpotifyPlaylistId}\"");
    }

    /// <summary>US-010 AC4: related playlists (already ranked/capped upstream) render as cards.</summary>
    [Fact]
    public async Task PlaylistDetailView_renders_the_related_playlists_it_is_given()
    {
        var related = SamplePlaylist with { Slug = "related-slug", Title = "A Related Playlist" };

        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = PlaylistCacheSnapshot.Unavailable,
            [nameof(PlaylistDetailView.RelatedPlaylists)] = new List<PlaylistContent> { related },
        });

        html.ShouldContain("A Related Playlist");
        html.ShouldContain("href=\"/playlists/related-slug\"");
    }

    private static async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var serviceProvider = services.BuildServiceProvider();
        await using var htmlRenderer = new HtmlRenderer(serviceProvider, NullLoggerFactory.Instance);

        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await htmlRenderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }
}
