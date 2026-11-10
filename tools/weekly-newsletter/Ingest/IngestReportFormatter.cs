using System.Globalization;
using System.Text;

namespace TheBluesland.WeeklyNewsletter.Ingest;

/// <summary>
/// Renders an <see cref="IngestReport"/> as the GitHub job-summary Markdown (news-ingest spec,
/// Design section 4, step 6) - the only place ingest health is visible, so it leads with one
/// glanceable line (icon, feeds OK, new, removed) and then one row per feed in feeds.yml order.
/// </summary>
public static class IngestReportFormatter
{
    private const string NotApplicable = "–";

    public static string ToMarkdown(IngestReport report, int removed, DateTimeOffset runAt)
    {
        var succeeded = report.Feeds.Count(feed => feed.Status == FeedRunStatus.Succeeded);
        var total = report.Feeds.Count;
        var icon = succeeded == total ? "✅" : succeeded == 0 ? "❌" : "⚠️";

        var markdown = new StringBuilder();
        markdown.Append(CultureInfo.InvariantCulture, $"## News ingest · {runAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC\n\n");
        markdown.Append(CultureInfo.InvariantCulture, $"{icon} {succeeded}/{total} feeds OK · {report.TotalAdded} new · {removed} removed (older than 8 weeks)\n\n");
        markdown.Append("| Source | Status | Fetched | New | Note |\n");
        markdown.Append("|---|---|---:|---:|---|\n");

        foreach (var feed in report.Feeds)
        {
            var row = feed.Status == FeedRunStatus.Succeeded
                ? $"| {Cell(feed.FeedName)} | ✅ OK | {feed.Fetched} | {feed.Added} |  |"
                : $"| {Cell(feed.FeedName)} | ❌ Failed | {NotApplicable} | {NotApplicable} | {Cell(feed.Error)} |";
            markdown.Append(row).Append('\n');
        }

        return markdown.ToString();
    }

    /// <summary>Keeps arbitrary text (feed names, exception messages) inside one table cell.</summary>
    private static string Cell(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Replace("|", "\\|", StringComparison.Ordinal);
}
