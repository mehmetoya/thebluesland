using Shouldly;
using TheBluesland.UnitTests.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Ingest;
using TheBluesland.WeeklyNewsletter.Ranking;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Ranking.RankingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Ranking;

/// <summary>
/// Regression tests against the repository's own ranking.yml (story-ranking spec, Design section 1
/// and "Kaynak dengesi"): legal needs legal context, non-music launches earn no "live" bonus, the
/// brand bonus applies, and every core source is a real feed.
/// </summary>
public sealed class RankingConfigTests
{
    private static RankingOptions Repository()
    {
        var result = RankingOptionsLoader.LoadAsync(RepoFiles.Find("tools/weekly-newsletter/ranking.yml"), CancellationToken.None).GetAwaiter().GetResult();
        result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors));
        return result.Options!;
    }

    [Theory]
    [InlineData("Young singer signs a record contract in Memphis", "release")]
    [InlineData("Band signs new publishing contract and shares first single", "release")]
    [InlineData("Producer sues label over unpaid royalties", "legal")]
    [InlineData("Singer settles copyright lawsuit with former bandmate", "legal")]
    [InlineData("Court rules in contract dispute between rapper and management", "legal")]
    public void Classify_contract_alone_is_not_legal_but_legal_context_is(string headline, string expected)
    {
        StoryTypeClassifier.Classify(Story(headline), Repository().Types).Name.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Grand Theft Auto VI: The Festival to Feature Travis Scott, Cardi B, and Justice")]
    [InlineData("GTA VI launch party headlines concert series")]
    public void Classify_video_game_launches_do_not_get_the_live_bonus(string headline)
    {
        var rule = StoryTypeClassifier.Classify(Story(headline), Repository().Types);

        rule.Name.ShouldBe("videogame");
        rule.Bonus.ShouldBe(0);
    }

    [Fact]
    public void Classify_music_festivals_still_count_as_live()
    {
        StoryTypeClassifier.Classify(Story("Newport Jazz Festival announces 2027 headliners"), Repository().Types).Name.ShouldBe("live");
    }

    [Fact]
    public void Brand_settings_lift_a_multi_source_blues_story_but_not_a_single_source_one()
    {
        var options = Repository();

        options.BrandKeywords.ShouldContain("blues");
        options.BrandKeywords.ShouldContain("rock");
        options.BrandKeywords.ShouldContain("jazz");
        var blues = Story("Blues guitarist announces farewell to the road", sources: 2);
        StoryTypeClassifier.FitsBrand(blues, options.BrandKeywords, options.CoreSources).ShouldBeTrue();
        // 2026-11-10 preview: one site's single-source stories crowded out the week's big news, so
        // the bonus needs two sources and stays below one extra source of coverage.
        options.BrandMinSources.ShouldBe(2);
        options.BrandBonus.ShouldBeLessThan(options.CoverageWeight);
        (2 + options.BrandBonus).ShouldBeGreaterThan(2.5 * options.CoverageWeight);
    }

    [Theory]
    [InlineData("Horror punk icons Murderdolls are reforming for their first show since Joey Jordison's death", "milestone")]
    [InlineData("Metal Hammer's tracks of the week: November 6 2026", "roundup")]
    [InlineData("Blues Rock Weekly – November 6, 2026", "roundup")]
    [InlineData("Richard Lloyd, Television Guitarist, Dies at 75", "obituary")]
    public void Regression_2026_11_10_preview_stories_get_the_right_type(string headline, string expected)
    {
        StoryTypeClassifier.Classify(Story(headline), Repository().Types).Name.ShouldBe(expected);
    }

    [Fact]
    public async Task Every_core_source_is_an_enabled_feed_in_feeds_yml()
    {
        var feeds = await FeedConfigLoader.LoadAsync(RepoFiles.Find("tools/weekly-newsletter/feeds.yml"), CancellationToken.None);

        feeds.IsValid.ShouldBeTrue();
        Repository().CoreSources.ShouldNotBeEmpty();
        Repository().CoreSources.ShouldAllBe(id => feeds.EnabledFeeds.Any(feed => feed.Id == id));
    }
}
