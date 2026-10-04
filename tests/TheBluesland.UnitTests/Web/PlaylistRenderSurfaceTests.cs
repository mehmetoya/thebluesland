using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using System.Diagnostics.CodeAnalysis;
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
        Eras: ["1970s"],
        CuratorNote: "Curator note body used only for rendering tests.",
        IsPublished: true,
        FeaturedOrder: null,
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

        html.ShouldContain("temporarily unavailable");
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
        html.ShouldNotContain("temporarily unavailable");
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

    /// <summary>Playback uses the direct Spotify link without an embedded player.</summary>
    [Fact]
    public async Task PlaylistDetailView_shows_direct_spotify_link_without_embedded_player()
    {
        var playableSnapshot = new PlaylistCacheSnapshot(IsPlayable: true, TrackCount: 34, CoverImageUrl: null);

        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = playableSnapshot,
        });

        html.ShouldNotContain("<iframe");
        html.ShouldNotContain("Listen here");
        // docs/specs/visitor-and-playlist-click-analytics.md, Design section 5: routes through the
        // /out/{slug} redirect endpoint (so the click is recorded) rather than linking straight to
        // open.spotify.com.
        html.ShouldContain($"href=\"/out/{SamplePlaylist.Slug}\"");
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

        html.ShouldContain($"href=\"/out/{SamplePlaylist.Slug}\"");
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

    [Fact]
    public async Task PlaylistDetailView_renders_a_refresh_mix_panel_for_related_playlists()
    {
        var related = SamplePlaylist with { Slug = "related-slug", Title = "A Related Playlist" };

        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = PlaylistCacheSnapshot.Unavailable,
            [nameof(PlaylistDetailView.RelatedPlaylists)] = new List<PlaylistContent> { related },
        });

        html.ShouldContain("Refresh mix");
        html.ShouldContain("Same vibe");
        html.ShouldContain("More energetic");
        html.ShouldContain("Use this mix");
        html.ShouldContain("Keep the mood and swap in nearby tracks.");
        html.ShouldContain("A Related Playlist");
    }

    [Fact]
    public async Task PlaylistDetailView_renders_refresh_actions_as_links_that_work_with_static_server_rendering()
    {
        var related = SamplePlaylist with { Slug = "related-slug", Title = "A Related Playlist" };

        var html = await RenderAsync<PlaylistDetailView>(
            new Dictionary<string, object?>
            {
                [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
                [nameof(PlaylistDetailView.CacheSnapshot)] = PlaylistCacheSnapshot.Unavailable,
                [nameof(PlaylistDetailView.RelatedPlaylists)] = new List<PlaylistContent> { related },
            },
            "http://localhost/playlists/test-slug?utm_source=email");

        html.ShouldContain("href=\"http://localhost/playlists/test-slug?utm_source=email&amp;mix=same-vibe\"");
        html.ShouldContain("href=\"http://localhost/playlists/test-slug?utm_source=email&amp;mix=more-energetic\"");
        html.ShouldContain("mix-selection=related-slug");
        html.ShouldContain("mix-applied-mode=same-vibe");
        html.ShouldContain("mix-hidden=1");
        html.ShouldNotContain("@onclick");
    }

    [Fact]
    public async Task PlaylistDetailView_restores_an_applied_selection_from_route_state()
    {
        var related = SamplePlaylist with { Slug = "related-slug", Title = "A Related Playlist" };

        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = PlaylistCacheSnapshot.Unavailable,
            [nameof(PlaylistDetailView.RelatedPlaylists)] = new List<PlaylistContent> { related },
            [nameof(PlaylistDetailView.MixQuery)] = "more-energetic",
            [nameof(PlaylistDetailView.MixSelectionQuery)] = "related-slug",
        });

        html.ShouldContain("Currently exploring:");
        html.ShouldContain("A Related Playlist");
        html.ShouldContain("Preview again");
        html.ShouldNotContain("Use this mix");
    }

    [Fact]
    public async Task PlaylistDetailView_can_preview_another_mix_without_losing_the_applied_selection()
    {
        var related = SamplePlaylist with { Slug = "related-slug", Title = "A Related Playlist" };

        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = PlaylistCacheSnapshot.Unavailable,
            [nameof(PlaylistDetailView.RelatedPlaylists)] = new List<PlaylistContent> { related },
            [nameof(PlaylistDetailView.MixQuery)] = "more-energetic",
            [nameof(PlaylistDetailView.MixSelectionQuery)] = "related-slug",
            [nameof(PlaylistDetailView.MixPreviewQuery)] = "1",
        });

        html.ShouldContain("Currently exploring:");
        html.ShouldContain("Use this mix");
        html.ShouldContain("Keep current list");
    }

    [Fact]
    public async Task PlaylistDetailView_restores_the_applied_ranking_after_dismissing_an_alternate_preview()
    {
        var relaxed = SamplePlaylist with
        {
            Slug = "relaxed-choice",
            Title = "Relaxed choice",
            Moods = ["warm"],
            Genres = ["soul"],
        };
        var energetic = SamplePlaylist with
        {
            Slug = "energetic-choice",
            Title = "Energetic choice",
            Moods = ["energetic"],
            Genres = ["rock"],
        };

        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = PlaylistCacheSnapshot.Unavailable,
            [nameof(PlaylistDetailView.RelatedPlaylists)] = new List<PlaylistContent> { relaxed, energetic },
            [nameof(PlaylistDetailView.MixQuery)] = "more-energetic",
            [nameof(PlaylistDetailView.MixAppliedModeQuery)] = "relaxed-flow",
            [nameof(PlaylistDetailView.MixSelectionQuery)] = "relaxed-choice",
            [nameof(PlaylistDetailView.MixHiddenQuery)] = "1",
        });

        html.IndexOf("href=\"/playlists/relaxed-choice\"", StringComparison.Ordinal)
            .ShouldBeLessThan(html.IndexOf("href=\"/playlists/energetic-choice\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlaylistDetailView_ignores_applied_selection_outside_the_related_playlist_set()
    {
        var related = SamplePlaylist with { Slug = "related-slug", Title = "A Related Playlist" };

        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = PlaylistCacheSnapshot.Unavailable,
            [nameof(PlaylistDetailView.RelatedPlaylists)] = new List<PlaylistContent> { related },
            [nameof(PlaylistDetailView.MixSelectionQuery)] = "unrelated-slug",
        });

        html.ShouldNotContain("Currently exploring:");
        html.ShouldContain("Use this mix");
    }

    [Fact]
    public async Task PlaylistDetailView_restores_a_dismissed_preview_from_route_state()
    {
        var related = SamplePlaylist with { Slug = "related-slug", Title = "A Related Playlist" };

        var html = await RenderAsync<PlaylistDetailView>(new Dictionary<string, object?>
        {
            [nameof(PlaylistDetailView.Content)] = SamplePlaylist,
            [nameof(PlaylistDetailView.CacheSnapshot)] = PlaylistCacheSnapshot.Unavailable,
            [nameof(PlaylistDetailView.RelatedPlaylists)] = new List<PlaylistContent> { related },
            [nameof(PlaylistDetailView.MixHiddenQuery)] = "1",
        });

        html.ShouldContain("Preview again");
        html.ShouldNotContain("mix-mode-buttons");
    }

    [Fact]
    public void PlaylistDetailView_parses_mix_mode_from_query_tokens()
    {
        PlaylistDetailView.ParseMixMode("same-vibe").ShouldBe(PlaylistMixMode.SameVibe);
        PlaylistDetailView.ParseMixMode("more-energetic").ShouldBe(PlaylistMixMode.MoreEnergetic);
        PlaylistDetailView.ParseMixMode("relaxed-flow").ShouldBe(PlaylistMixMode.RelaxedFlow);
        PlaylistDetailView.ParseMixMode("deeper-cuts").ShouldBe(PlaylistMixMode.DeeperCuts);
        PlaylistDetailView.ParseMixMode("unknown-mode").ShouldBe(PlaylistMixMode.SameVibe);
    }

    [Fact]
    public void PlaylistDetailView_serializes_mix_mode_to_query_tokens()
    {
        PlaylistDetailView.ToQueryToken(PlaylistMixMode.SameVibe).ShouldBe("same-vibe");
        PlaylistDetailView.ToQueryToken(PlaylistMixMode.MoreEnergetic).ShouldBe("more-energetic");
        PlaylistDetailView.ToQueryToken(PlaylistMixMode.RelaxedFlow).ShouldBe("relaxed-flow");
        PlaylistDetailView.ToQueryToken(PlaylistMixMode.DeeperCuts).ShouldBe("deeper-cuts");
    }

    [Fact]
    public void PlaylistDetailView_preserves_existing_query_parameters_when_updating_or_clearing_mix_state()
    {
        var withMix = PlaylistDetailView.BuildRouteWithMix("/playlists/test-slug", "?utm_source=test", PlaylistMixMode.MoreEnergetic);
        withMix.ShouldContain("utm_source=test");
        withMix.ShouldContain("mix=more-energetic");
        withMix.ShouldNotContain("mix=same-vibe");

        var previewingAnotherMode = PlaylistDetailView.BuildRouteWithMix(
            "/playlists/test-slug",
            "?utm_source=test&mix=same-vibe&mix-selection=old-slug&mix-applied-mode=relaxed-flow",
            PlaylistMixMode.MoreEnergetic);
        previewingAnotherMode.ShouldContain("mix=more-energetic");
        previewingAnotherMode.ShouldContain("mix-selection=old-slug");
        previewingAnotherMode.ShouldContain("mix-applied-mode=relaxed-flow");
        previewingAnotherMode.ShouldContain("mix-preview=1");

        var appliedMix = PlaylistDetailView.BuildRouteWithSelection(
            "/playlists/test-slug#details",
            "?utm_source=test&mix-hidden=1&mix-selection=old-slug",
            PlaylistMixMode.MoreEnergetic,
            "new-slug");
        appliedMix.ShouldContain("utm_source=test");
        appliedMix.ShouldContain("mix=more-energetic");
        appliedMix.ShouldContain("mix-selection=new-slug");
        appliedMix.ShouldContain("mix-applied-mode=more-energetic");
        appliedMix.ShouldNotContain("mix-hidden");
        appliedMix.ShouldEndWith("#details");

        var dismissedMix = PlaylistDetailView.BuildRouteWithDismissedPreview(
            "/playlists/test-slug",
            "?utm_source=test&mix=more-energetic&mix-selection=old-slug&mix-preview=1");
        dismissedMix.ShouldContain("mix=more-energetic");
        dismissedMix.ShouldContain("mix-hidden=1");
        dismissedMix.ShouldContain("mix-selection=old-slug");
        dismissedMix.ShouldContain("mix-applied-mode=more-energetic");
        dismissedMix.ShouldNotContain("mix-preview");

        var visibleMix = PlaylistDetailView.BuildRouteWithVisiblePreview(
            "/playlists/test-slug",
            "?utm_source=test&mix=more-energetic&mix-selection=old-slug&mix-hidden=1");
        visibleMix.ShouldContain("mix-selection=old-slug");
        visibleMix.ShouldContain("mix-applied-mode=more-energetic");
        visibleMix.ShouldContain("mix-preview=1");
        visibleMix.ShouldNotContain("mix-hidden");

        var withoutMix = PlaylistDetailView.BuildRouteWithoutMix(
            "/playlists/test-slug",
            "?utm_source=test&mix=more-energetic&mix-selection=old-slug&mix-hidden=1&mix-preview=1");
        withoutMix.ShouldBe("/playlists/test-slug?utm_source=test");
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string uri)
        {
            Initialize(uri, uri);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
    private static async Task<string> RenderAsync<TComponent>(
        Dictionary<string, object?> parameters,
        string navigationUri = "http://localhost/")
        where TComponent : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<NavigationManager>(_ => new TestNavigationManager(navigationUri));
        await using var serviceProvider = services.BuildServiceProvider();
        await using var htmlRenderer = new HtmlRenderer(serviceProvider, NullLoggerFactory.Instance);

        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await htmlRenderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }
}
