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
        CuratorNote: "Curator note body used only for rendering tests.",
        IsPublished: true);

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
