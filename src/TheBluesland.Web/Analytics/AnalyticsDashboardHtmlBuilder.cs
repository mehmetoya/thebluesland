using System.Globalization;
using System.Net;
using System.Text;

namespace TheBluesland.Web.Analytics;

/// <summary>One day's distinct visitor-hash count (docs/specs/analytics-dashboard.md, Design section 2).</summary>
public sealed record DailyUniqueVisitorCount(DateOnly Day, int UniqueVisitors);

/// <summary>One playlist's event count for either the by-view or by-click ranking chart.</summary>
public sealed record PlaylistEventCount(string PlaylistSlug, int Count);

/// <summary>
/// Renders the <c>/dashboard</c> route (docs/specs/analytics-dashboard.md, Design section 3) as
/// plain, hand-built HTML with inline SVG bar charts - same "one small dedicated builder per
/// generated-output concern" pattern as <see cref="TheBluesland.Web.Seo.SitemapGenerator"/>/
/// <see cref="TheBluesland.Web.Seo.SocialCardGenerator"/>. Deliberately no chart library and no
/// <c>&lt;script&gt;</c>/<c>&lt;style&gt;</c>: this app's CSP is <c>script-src 'self'</c> with no
/// externally-hosted script allowed and <c>style-src 'self'</c> with no <c>'unsafe-inline'</c> (see
/// docs/specs/dark-light-mode-toggle.md section 2), so every visual attribute below is a plain SVG
/// presentation attribute (<c>fill</c>, <c>x</c>, <c>y</c>, ...), never a <c>style=""</c> string -
/// CSP's style-src only governs the latter.
///
/// Never prints a raw <c>visitor_hash</c> - only aggregated counts/slugs/dates belong on this page
/// (spec Boundaries).
/// </summary>
public static class AnalyticsDashboardHtmlBuilder
{
    private const string DailyVisitorsColor = "#4a7fb5";
    private const string ViewsColor = "#4a9b7f";
    private const string ClicksColor = "#d99a4e";

    public static string Build(
        IReadOnlyList<DailyUniqueVisitorCount> dailyUniqueVisitors,
        IReadOnlyList<PlaylistEventCount> topPlaylistsByView,
        IReadOnlyList<PlaylistEventCount> topPlaylistsByClick)
    {
        var builder = new StringBuilder();
        builder.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<title>TheBluesland analytics</title></head><body>")
            .Append("<h1>TheBluesland analytics</h1>");

        AppendDailyUniquesChart(builder, dailyUniqueVisitors);
        AppendPlaylistBarChart(builder, "Top playlists by page view", topPlaylistsByView, ViewsColor);
        AppendPlaylistBarChart(builder, "Top playlists by Spotify click-through", topPlaylistsByClick, ClicksColor);

        builder.Append("</body></html>");
        return builder.ToString();
    }

    // Vertical bars, oldest-to-newest left-to-right (the query returns newest-first, sorted here
    // purely for chart reading order - a trend reads left-to-right as "then to now", the opposite
    // of how the old table listed rows).
    private static void AppendDailyUniquesChart(StringBuilder builder, IReadOnlyList<DailyUniqueVisitorCount> rows)
    {
        builder.Append("<h2>Daily unique visitors (last 30 days)</h2>");
        if (rows.Count == 0)
        {
            builder.Append("<p>No data yet.</p>");
            return;
        }

        var chronological = rows.OrderBy(row => row.Day).ToList();
        var max = Math.Max(chronological.Max(row => row.UniqueVisitors), 1);

        const int chartHeight = 200;
        const int barWidth = 26;
        const int barGap = 10;
        const int leftPadding = 12;
        const int topPadding = 20;
        const int labelHeight = 34;
        var width = leftPadding * 2 + (chronological.Count * (barWidth + barGap));
        var height = topPadding + chartHeight + labelHeight;
        var baselineY = topPadding + chartHeight;

        builder.Append(CultureInfo.InvariantCulture, $"<svg viewBox=\"0 0 {width} {height}\" width=\"{width}\" height=\"{height}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"Daily unique visitors, last 30 days\">");
        builder.Append(CultureInfo.InvariantCulture, $"<line x1=\"0\" y1=\"{baselineY}\" x2=\"{width}\" y2=\"{baselineY}\" stroke=\"#ccc\" stroke-width=\"1\" />");

        for (var i = 0; i < chronological.Count; i++)
        {
            var row = chronological[i];
            var barHeight = (int)Math.Round((double)row.UniqueVisitors / max * (chartHeight - 24));
            var x = leftPadding + (i * (barWidth + barGap));
            var y = baselineY - barHeight;

            builder.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x}\" y=\"{y}\" width=\"{barWidth}\" height=\"{barHeight}\" fill=\"{DailyVisitorsColor}\" />");
            builder.Append(CultureInfo.InvariantCulture, $"<text x=\"{x + (barWidth / 2)}\" y=\"{y - 4}\" font-size=\"11\" text-anchor=\"middle\" fill=\"#333\">{row.UniqueVisitors}</text>");
            builder.Append(CultureInfo.InvariantCulture, $"<text x=\"{x + (barWidth / 2)}\" y=\"{baselineY + 16}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#333\">{row.Day:MM-dd}</text>");
        }

        builder.Append("</svg>");
    }

    // Horizontal bars, longest first (rows already arrive sorted descending by count) - reads as a
    // ranked list, label on the left, bar length and printed count on the right.
    private static void AppendPlaylistBarChart(StringBuilder builder, string heading, IReadOnlyList<PlaylistEventCount> rows, string barColor)
    {
        builder.Append("<h2>").Append(WebUtility.HtmlEncode(heading)).Append("</h2>");
        if (rows.Count == 0)
        {
            builder.Append("<p>No data yet.</p>");
            return;
        }

        var max = Math.Max(rows.Max(row => row.Count), 1);

        const int rowHeight = 22;
        const int rowGap = 6;
        const int labelWidth = 260;
        const int maxBarWidth = 400;
        const int rightPadding = 40;
        var width = labelWidth + maxBarWidth + rightPadding;
        var height = rows.Count * (rowHeight + rowGap);

        builder.Append(CultureInfo.InvariantCulture, $"<svg viewBox=\"0 0 {width} {height}\" width=\"{width}\" height=\"{height}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"{WebUtility.HtmlEncode(heading)}\">");

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var barWidth = Math.Max((int)Math.Round((double)row.Count / max * maxBarWidth), 2);
            var y = i * (rowHeight + rowGap);
            var textY = y + rowHeight - 6;
            var label = row.PlaylistSlug.Length > 34 ? string.Concat(row.PlaylistSlug.AsSpan(0, 33), "…") : row.PlaylistSlug;

            builder.Append(CultureInfo.InvariantCulture, $"<text x=\"{labelWidth - 8}\" y=\"{textY}\" font-size=\"12\" text-anchor=\"end\" fill=\"#333\">{WebUtility.HtmlEncode(label)}</text>");
            builder.Append(CultureInfo.InvariantCulture, $"<rect x=\"{labelWidth}\" y=\"{y}\" width=\"{barWidth}\" height=\"{rowHeight}\" fill=\"{barColor}\" />");
            builder.Append(CultureInfo.InvariantCulture, $"<text x=\"{labelWidth + barWidth + 6}\" y=\"{textY}\" font-size=\"12\" fill=\"#333\">{row.Count}</text>");
        }

        builder.Append("</svg>");
    }
}
