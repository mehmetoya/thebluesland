using Shouldly;
using TheBluesland.WeeklyNewsletter.Clustering;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Clustering.ClusteringTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Clustering;

/// <summary>story-clustering spec, Design section 4: the job summary is this module's only UI.</summary>
public sealed class ClusterReportFormatterTests
{
    private static readonly DateTimeOffset From = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);
    private static readonly Dictionary<string, string> Names = new() { ["stereogum"] = "Stereogum", ["rolling-stone"] = "Rolling Stone" };

    private static readonly Story Oasis = new(
        "s-1",
        [Article(1, "stereogum", "Oasis Won’t Attend Hall Of Fame Induction"), Article(2, "rolling-stone", "Oasis to Skip Induction", hoursAfterMonday: 1)],
        ["stereogum", "rolling-stone"]);

    private static readonly Story Lone = new("s-9", [Article(9, "pitchfork", "Lone | Story <b>")], ["pitchfork"]);

    [Fact]
    public void ToMarkdown_headline_counts_articles_stories_and_covered_stories()
    {
        var markdown = ClusterReportFormatter.ToMarkdown([Oasis, Lone], From, To, Options(), Names);

        markdown.ShouldStartWith("## Story clusters · 2026-11-03 10:00 → 2026-11-10 10:00 UTC");
        markdown.ShouldContain("3 articles → 2 stories · 1 covered by 2+ sources · TF-IDF, threshold 0.22");
    }

    [Fact]
    public void ToMarkdown_lists_covered_stories_with_source_names_and_members()
    {
        var markdown = ClusterReportFormatter.ToMarkdown([Oasis, Lone], From, To, Options(), Names);

        markdown.ShouldContain("| 1 | [Oasis Won’t Attend Hall Of Fame Induction](https://example.com/1) | Stereogum · Rolling Stone | 2 |");
        markdown.ShouldContain("- Rolling Stone · 11-09 13:00 · [Oasis to Skip Induction](https://example.com/2)");
    }

    [Fact]
    public void ToMarkdown_folds_single_source_stories_and_escapes_feed_text()
    {
        var markdown = ClusterReportFormatter.ToMarkdown([Oasis, Lone], From, To, Options(), Names);

        markdown.ShouldContain("<details><summary>Single-source stories (1)</summary>");
        // Unknown source ids fall back to the id; pipes and tags cannot break the Markdown.
        markdown.ShouldContain("- pitchfork · [Lone \\| Story &lt;b&gt;](https://example.com/9)");
    }

    [Fact]
    public void ToMarkdown_says_so_when_the_window_is_empty()
    {
        ClusterReportFormatter.ToMarkdown([], From, To, Options(), Names).ShouldContain("No articles in this window.");
    }
}
