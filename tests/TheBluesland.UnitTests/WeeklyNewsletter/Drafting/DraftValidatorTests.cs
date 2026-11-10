using Shouldly;
using TheBluesland.WeeklyNewsletter.Drafting;
using Xunit;
using Xunit.Abstractions;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

/// <summary>issue-drafting spec, Design section 3. The approved sample issue is the calibration.</summary>
public sealed class DraftValidatorTests(ITestOutputHelper output)
{
    private static readonly FactCard Card = W46Card();

    [Theory]
    [InlineData("Türkçe")]
    [InlineData("English")]
    public void The_approved_sample_issue_passes_with_no_warning(string language)
    {
        var sample = SampleIssue(language, Card);
        sample.Stories.Count.ShouldBe(5);

        var (clean, warnings) = DraftValidator.Validate(sample, Card);

        warnings.ToList().ForEach(warning => output.WriteLine($"{warning.Where}: {warning.Message}"));
        warnings.ShouldBeEmpty();
        clean.ShouldBe(sample with { Stories = clean.Stories });
        clean.Stories.Select(story => story.Body).ShouldBe(sample.Stories.Select(story => story.Body));
    }

    [Fact]
    public void Numbers_names_and_copies_that_are_not_in_the_card_are_flagged()
    {
        var key = Card.Picks.First().Key;
        var draft = new DraftResponse(
            "Bu hafta beş haber var.",
            "Haftaya görüşürüz.",
            [new DraftStory(
                key,
                "Richard Lloyd hayatını kaybetti",
                "Lloyd 1977'de Marquee Moon'u kaydetti. Lloyd co-founded the pioneering rock band alongside Tom Verlaine.",
                "Önemli bir kayıp.")]);

        var (_, warnings) = DraftValidator.Validate(draft, Card);

        warnings.ShouldContain(warning => warning.Where == key && warning.Message.Contains("number “1977”", StringComparison.Ordinal));
        warnings.ShouldContain(warning => warning.Message.Contains("name “Marquee”", StringComparison.Ordinal));
        warnings.ShouldContain(warning => warning.Message.Contains("copies a summary", StringComparison.Ordinal));
    }

    [Fact]
    public void Links_brackets_emoji_and_exclamation_marks_are_removed_and_reported()
    {
        var key = Card.Picks.First().Key;
        var draft = new DraftResponse("Bu hafta! 🎸 beş haber.", "Görüşürüz.", [new DraftStory(key, "Başlık", "Bkz. [kaynak](https://x.test/a) <b>burada</b>.", "Önemli.")]);

        var (clean, warnings) = DraftValidator.Validate(draft, Card);

        clean.Intro.ShouldBe("Bu hafta beş haber.");
        clean.Stories[0].Body.ShouldBe("Bkz. kaynak burada.");
        warnings.Count(warning => warning.Message.Contains("removed links", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public void Lengths_and_sentence_counts_are_flagged()
    {
        var key = Card.Picks.First().Key;
        var draft = new DraftResponse(
            "Giriş.",
            "Kapanış.",
            [new DraftStory(key, new string('a', 71), "Bir. İki. Üç. Dört.", "Bir. İki. Üç.")]);

        var (_, warnings) = DraftValidator.Validate(draft, Card);

        warnings.ShouldContain(warning => warning.Message == "headline: 71 characters (limit 70).");
        warnings.ShouldContain(warning => warning.Message == "body: more than 3 sentences.");
        warnings.ShouldContain(warning => warning.Message == "why it matters: more than 2 sentences.");
    }
}
