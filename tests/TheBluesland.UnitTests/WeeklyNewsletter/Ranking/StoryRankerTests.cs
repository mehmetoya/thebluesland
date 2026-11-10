using Shouldly;
using TheBluesland.WeeklyNewsletter.Ranking;
using Xunit;
using StoryModel = TheBluesland.WeeklyNewsletter.Clustering.Story;
using static TheBluesland.UnitTests.WeeklyNewsletter.Clustering.ClusteringTestData;
using static TheBluesland.UnitTests.WeeklyNewsletter.Ranking.RankingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Ranking;

/// <summary>
/// story-ranking spec, Design sections 1–2. Headlines are realistic and distinct, and every week has
/// <see cref="RankingTestData.Background"/> so IDF behaves as in a real week.
/// </summary>
public sealed class StoryRankerTests
{
    private static RankingResult Rank(RankingOptions settings, params StoryModel[] stories) =>
        StoryRanker.Rank([.. Background(), .. stories], settings, Options());

    [Fact]
    public void Rank_scores_coverage_plus_type_bonus_and_orders_by_score()
    {
        var obituary = Story("Guitar pioneer Alma Brightwater dies at 80 in Memphis", sources: 2);    // 2 + 1.5
        var release = Story("Corvid Lane share surprise single Paper Lanterns", sources: 3);           // 3 + 0
        var tour = Story("Ditto Harbour announce sprawling 2027 tour of Asia", sources: 3);           // 3 + 0.25

        var result = Rank(Settings(picks: 3, backups: 0), release, tour, obituary);

        result.Picks.Select(pick => pick.Story).ShouldBe([obituary, tour, release]);
        result.Picks[0].Score.ShouldBe(3.5);
        result.Picks[0].Type.ShouldBe("obituary");
        result.Picks[0].Bonus.ShouldBe(1.5);
    }

    [Fact]
    public void Rank_brand_bonus_lets_a_two_source_blues_story_beat_a_three_source_off_brand_one()
    {
        var offBrand = Story("Corvid Lane share surprise single Paper Lanterns", sources: 3);                       // 3 + 0
        var blues = Story("Delta bluesman Alma Brightwater announces farewell residency", sources: 2, hoursAfterMonday: 5); // 2 + 0.25 + 1.5
        var settings = Settings(picks: 2, backups: 0, brandBonus: 1.5, brandKeywords: ["blues", "bluesman"]);

        var result = Rank(settings, offBrand, blues);

        result.Picks[0].Story.ShouldBe(blues);
        result.Picks[0].BrandBonus.ShouldBe(1.5);
        result.Picks[1].BrandBonus.ShouldBe(0);
        Rank(Settings(picks: 2, backups: 0), offBrand, blues).Picks[0].Story.ShouldBe(offBrand);
    }

    [Fact]
    public void Rank_core_source_gives_a_single_source_story_the_brand_bonus_once()
    {
        var core = Story("Fennel Avenue reveal acoustic version of Glass Garden", sources: 1) with { Sources = ["louder"] };
        var plain = Story("Halcyon Drift unveil remix album Static Bloom", sources: 1);
        var settings = Settings(picks: 2, backups: 0, brandBonus: 1.0, brandKeywords: ["rock"], coreSources: ["louder"]);

        var result = Rank(settings, plain, core);

        result.Picks[0].Story.ShouldBe(core);
        result.Picks[0].Score.ShouldBe(2.0);
    }

    [Fact]
    public void Rank_brand_bonus_does_not_rescue_roundups()
    {
        var roundup = Story("Best songs of the week: rock edition", sources: 1);

        var result = Rank(Settings(brandBonus: 1.0, brandKeywords: ["rock"]), roundup);

        result.Picks.ShouldBeEmpty();
        result.Skipped.ShouldContain(skipped => skipped.Story.Story == roundup && skipped.Story.BrandBonus == 0);
    }

    [Fact]
    public void Rank_breaks_score_ties_by_article_count_then_recency()
    {
        var older = Story("Fennel Avenue reveal acoustic version of Glass Garden", sources: 2, hoursAfterMonday: 0);
        var newer = Story("Halcyon Drift unveil remix album Static Bloom", sources: 2, hoursAfterMonday: 10);

        Rank(Settings(picks: 2, backups: 0), older, newer).Picks[0].Story.ShouldBe(newer);
    }

    [Fact]
    public void Rank_takes_at_most_two_stories_of_one_type()
    {
        var first = Story("Arcadia Fire Sale announce North American tour with Kestrel", 3);
        var second = Story("Bellwether Union plot European tour dates for spring", 3);
        var third = Story("Cobalt Saints confirm stadium tour across Brazil and Chile", 3);
        var single = Story("Dovetail Mirage drop moody ballad Velvet Static", 1);

        var result = Rank(Settings(picks: 3, backups: 0), first, second, third, single);

        result.Picks.Count(pick => pick.Type == "live").ShouldBe(2);
        result.Picks[^1].Story.ShouldBe(single);
        result.Skipped.ShouldContain(skip => skip.Story.Story == third && skip.Reason == "live: already 2 chosen");
    }

