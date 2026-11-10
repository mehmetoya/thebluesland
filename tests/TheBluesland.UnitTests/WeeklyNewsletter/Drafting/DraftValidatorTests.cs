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
    public void Newlines_and_separators_never_survive_so_front_matter_and_headings_stay_intact()
    {
        var key = Card.Picks.First().Key;
        var draft = DraftResponse.Parse(FakeAnswer(Card.Stories.Select(story => story.Key)), Card);
        draft = draft with { Stories = draft.Stories.Select(story => story.Key == key ? story with { Headline = "A\n---\nB\u2028# C\r\nD" } : story).ToList() };

        var (clean, _) = DraftValidator.Validate(draft, Card);
        var markdown = IssueMarkdownWriter.Issue(Week46, DraftLanguage.English, clean, Card);

        clean.For(key).Headline.ShouldBe("A --- B # C D");
        markdown.Split('\n').Count(line => line == "---").ShouldBe(2);
        markdown.ShouldContain("## A --- B # C D\n");
    }

    [Fact]
    public void Email_addresses_and_bare_domains_are_removed()
    {
        var key = Card.Picks.First().Key;
        var draft = new DraftResponse("Yaz: editor@example.com ya da evil.com/x adresine.", "Görüşürüz.", [new DraftStory(key, "Başlık", "Gövde.", "Önemli.")]);

        var (clean, warnings) = DraftValidator.Validate(draft, Card);

        clean.Intro.ShouldBe("Yaz: ya da adresine.");
        warnings.ShouldContain(warning => warning.Where == "intro");
    }

    [Fact]
    public void Names_match_whole_words_and_card_dates_count_as_facts()
    {
        var key = Card.Picks.First().Key;
        var draft = new DraftResponse(
            "Giriş.",
            "Kapanış.",
            [new DraftStory(key, "Başlık", "Haber 7 Kasım'da geldi ve Lloy ile Richard konuştu.", "Önemli.")]);

        var (_, warnings) = DraftValidator.Validate(draft, Card);

        warnings.ShouldContain(warning => warning.Message.Contains("name “Lloy”", StringComparison.Ordinal));
        warnings.ShouldNotContain(warning => warning.Message.Contains("Richard", StringComparison.Ordinal));
        warnings.ShouldNotContain(warning => warning.Message.Contains("number “7”", StringComparison.Ordinal));
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
