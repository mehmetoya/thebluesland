using Shouldly;
using TheBluesland.WeeklyNewsletter.Ranking;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Ranking.RankingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Ranking;

/// <summary>story-ranking spec, Design section 5: every pick shows why it is there.</summary>
public sealed class RankReportFormatterTests
{
    private static readonly DateTimeOffset From = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);
    private static readonly Dictionary<string, string> Names = new() { ["source-0"] = "Pitchfork", ["source-1"] = "Stereogum" };

    [Fact]
    public void Why_lists_sources_type_bonus_and_score_with_invariant_numbers()
    {
        RankReportFormatter.Why(new RankedStory(Story("Alma Brightwater dies", sources: 2), 3.5, "obituary", 1.5, false)).ShouldBe("2 sources · obituary +1.5 = 3.50");
        RankReportFormatter.Why(new RankedStory(Story("Paper Lanterns single", sources: 1), 1, "release", 0, false)).ShouldBe("1 source · release = 1.00");
    }

    [Fact]
    public void ToMarkdown_shows_headline_picks_backups_and_passed_over_stories()
    {
        var pick = new RankedStory(Story("Alma Brightwater | dies <b>", sources: 2), 3.5, "obituary", 1.5, true);
        var backup = new RankedStory(Story("Paper Lanterns single", sources: 1), 1, "release", 0, false);
        var skipped = new SkippedStory(new RankedStory(Story("Best songs list", sources: 1), 1, "roundup", 0, false), "roundup: not a news story");

        var markdown = RankReportFormatter.ToMarkdown(new RankingResult([pick], [backup], [skipped], "not needed (x)"), 3, From, To, Names);

        markdown.ShouldStartWith("## Weekly pick · 2026-11-03 10:00 → 2026-11-10 10:00 UTC");
        markdown.ShouldContain("3 stories · 1 picked + 1 backups · genre slot: not needed (x)");
        markdown.ShouldContain("| 1 | [Alma Brightwater \\| dies &lt;b&gt;](https://example.com/");
        markdown.ShouldContain("| Pitchfork · Stereogum | 2 sources · obituary +1.5 = 3.50 |");
        markdown.ShouldContain("| B1 | [Paper Lanterns single]");
        markdown.ShouldContain("<details><summary>Passed over (1)</summary>");
        markdown.ShouldContain("— roundup: not a news story");
    }

    [Fact]
    public void ToMarkdown_says_so_when_nothing_can_be_picked()
    {
        RankReportFormatter.ToMarkdown(new RankingResult([], [], [], "not applied"), 0, From, To, Names).ShouldContain("No stories to pick in this window.");
    }
}
