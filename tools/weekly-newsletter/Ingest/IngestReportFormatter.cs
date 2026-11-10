using System.Globalization;
using System.Text;

namespace TheBluesland.WeeklyNewsletter.Ingest;

/// <summary>
/// Renders an <see cref="IngestReport"/> as the GitHub job-summary Markdown (news-ingest spec,
/// Design section 4, step 6) - the only place ingest health is visible, so it leads with one
/// glanceable line (icon, feeds OK, new, removed), then a database error if there was one, then
/// one row per feed in feeds.yml order. Feed-supplied text is escaped and length-capped so it can
/// neither break the table nor be swallowed by GitHub's HTML sanitizer.
/// </summary>
public static class IngestReportFormatter
{
    private const string NotApplicable = "–";
    private const int MaxCellLength = 200;

    public static string ToMarkdown(IngestReport report, int? removed, DateTimeOffset runAt)
    {
        var succeeded = report.Feeds.Count(feed => feed.Status == FeedRunStatus.Succeeded);
        var total = report.Feeds.Count;
        var icon = report.DatabaseError is not null || succeeded == 0 ? "❌" : succeeded == total ? "✅" : "⚠️";
        var stored = report.DatabaseError is null;

        var markdown = new StringBuilder();
        markdown.Append(CultureInfo.InvariantCulture, $"## News ingest · {runAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC\n\n");
        markdown.Append(CultureInfo.InvariantCulture,
            $"{icon} {succeeded}/{total} feeds OK · {(stored ? report.TotalAdded.ToString(CultureInfo.InvariantCulture) : NotApplicable)} new · {(removed?.ToString(CultureInfo.InvariantCulture) ?? NotApplicable)} removed (older than 8 weeks)\n\n");

        if (!stored)
        {
            markdown.Append(CultureInfo.InvariantCulture, $"**Database error — nothing was stored this run:** {Cell(report.DatabaseError)}\n\n");
        }

        markdown.Append("| Source | Status | Fetched | New | Note |\n");
        markdown.Append("|---|---|---:|---:|---|\n");

        foreach (var feed in report.Feeds)
        {
            var row = feed.Status == FeedRunStatus.Succeeded
                ? $"| {Cell(feed.FeedName)} | ✅ OK | {feed.Fetched} | {(stored ? feed.Added.ToString(CultureInfo.InvariantCulture) : NotApplicable)} |  |"
                : $"| {Cell(feed.FeedName)} | ❌ Failed | {NotApplicable} | {NotApplicable} | {Cell(feed.Error)} |";
            markdown.Append(row).Append('\n');
        }

        return markdown.ToString();
    }

    /// <summary>Keeps arbitrary text (feed names, exception messages) inside one table cell.</summary>
    private static string Cell(string? text)
    {
        var flat = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length > MaxCellLength)
        {
            flat = flat[..(MaxCellLength - 1)] + "…";
        }

        return flat
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}
