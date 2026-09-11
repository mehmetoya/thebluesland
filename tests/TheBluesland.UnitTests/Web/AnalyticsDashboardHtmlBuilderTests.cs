using Shouldly;
using TheBluesland.Web.Analytics;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// docs/specs/analytics-dashboard.md Testing Strategy: given known fixture rows, the rendered HTML
/// contains the expected slugs/counts. A simple string-contains assertion is enough here - this
/// isn't a page that needs golden-file/snapshot testing.
/// </summary>
public sealed class AnalyticsDashboardHtmlBuilderTests
{
    [Fact]
    public void Build_renders_the_daily_unique_visitor_table()
    {
        var html = AnalyticsDashboardHtmlBuilder.Build(
            [new DailyUniqueVisitorCount(new DateOnly(2026, 9, 10), 42)],
            [],
            []);

        html.ShouldContain("Daily unique visitors");
        html.ShouldContain("2026-09-10");
        html.ShouldContain("42");
    }

    [Fact]
    public void Build_renders_the_top_playlists_by_view_and_by_click_tables()
    {
        var html = AnalyticsDashboardHtmlBuilder.Build(
            [],
            [new PlaylistEventCount("masterpieces-of-erkin-the-father", 7)],
            [new PlaylistEventCount("blue-skies-ahead", 3)]);

        html.ShouldContain("Top playlists by page view");
        html.ShouldContain("masterpieces-of-erkin-the-father");
        html.ShouldContain("7");
        html.ShouldContain("Top playlists by Spotify click-through");
        html.ShouldContain("blue-skies-ahead");
        html.ShouldContain("3");
    }

    /// <summary>Boundary: never print a raw visitor_hash - only aggregated counts/slugs/dates.</summary>
    [Fact]
    public void Build_does_not_render_a_style_block_or_any_visitor_hash_looking_value()
    {
        var html = AnalyticsDashboardHtmlBuilder.Build(
            [new DailyUniqueVisitorCount(new DateOnly(2026, 9, 10), 1)],
            [new PlaylistEventCount("some-slug", 1)],
            [new PlaylistEventCount("some-slug", 1)]);

        html.ShouldNotContain("<style");
        html.ShouldContain("<table>");
    }

    [Fact]
    public void Build_html_encodes_a_playlist_slug_that_contains_markup()
    {
        var html = AnalyticsDashboardHtmlBuilder.Build(
            [],
            [new PlaylistEventCount("<script>alert(1)</script>", 1)],
            []);

        html.ShouldNotContain("<script>alert(1)</script>");
        html.ShouldContain("&lt;script&gt;");
    }
}