    [Fact]
    public void Rank_takes_only_one_of_two_related_stories()
    {
        var first = Story("Robbie Williams defends Olivia Rodrigo over Take Me Home claims", sources: 2);
        var second = Story("Christmas Wrapping songwriter calls Olivia Rodrigo Take Me Home an homage", sources: 2);
        var other = Story("Kilnworth Choir release field recordings from Lisbon", sources: 1);

        var result = Rank(Settings(picks: 2, backups: 0), first, second, other);

        result.Picks.Select(pick => pick.Story).ShouldBe([first, other]);
        result.Skipped.ShouldContain(skip => skip.Story.Story == second && skip.Reason.StartsWith("related to", StringComparison.Ordinal));
    }

    [Fact]
    public void Rank_never_picks_excluded_types()
    {
        var roundup = Story("The 5 best songs of the week from everywhere", sources: 7);
        var news = Story("Marlowe Pines sign with independent label Quiet Hours", 1);

        var result = Rank(Settings(picks: 2, backups: 0), roundup, news);

        result.Picks.ShouldHaveSingleItem().Story.ShouldBe(news);
        result.Skipped.ShouldContain(skip => skip.Story.Story == roundup && skip.Reason == "roundup: not a news story");
    }

    [Fact]
    public void Genre_slot_swaps_the_last_pick_for_a_strong_enough_genre_story()
    {
        var top = Story("Streaming giant Northwind strikes charts deal with Billboard", 3);
        var pop = Story("Popstar Juniper Vale teases glossy comeback single", 2);
        var indie = Story("Indie trio Lantern Moth return with lo-fi single", 2);

        var result = Rank(Settings(picks: 2, backups: 1), top, pop, indie);

        result.Picks.Select(pick => pick.Story).ShouldBe([top, indie]);
        result.GenreSlot.ShouldStartWith("swapped in");
        result.Backups.ShouldHaveSingleItem().Story.ShouldBe(pop);
    }

    [Fact]
    public void Genre_slot_keeps_the_pick_when_the_genre_story_is_too_weak()
    {
        var top = Story("Streaming giant Northwind strikes charts deal with Billboard", 3);
        var pop = Story("Popstar Juniper Vale teases glossy comeback single", 3);
        var indie = Story("Indie trio Lantern Moth return with lo-fi single", 1);

        var result = Rank(Settings(picks: 2, backups: 0), top, pop, indie);

        result.Picks[^1].Story.ShouldBe(pop);
        result.GenreSlot.ShouldStartWith("kept:");
    }

    [Fact]
    public void Genre_slot_is_not_needed_when_a_pick_already_matches()
    {
        var result = Rank(
            Settings(picks: 2, backups: 0),
            Story("Rock guitarist Ossian Grey dies at 71 after long illness", 3),
            Story("Popstar Juniper Vale teases glossy comeback single", 2));

        result.GenreSlot.ShouldStartWith("not needed");
    }

    [Fact]
    public void Rank_handles_short_and_empty_weeks()
    {
        var empty = StoryRanker.Rank([], Settings(), Options());
        empty.Picks.ShouldBeEmpty();
        empty.Backups.ShouldBeEmpty();

        var shortWeek = Rank(Settings(), Story("Nimbus Quarry share live session from Oslo", 1), Story("Orchid Telegraph cover Kate Bush classic", 1));
        shortWeek.Picks.Count.ShouldBe(2);
        shortWeek.Backups.ShouldBeEmpty();
        shortWeek.GenreSlot.ShouldBe("not applied (fewer stories than picks)");
    }

    [Fact]
    public void Rank_is_deterministic_regardless_of_input_order()
    {
        StoryModel[] stories =
        [
            Story("Arcadia Fire Sale announce North American tour with Kestrel", 3),
            Story("Bellwether Union plot European tour dates for spring", 3),
            Story("Guitar pioneer Alma Brightwater dies at 80 in Memphis", 2),
            Story("Corvid Lane share surprise single Paper Lanterns", 2),
        ];
        var expected = Describe(Rank(Settings(picks: 2, backups: 2), stories));

        Describe(Rank(Settings(picks: 2, backups: 2), [.. stories.Reverse()])).ShouldBe(expected);
    }

    private static string Describe(RankingResult result) =>
        string.Join(',', result.Picks.Concat(result.Backups).Select(pick => pick.Story.Key));
}
