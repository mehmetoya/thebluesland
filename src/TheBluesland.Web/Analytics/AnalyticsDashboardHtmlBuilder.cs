using System.Net;
using System.Text;

namespace TheBluesland.Web.Analytics;

/// <summary>One day's distinct visitor-hash count (docs/specs/analytics-dashboard.md, Design section 2).</summary>
public sealed record DailyUniqueVisitorCount(DateOnly Day, int UniqueVisitors);

/// <summary>One playlist's event count for either the by-view or by-click ranking table.</summary>
public sealed record PlaylistEventCount(string PlaylistSlug, int Count);

/// <summary>
/// Renders the <c>/dashboard</c> route's three tables (docs/specs/analytics-dashboard.md, Design
/// section 3) as plain, hand-built semantic HTML - same "one small dedicated builder per
/// generated-output concern" pattern as <see cref="TheBluesland.Web.Seo.SitemapGenerator"/>/
/// <see cref="TheBluesland.Web.Seo.SocialCardGenerator"/>. This is a single-owner diagnostic page,
/// not a public one: no design tokens, no dark/light theme, and deliberately no <c>&lt;style&gt;</c>
/// block - this app's Content-Security-Policy has no <c>'unsafe-inline'</c> for <c>style-src</c>
/// (same reasoning as <c>script-src</c>; see docs/specs/dark-light-mode-toggle.md section 2), and an
/// unstyled <c>&lt;table&gt;</c> is entirely adequate for this audience.
///
/// Never prints a raw <c>visitor_hash</c> - only aggregated counts/slugs/dates belong on this page
/// (spec Boundaries).
/// </summary>
public static class AnalyticsDashboardHtmlBuilder
{
    public static string Build(
        IReadOnlyList<DailyUniqueVisitorCount> dailyUniqueVisitors,
        IReadOnlyList<PlaylistEventCount> topPlaylistsByView,
        IReadOnlyList<PlaylistEventCount> topPlaylistsByClick)
    {
        var builder = new StringBuilder();
        builder.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<title>TheBluesland analytics</title></head><body>")
            .Append("<h1>TheBluesland analytics</h1>");

        AppendDailyUniquesTable(builder, dailyUniqueVisitors);
        AppendPlaylistCountTable(builder, "Top playlists by page view", topPlaylistsByView);
        AppendPlaylistCountTable(builder, "Top playlists by Spotify click-through", topPlaylistsByClick);

        builder.Append("</body></html>");
        return builder.ToString();
    }

    private static void AppendDailyUniquesTable(StringBuilder builder, IReadOnlyList<DailyUniqueVisitorCount> rows)
    {
        builder.Append("<h2>Daily unique visitors (last 30 days)</h2>")
            .Append("<table><thead><tr><th>Day</th><th>Unique visitors</th></tr></thead><tbody>");

        foreach (var row in rows)
        {
            builder.Append("<tr><td>").Append(row.Day.ToString("yyyy-MM-dd")).Append("</td><td>")
                .Append(row.UniqueVisitors).Append("</td></tr>");
        }

        builder.Append("</tbody></table>");
    }

    private static void AppendPlaylistCountTable(StringBuilder builder, string heading, IReadOnlyList<PlaylistEventCount> rows)
    {
        builder.Append("<h2>").Append(WebUtility.HtmlEncode(heading)).Append("</h2>")
            .Append("<table><thead><tr><th>Playlist slug</th><th>Count</th></tr></thead><tbody>");

        foreach (var row in rows)
        {
            builder.Append("<tr><td>").Append(WebUtility.HtmlEncode(row.PlaylistSlug)).Append("</td><td>")
                .Append(row.Count).Append("</td></tr>");
        }

        builder.Append("</tbody></table>");
    }
}
