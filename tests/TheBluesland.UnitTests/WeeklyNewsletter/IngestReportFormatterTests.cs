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
    public void ToMarkdown_escapes_pipes_and_flattens_newlines_in_error_text()
    {
        var report = new IngestReport([new FeedRunResult("x", "X | Y", FeedRunStatus.Failed, 0, 0, "bad\nthing | here")]);

        var markdown = IngestReportFormatter.ToMarkdown(report, removed: 0, RunAt);

        markdown.ShouldContain("| X \\| Y | ❌ Failed | – | – | bad thing \\| here |");
    }
}
