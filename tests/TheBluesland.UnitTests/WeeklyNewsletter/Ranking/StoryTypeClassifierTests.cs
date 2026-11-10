using Shouldly;
using TheBluesland.WeeklyNewsletter.Ranking;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Ranking.RankingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Ranking;

/// <summary>story-ranking spec, Design section 1: the type table.</summary>
public sealed class StoryTypeClassifierTests
{
    [Theory]
    [InlineData("Richard Lloyd, Television Guitarist, Dies at 75", "obituary")]
    [InlineData("YouTube Streams Will Once Again Count Toward Billboard Charts", "industry")]
    [InlineData("Nick Cave Launches 2027 Tour", "live")]
    [InlineData("The 5 Best Songs Of The Week", "roundup")]
    [InlineData("Black Belt Eagle Scout Shares New Song “Fantasy”", "release")]
    public void Classify_uses_the_first_matching_rule_or_release(string headline, string expected)
    {
        StoryTypeClassifier.Classify(Story(headline), Types).Name.ShouldBe(expected);
    }

    [Fact]
    public void Classify_earlier_rules_win()
    {
        StoryTypeClassifier.Classify(Story("Juan Atkins’ Catalog Surges on Charts After Producer’s Death"), Types).Name.ShouldBe("obituary");
    }

    [Fact]
    public void Classify_matches_whole_words_only_and_honours_exclusions()
    {
        StoryTypeClassifier.Classify(Story("Divorce, Near-Death, and Drugs: Darius Rucker Documentary"), Types).Name.ShouldBe("release");
        StoryTypeClassifier.Classify(Story("Studies Show Touring Is Hard"), Types).Name.ShouldBe("release");
    }

    [Fact]
    public void Classify_looks_at_every_member_headline()
    {
        var story = Story("Oasis Won’t Attend The Ceremony", sources: 1);
        var other = Story("Oasis Won’t Attend: Tour Plans Unaffected", sources: 1);
        var combined = story with { Articles = [.. story.Articles, .. other.Articles] };

        StoryTypeClassifier.Classify(combined, Types).Name.ShouldBe("live");
    }

    [Fact]
    public void MatchesGenre_checks_headlines_and_summaries_as_whole_words()
    {
        StoryTypeClassifier.MatchesGenre(Story("Oasis to Skip Hall of Fame", summary: "The rock band says no."), ["rock"]).ShouldBeTrue();
        StoryTypeClassifier.MatchesGenre(Story("Rockstar Games Announces Festival"), ["rock"]).ShouldBeFalse();
    }
}
