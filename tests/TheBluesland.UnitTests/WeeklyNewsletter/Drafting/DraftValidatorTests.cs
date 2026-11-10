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

        var (clean, warnings) = DraftValidator.Validate(
            sample,
            Card,
            language == "English" ? DraftLanguage.English : DraftLanguage.Turkish,
            Week46.SendDate);

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

    [Theory]
    [InlineData("Efsane ikili yine sahnede.", "efsane")]
    [InlineData("İnanılmaz bir dönüş.", "inanılmaz")]
    [InlineData("Grup tarihi bir konser verdi.", "tarihi bir")]
    [InlineData("A legendary duo, back on stage.", "legendary")]
    [InlineData("An Iconic return.", "iconic")]
    public void Hype_words_are_flagged(string body, string word)
    {
        var key = Card.Picks.First().Key;
        var draft = new DraftResponse("Bu hafta beş haber var.", "Haftaya görüşürüz.", [new DraftStory(key, "Başlık", body, "Önemli.")]);

        var (_, warnings) = DraftValidator.Validate(draft, Card);

        warnings.ShouldContain(warning => warning.Where == key && warning.Message.Contains($"hype word “{word}”", StringComparison.Ordinal));
    }

    [Fact]
    public void A_release_date_is_not_hype()
    {
        var key = Card.Picks.First().Key;
        var draft = new DraftResponse("Bu hafta beş haber var.", "Haftaya görüşürüz.", [new DraftStory(key, "Başlık", "Albümün çıkış tarihi belli oldu. A history of the band.", "Önemli.")]);

        DraftValidator.Validate(draft, Card).Warnings.ShouldNotContain(warning => warning.Message.Contains("hype", StringComparison.Ordinal));
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

    private static string KeyOf(int position) => Card.Picks.ElementAt(position - 1).Key;

    private static (DraftResponse Draft, IReadOnlyList<DraftWarning> Warnings) Story(
        int position, string headline, string body, string why, DraftLanguage? language = null, DateOnly? sendDate = null) =>
        DraftValidator.Validate(
            new DraftResponse("Giriş.", "Kapanış.", [new DraftStory(KeyOf(position), headline, body, why)]),
            Card,
            language,
            sendDate);

    [Fact]
    public void An_outro_sentence_about_ai_is_removed_because_the_code_adds_the_note()
    {
        var draft = new DraftResponse(
            "Giriş.",
            "See you next Monday. This issue was prepared with AI assistance and edited by TheBluesland's editor.",
            []);

        var (clean, warnings) = DraftValidator.Validate(draft, Card, DraftLanguage.English);

        clean.Outro.ShouldBe("See you next Monday.");
        warnings.ShouldContain(warning => warning.Where == "outro" && warning.Message.Contains("removed a sentence about AI", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("tr", "Bu sayı yapay zekâ yardımıyla yazıldı; TheBluesland editörü seçti ve onayladı.")]
    [InlineData("en", "Written with AI assistance; selected and approved by TheBluesland's editor.")]
    public void An_outro_that_is_only_the_disclosure_becomes_the_languages_standard_outro(string code, string note)
    {
        var language = DraftLanguage.All.Single(candidate => candidate.Code == code);

        var (clean, _) = DraftValidator.Validate(new DraftResponse("Giriş.", note, []), Card, language);

        clean.Outro.ShouldBe(language.FallbackOutro);
    }

    [Fact]
    public void A_number_from_another_story_is_not_a_fact_of_this_story()
    {
        // "21" is the GTA festival's date; the Lloyd story's own card never says it.
        var (_, warnings) = Story(1, "Başlık", "Lloyd 21 yaşında kaydetti.", "Önemli.");

        warnings.ShouldContain(warning => warning.Where == KeyOf(1) && warning.Message.Contains("number “21”", StringComparison.Ordinal));
        Story(4, "Başlık", "Festival 21 Kasım'da.", "Önemli.").Warnings.ShouldNotContain(warning => warning.Message.Contains("number “21”", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_first_word_of_a_field_is_exempt_from_the_name_check()
    {
        var (_, warnings) = Story(1, "Başlık", "Marquee çıktı. Marquee'nin sesi yaşıyor. Grup devam etti.", "Önemli.");

        warnings.Count(warning => warning.Message.Contains("name “Marquee”", StringComparison.Ordinal)).ShouldBe(1);
        warnings.ShouldNotContain(warning => warning.Message.Contains("“Grup”", StringComparison.Ordinal));
    }

    [Fact]
    public void Numbers_and_names_from_the_stories_headlines_that_the_text_leaves_out_are_flagged()
    {
        var (_, lloyd) = Story(1, "Gitarist Richard Lloyd öldü", "Television'ın kurucusu hayatını kaybetti.", "Önemli.");
        var (_, strokes) = Story(5, "The Strokes sahnede", "Grup bir şarkı çaldı.", "Önemli.");
        var (_, complete) = Story(1, "Lloyd öldü", "Television'ın kurucusu Richard Lloyd 75 yaşında öldü.", "Önemli.");

        lloyd.ShouldContain(warning => warning.Where == KeyOf(1) && warning.Message == "omitted: number “75” is in the card's headlines for this story but not in the text.");
        strokes.ShouldContain(warning => warning.Where == KeyOf(5) && warning.Message.Contains("“SNL”", StringComparison.Ordinal));
        complete.ShouldNotContain(warning => warning.Message.StartsWith("omitted", StringComparison.Ordinal));
    }

    [Fact]
    public void A_multi_word_name_is_covered_by_its_initials()
    {
        // Two of the three headlines say "Grand Theft Auto VI" or "GTA VI".
        var (_, withoutNumeral) = Story(4, "GTA festivali", "GTA için Miami'de festival var.", "Önemli.");
        var (_, complete) = Story(4, "GTA VI festivali", "GTA VI için Miami'de festival var.", "Önemli.");

        withoutNumeral.ShouldContain(warning => warning.Message.StartsWith("omitted", StringComparison.Ordinal) && warning.Message.Contains("“VI”", StringComparison.Ordinal));
        withoutNumeral.ShouldNotContain(warning => warning.Message.StartsWith("omitted", StringComparison.Ordinal)
            && (warning.Message.Contains("“Grand”", StringComparison.Ordinal) || warning.Message.Contains("“Auto”", StringComparison.Ordinal)));
        complete.ShouldNotContain(warning => warning.Message.StartsWith("omitted", StringComparison.Ordinal) && warning.Message.Contains("“VI”", StringComparison.Ordinal));
    }

    [Fact]
    public void Facts_in_only_one_language_are_flagged_as_parity_warnings_on_the_other()
    {
        var key = KeyOf(1);
        DraftResponse Text(string body)
        {
            var answer = Review.ReviewTestData.Answer();
            return answer with { Stories = [.. answer.Stories.Select(story => story.Key == key ? story with { Body = body } : story)] };
        }

        var turkish = Review.ReviewTestData.Draft(DraftLanguage.Turkish, Text("Richard Lloyd 75 yaşında öldü. Geese saygı duruşu yaptı. Cameron Winter da vardı."));
        var english = Review.ReviewTestData.Draft(DraftLanguage.English, Text("Richard Lloyd has died. Geese paid tribute."));

        var result = DraftValidator.AddParity([turkish, english], Card);

        result[0].Warnings.Where(warning => warning.Message.Contains("parity", StringComparison.Ordinal)).Select(warning => warning.Message).ToList().ShouldBeEmpty();
        var parity = result[1].Warnings.Where(warning => warning.Message.Contains("parity", StringComparison.Ordinal)).Select(warning => warning.Message).ToList();
        parity.ShouldContain("EN/TR parity: “75” is in the TR story but not in the EN story.");
        parity.ShouldContain("EN/TR parity: “Winter” is in the TR story but not in the EN story.");
        parity.ShouldNotContain(message => message.Contains("Geese", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, "Lloyd aylar önce, bir yıl önce söylemişti. Pazartesi günü konuştu.", "bir yıl")]
    [InlineData(1, "Lloyd a year ago said it. He spoke on Monday.", "a year")]
    [InlineData(1, "The band spoke on Monday.", "Monday")]
    [InlineData(1, "Grup geçen hafta çaldı.", "geçen hafta")]
    [InlineData(1, "The band has toured for decades.", "decades")]
    public void Time_wording_the_card_does_not_state_is_flagged(int position, string body, string wording)
    {
        var (_, warnings) = Story(position, "Başlık", body, "Önemli.");

        warnings.ShouldContain(warning => warning.Where == KeyOf(position) && warning.Message.Contains($"time wording “{wording}”", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_weekday_the_card_states_is_allowed_in_either_language()
    {
        // The Billboard story's card says "Monday (November 9)".
        Story(2, "Başlık", "Billboard pazartesi açıkladı.", "Önemli.").Warnings.ShouldNotContain(warning => warning.Message.StartsWith("time wording", StringComparison.Ordinal));
        Story(2, "Başlık", "Billboard announced it on Monday.", "Önemli.").Warnings.ShouldNotContain(warning => warning.Message.StartsWith("time wording", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Television öncü bir grup olarak hâlâ bu mirasla anılıyor.", "öncü bir grup")]
    [InlineData("Bu, geniş kitle için önemli.", "geniş kitle")]
    [InlineData("A pioneering band, proving its reach well beyond fans.", "pioneering")]
    [InlineData("The coveted slot.", "coveted")]
    public void Stock_why_it_matters_phrases_are_flagged(string why, string phrase)
    {
        var (_, warnings) = Story(1, "Başlık", "Gövde.", why);

        warnings.ShouldContain(warning => warning.Where == KeyOf(1) && warning.Message.Contains($"generic phrase “{phrase}”", StringComparison.Ordinal));
    }

    [Fact]
    public void An_event_before_the_send_date_written_in_future_tense_is_flagged()
    {
        var sent = new DateOnly(2026, 11, 16);
        var turkish = Story(3, "Başlık", "Oasis, 14 Kasım'daki törene katılmayacak.", "Önemli.", sendDate: sent).Warnings;
        var english = Story(3, "Headline", "Oasis will skip the ceremony on November 14.", "Important.", sendDate: sent).Warnings;
        var past = Story(3, "Başlık", "Oasis, 14 Kasım'daki törene katılmadı.", "Önemli.", sendDate: sent).Warnings;
        var upcoming = Story(3, "Başlık", "Oasis, 14 Kasım'daki törene katılmayacak.", "Önemli.", sendDate: new DateOnly(2026, 11, 9)).Warnings;

        turkish.ShouldContain(warning => warning.Where == KeyOf(3) && warning.Message.Contains("future tense", StringComparison.Ordinal));
        english.ShouldContain(warning => warning.Message.Contains("“November 14” is before the send date (2026-11-16)", StringComparison.Ordinal));
        past.ShouldNotContain(warning => warning.Message.Contains("future tense", StringComparison.Ordinal));
        upcoming.ShouldNotContain(warning => warning.Message.Contains("future tense", StringComparison.Ordinal));
    }

    [Fact]
    public void This_week_is_flagged_because_the_issue_covers_the_previous_week()
    {
        var (_, warnings) = DraftValidator.Validate(new DraftResponse("Bu hafta beş haber. This week too.", "Kapanış.", []), Card);

        warnings.Count(warning => warning.Where == "intro" && warning.Message.Contains("previous week", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public void Subject_and_preview_are_checked_for_length_brand_tag_and_repetition()
    {
        var (clean, warnings) = DraftValidator.Validate(
            new DraftResponse("Five stories, five minutes.", "Bye.", [], "Richard Lloyd dies and a very long second hook that goes on and on", "Richard Lloyd dies and a very long second hook that goes on and on."),
            Card,
            DraftLanguage.English);

        clean.Subject.ShouldNotBeNull();
        warnings.ShouldContain(warning => warning.Where == "subject" && warning.Message.Contains("(limit 60)", StringComparison.Ordinal));
        warnings.ShouldContain(warning => warning.Where == "subject" && warning.Message.Contains("does not end with “ · TBL Weekly”", StringComparison.Ordinal));
        warnings.ShouldContain(warning => warning.Where == "preview" && warning.Message == "preview: repeats the subject.");
    }

    [Fact]
    public void A_missing_subject_or_intro_tagline_is_flagged()
    {
        var (_, warnings) = DraftValidator.Validate(new DraftResponse("Giriş.", "Kapanış.", []), Card, DraftLanguage.Turkish);

        warnings.ShouldContain(warning => warning.Where == "subject" && warning.Message.Contains("missing", StringComparison.Ordinal));
        warnings.ShouldContain(warning => warning.Where == "intro" && warning.Message.Contains("Beş haber, beş dakika.", StringComparison.Ordinal));
    }
}
