using Shouldly;
using TheBluesland.WeeklyNewsletter.Ingest;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter;

/// <summary>
/// The GitHub job summary is the only place Mehmet sees ingest health (news-ingest spec, Design
/// section 4, step 6): one glanceable headline, then one row per feed in feeds.yml order.
/// </summary>
public sealed class IngestReportFormatterTests
{
    private static readonly DateTimeOffset RunAt = new(2026, 11, 10, 8, 17, 0, TimeSpan.Zero);

    [Fact]
    public void ToMarkdown_headline_summarizes_health_new_articles_and_retention()
    {
        var report = new IngestReport(
        [
            new FeedRunResult("billboard", "Billboard", FeedRunStatus.Succeeded, 10, 3, null),
            new FeedRunResult("nme", "NME", FeedRunStatus.Failed, 0, 0, "HTTP 503 ServiceUnavailable"),
        ]);

        var markdown = IngestReportFormatter.ToMarkdown(report, removed: 41, RunAt);

        markdown.ShouldStartWith("## News ingest · 2026-11-10 08:17 UTC");
        markdown.ShouldContain("⚠️ 1/2 feeds OK · 3 new · 41 removed (older than 8 weeks)");
    }

    [Fact]
    public void ToMarkdown_lists_feeds_in_report_order_with_a_row_each()
    {
        var report = new IngestReport(
        [
            new FeedRunResult("billboard", "Billboard", FeedRunStatus.Succeeded, 10, 3, null),
            new FeedRunResult("nme", "NME", FeedRunStatus.Failed, 0, 0, "HTTP 503 ServiceUnavailable"),
        ]);

        var lines = IngestReportFormatter.ToMarkdown(report, removed: 0, RunAt).Split('\n');

        lines.ShouldContain("| Source | Status | Fetched | New | Note |");
        var billboard = Array.IndexOf(lines, "| Billboard | ✅ OK | 10 | 3 |  |");
        var nme = Array.IndexOf(lines, "| NME | ❌ Failed | – | – | HTTP 503 ServiceUnavailable |");
        billboard.ShouldBeGreaterThan(0);
        nme.ShouldBeGreaterThan(billboard);
    }

    [Theory]
    [InlineData(2, 2, "✅")]
    [InlineData(1, 2, "⚠️")]
    [InlineData(0, 2, "❌")]
    public void ToMarkdown_headline_icon_reflects_how_many_feeds_succeeded(int succeeded, int total, string icon)
    {
        var feeds = Enumerable.Range(0, total)
            .Select(i => i < succeeded
                ? new FeedRunResult($"f{i}", $"Feed {i}", FeedRunStatus.Succeeded, 1, 0, null)
                : new FeedRunResult($"f{i}", $"Feed {i}", FeedRunStatus.Failed, 0, 0, "HTTP 500 InternalServerError"))
            .ToList();

        var markdown = IngestReportFormatter.ToMarkdown(new IngestReport(feeds), removed: 0, RunAt);

        markdown.ShouldContain($"{icon} {succeeded}/{total} feeds OK");
    }

    [Fact]
    public void ToMarkdown_escapes_html_and_backticks_and_caps_long_notes()
    {
        var report = new IngestReport([new FeedRunResult("x", "X", FeedRunStatus.Failed, 0, 0,
            "Unsupported feed root element <html>; `code` " + new string('z', 500))]);

        var row = IngestReportFormatter.ToMarkdown(report, removed: 0, RunAt).Split('\n').Single(line => line.StartsWith("| X |", StringComparison.Ordinal));

        row.ShouldContain("&lt;html&gt;");
        row.ShouldContain("\\`code\\`");
        row.Length.ShouldBeLessThan(300);
    }

    [Fact]
    public void ToMarkdown_database_error_is_called_out_and_new_counts_are_not_claimed()
    {
        var report = new IngestReport(
            [new FeedRunResult("billboard", "Billboard", FeedRunStatus.Succeeded, 10, 0, null)],
            DatabaseError: "NpgsqlException: Failed to connect");

        var markdown = IngestReportFormatter.ToMarkdown(report, removed: null, RunAt);

        markdown.ShouldContain("❌ 1/1 feeds OK · – new · – removed");
        markdown.ShouldContain("**Database error — nothing was stored this run:** NpgsqlException: Failed to connect");
        markdown.ShouldContain("| Billboard | ✅ OK | 10 | – |");
    }

    [Fact]
    public void ToMarkdown_escapes_pipes_and_flattens_newlines_in_error_text()
    {
        var report = new IngestReport([new FeedRunResult("x", "X | Y", FeedRunStatus.Failed, 0, 0, "bad\nthing | here")]);

        var markdown = IngestReportFormatter.ToMarkdown(report, removed: 0, RunAt);

        markdown.ShouldContain("| X \\| Y | ❌ Failed | – | – | bad thing \\| here |");
    }
}
