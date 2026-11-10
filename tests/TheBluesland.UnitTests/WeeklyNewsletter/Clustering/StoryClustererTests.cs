using System.Diagnostics;
using Shouldly;
using TheBluesland.WeeklyNewsletter.Clustering;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Clustering.ClusteringTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Clustering;

/// <summary>story-clustering spec, Design sections 1–2.</summary>
public sealed class StoryClustererTests
{
    private static readonly ClusterInput OasisStereogum = Article(1, "stereogum", "Oasis Won’t Attend Their Rock & Roll Hall Of Fame Induction");
    private static readonly ClusterInput OasisRollingStone = Article(2, "rolling-stone", "Oasis Won’t Be Attending Rock and Roll of Fame Induction Ceremony", hoursAfterMonday: 2);
    private static readonly ClusterInput OasisConsequence = Article(3, "consequence", "Oasis to Skip Rock and Roll Hall of Fame Induction Ceremony", hoursAfterMonday: 1);

    [Fact]
    public void Cluster_groups_one_event_from_several_sources_and_counts_each_source_once()
    {
        var stories = StoryClusterer.Cluster([.. Filler(40), OasisStereogum, OasisRollingStone, OasisConsequence], Options());

        var oasis = stories[0];
        oasis.Key.ShouldBe("s-1");
        oasis.Articles.Select(article => article.Id).ShouldBe([1, 3, 2]);
        oasis.Sources.ShouldBe(["stereogum", "consequence", "rolling-stone"]);
        oasis.SourceCount.ShouldBe(3);
        oasis.FirstSeen.ShouldBe(Monday);
        oasis.LastSeen.ShouldBe(Monday.AddHours(2));
        stories.Count.ShouldBe(41);
    }

    [Fact]
    public void Cluster_never_links_two_articles_from_the_same_source()
    {
        var first = Article(1, "billboard", "Del Casher, Inventor of Guitar Wah-Wah Pedal, Dies at 88");
        var second = Article(2, "billboard", "15 Great Songs That Use the Wah-Wah Guitar Pedal", "Del Casher, inventor of the wah-wah pedal, died at 88.");

        var stories = StoryClusterer.Cluster([.. Filler(40), first, second], Options());

        stories.ShouldNotContain(story => story.Articles.Count > 1);
    }

    [Fact]
    public void Cluster_never_links_articles_further_apart_than_the_window()
    {
        var late = OasisRollingStone with { EffectiveAt = Monday.AddHours(97) };

        var stories = StoryClusterer.Cluster([.. Filler(40), OasisStereogum, late], Options(maxHoursApart: 96));

        stories.ShouldNotContain(story => story.Articles.Count > 1);
    }

    [Fact]
    public void Cluster_keeps_an_article_alone_when_it_only_shares_a_name_with_another_story()
    {
        // Average linkage: the billionaire article is close to one VetsAid article only via the
        // shared name, so it must not be chained into the VetsAid story.
        var vetsAidStereogum = Article(1, "stereogum", "Paul McCartney & Ringo Starr To Perform With Joe Walsh At VetsAid Benefit");
        var vetsAidRollingStone = Article(2, "rolling-stone", "Paul McCartney and Ringo Starr to Reunite at Las Vegas Show With Joe Walsh");
        var billionaire = Article(3, "billboard", "Paul McCartney Is a Billionaire — and the World’s Third-Richest Musician");

        var stories = StoryClusterer.Cluster([.. Filler(40), vetsAidStereogum, vetsAidRollingStone, billionaire], Options());

        stories.Single(story => story.Articles.Any(article => article.Id == 3)).Articles.Count.ShouldBe(1);
        stories.Single(story => story.Articles.Any(article => article.Id == 1)).Articles.Select(article => article.Id).ShouldBe([1, 2]);
    }

    [Fact]
    public void Cluster_is_deterministic_regardless_of_input_order()
    {
        List<ClusterInput> articles = [.. Filler(60), OasisStereogum, OasisRollingStone, OasisConsequence];
        var expected = Describe(StoryClusterer.Cluster(articles, Options()));

        var random = new Random(46);
        for (var run = 0; run < 5; run++)
        {
            var shuffled = articles.OrderBy(_ => random.Next()).ToList();
            Describe(StoryClusterer.Cluster(shuffled, Options())).ShouldBe(expected);
        }
    }

    [Fact]
    public void Cluster_returns_nothing_for_an_empty_week_and_a_lone_story_for_one_article()
    {
        StoryClusterer.Cluster([], Options()).ShouldBeEmpty();

        var single = StoryClusterer.Cluster([OasisStereogum], Options()).ShouldHaveSingleItem();
        single.Key.ShouldBe("s-1");
        single.SourceCount.ShouldBe(1);
    }

    [Fact]
    public void Cluster_orders_stories_by_source_count_then_first_seen()
    {
        var older = Article(10, "pitchfork", "Lone Older Story About Something", hoursAfterMonday: -5);

        var stories = StoryClusterer.Cluster([.. Filler(40), older, OasisStereogum, OasisRollingStone], Options());

        stories[0].Key.ShouldBe("s-1");
        stories[1].Key.ShouldBe("s-10");
    }

    [Fact]
    public void Cluster_handles_a_thousand_article_week_with_many_merges_within_five_seconds()
    {
        // 800 unrelated articles plus 100 events each reported by two sources: 100 merges, which
        // exercises the merge loop, not only the similarity matrix.
        var week = Filler(800).ToList();
        for (var index = 0; index < 100; index++)
        {
            var headline = $"Performer{index} cancels arena residency Venue{index} after injury Injury{index}";
            week.Add(Article(5000 + index, "stereogum", headline, hoursAfterMonday: index % 40));
            week.Add(Article(6000 + index, "consequence", headline + " report", hoursAfterMonday: (index % 40) + 1));
        }

        var stopwatch = Stopwatch.StartNew();
        var stories = StoryClusterer.Cluster(week, Options());
        stopwatch.Stop();

        stories.Count(story => story.SourceCount == 2).ShouldBe(100);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    private static string Describe(IReadOnlyList<Story> stories) =>
        string.Join('\n', stories.Select(story => $"{story.Key}:{string.Join(',', story.Articles.Select(article => article.Id))}"));
}
