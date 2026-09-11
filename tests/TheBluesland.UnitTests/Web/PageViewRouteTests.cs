using Shouldly;
using TheBluesland.Web.Analytics;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// docs/specs/visitor-and-playlist-click-analytics.md Design section 4's exact route table.
/// </summary>
public sealed class PageViewRouteTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/about")]
    [InlineData("/collections")]
    [InlineData("/privacy")]
    [InlineData("/terms")]
    public void Exact_match_content_routes_classify_as_a_page_view_with_no_playlist_slug(string path)
    {
        var isPageView = PageViewRoute.TryClassify(path, out var playlistSlug);

        isPageView.ShouldBeTrue();
        playlistSlug.ShouldBeNull();
    }

    [Fact]
    public void Playlist_detail_route_classifies_as_a_page_view_with_the_slug()
    {
        var isPageView = PageViewRoute.TryClassify("/playlists/my-slug", out var playlistSlug);

        isPageView.ShouldBeTrue();
        playlistSlug.ShouldBe("my-slug");
    }

    /// <summary>Design section 4: a collection page view is logged, but PlaylistSlug stays null -
    /// it's a collection key, not a playlist, and must not be conflated with one.</summary>
    [Fact]
    public void Collection_detail_route_classifies_as_a_page_view_with_no_playlist_slug()
    {
        var isPageView = PageViewRoute.TryClassify("/collections/my-collection", out var playlistSlug);

        isPageView.ShouldBeTrue();
        playlistSlug.ShouldBeNull();
    }

    [Theory]
    [InlineData("/playlists/my-slug/og-image.png")]
    [InlineData("/playlists/")]
    [InlineData("/health/live")]
    [InlineData("/sitemap.xml")]
    [InlineData("/robots.txt")]
    [InlineData("/llms.txt")]
    [InlineData("/og-image.png")]
    [InlineData("/out/my-slug")]
    [InlineData("/css/app.css")]
    public void Everything_else_does_not_classify_as_a_page_view(string path)
    {
        var isPageView = PageViewRoute.TryClassify(path, out var playlistSlug);

        isPageView.ShouldBeFalse();
        playlistSlug.ShouldBeNull();
    }
}
