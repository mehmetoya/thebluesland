using Shouldly;
using TheBluesland.WeeklyNewsletter.Drafting;
using Xunit;
using Xunit.Abstractions;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

/// <summary>Editorial Room round 2: stale-tense dates, repeated why-it-matters, sample copies, parity noise, backups.</summary>
public sealed class DraftValidatorEditorialTests(ITestOutputHelper output)
{
    private static readonly FactCard Card = W46Card();

    private static string KeyOf(int position) => Card.Picks.ElementAt(position - 1).Key;

    private static (DraftResponse Draft, IReadOnlyList<DraftWarning> Warnings) Story(
        int position, string headline, string body, string why, DateOnly? sendDate = null, string? sampleText = null) =>
        DraftValidator.Validate(
            new DraftResponse("Giriş.", "Kapanış.", [new DraftStory(KeyOf(position), headline, body, why)]),
            Card,
            null,
            sendDate,
            sampleText);

    [Fact]
    public void Ordinal_dates_are_read_as_dates_and_a_card_date_before_the_send_date_may_not_be_written_in_future_tense()
    {
        // The Oasis card says "November 14" (a ceremony before the 16th); the model writes it in future tense without a date.
        var sent = new DateOnly(2026, 11, 16);
        var cardDated = Story(3, "Başlık", "Noel ve Liam Gallagher törene gitmeyecek.", "Önemli.", sent).Warnings;
        var ordinal = Story(3, "Headline", "Oasis will skip the ceremony on November 14th.", "Important.", sent).Warnings;
        var dayFirst = Story(3, "Headline", "Oasis will skip the ceremony on 14th November.", "Important.", sent).Warnings;
        var past = Story(3, "Başlık", "Noel ve Liam Gallagher törene gitmedi.", "Önemli.", sent).Warnings;

        cardDated.ShouldContain(warning => warning.Where == KeyOf(3)
            && warning.Message == "the card dates “November 14” before the send date; write it as already announced/happened, not future tense.");
        ordinal.ShouldContain(warning => warning.Message.Contains("“November 14th” is before the send date (2026-11-16)", StringComparison.Ordinal));
        dayFirst.ShouldContain(warning => warning.Message.Contains("“14th November” is before the send date", StringComparison.Ordinal));
        past.ShouldNotContain(warning => warning.Message.Contains("send date", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2026, 11, 3)]
    [InlineData(2026, 12, 10)]
    public void A_card_date_that_is_not_within_two_weeks_before_the_send_date_is_not_a_stale_event(int year, int month, int day)
    {
        // Nov 3: the ceremony is still ahead. Dec 10: it is 26 days back, too old to read as this issue's event.
        Story(3, "Başlık", "Noel ve Liam Gallagher törene gitmeyecek.", "Önemli.", new DateOnly(year, month, day)).Warnings
            .ShouldNotContain(warning => warning.Message.Contains("send date", StringComparison.Ordinal));
    }

    [Fact]
    public void A_backup_the_model_skipped_gets_one_warning_and_no_other_check()
    {
        var key = Card.Backups.First().Key;
        var draft = new DraftResponse(
            "Giriş.",
            "Kapanış.",
            [new DraftStory(key, "This is the epic setlist [x](https://x.test) and Zzyzx", "Sepultura were 99 old friends.", DraftResponse.MissingBackupNote)]);

        var (clean, warnings) = DraftValidator.Validate(draft, Card);

        warnings.Where(warning => warning.Where == key).Select(warning => warning.Message).ShouldBe(["backup not written by the model; card text shown untranslated."]);
        clean.For(key).Headline.ShouldBe("This is the epic setlist x and Zzyzx");
        clean.For(key).WhyItMatters.ShouldBe(DraftResponse.MissingBackupNote);
    }

    [Fact]
    public void A_skipped_backup_does_not_cause_parity_warnings()
    {
        var key = Card.Backups.First().Key;
        var skipped = new DraftStory(key, "Card headline", "Card summary 99.", DraftResponse.MissingBackupNote);
        var written = new DraftStory(key, "Sepultura bitirdi", "Sepultura son konserini verdi.", "Önemli.");
        LanguageDraft Draft(DraftLanguage language, DraftStory story) =>
            new(language, string.Empty, string.Empty, new DraftResponse("Giriş.", "Kapanış.", [story]), [], "m");

        var result = DraftValidator.AddParity([Draft(DraftLanguage.Turkish, skipped), Draft(DraftLanguage.English, written)], Card);

        result.ShouldAllBe(draft => draft.Warnings.Count == 0);
    }

    [Theory]
    [InlineData("Richard Lloyd 75 yaşında öldü.", "Richard Lloyd 75 yaşında öldü.", "Richard Lloyd öldü")]
    [InlineData("Television guitarist Richard Lloyd died at 75.", "Richard Lloyd, the Television guitarist, died at 75.", "Richard Lloyd dies")]
    public void A_why_it_matters_that_repeats_the_headline_and_body_is_flagged(string body, string why, string headline)
    {
        var repeated = Story(1, headline, body, why).Warnings;
        var fresh = Story(1, headline, body, "Geese daha önce de grubun şarkılarını çalmıştı; kayıp müzik dünyasında geniş yankı buldu.").Warnings;

        repeated.ShouldContain(warning => warning.Where == KeyOf(1) && warning.Message == "why it matters repeats the headline/body; add a concrete fact from the card.");
        fresh.ShouldNotContain(warning => warning.Message.StartsWith("why it matters repeats", StringComparison.Ordinal));
    }

    [Fact]
    public void Copying_the_voice_guides_sample_issue_is_flagged()
    {
        var sample = DraftPromptBuilder.SampleText(VoiceGuide());
        sample.ShouldContain("Tessaly Brand'in 68 yaşında öldüğünü");
        sample.ShouldNotContain("Örnekteki hiçbir ifade");
        const string Copied = "Grup, basçısı Tessaly Brand'in 68 yaşında öldüğünü bir açıklamayla duyurdu.";

        var withSample = Story(1, "Başlık", Copied, "Önemli.", sampleText: sample).Warnings;
        var withoutSample = Story(1, "Başlık", Copied, "Önemli.").Warnings;

        withSample.ToList().ForEach(warning => output.WriteLine($"{warning.Where}: {warning.Message}"));
        withSample.ShouldContain(warning => warning.Where == KeyOf(1)
            && warning.Message.StartsWith("body: copies the voice guide's sample issue (“grup basc", StringComparison.Ordinal)
            && warning.Message.EndsWith("…”).", StringComparison.Ordinal));
        withoutSample.ShouldNotContain(warning => warning.Message.Contains("sample issue", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Birçok müzisyen bunu kullandı.", "birçok müzisyen")]
    [InlineData("Pek çok müzisyen etkilendi.", "pek çok müzisyen")]
    [InlineData("YouTube verileri listelerde yeniden yer alacak.", "yeniden yer alacak")]
    [InlineData("Many musicians used it.", "many musicians")]
    [InlineData("It was a favorite of Frank Zappa.", "a favorite of")]
    public void More_stock_why_it_matters_phrases_are_flagged(string why, string phrase)
    {
        Story(1, "Başlık", "Gövde.", why).Warnings
            .ShouldContain(warning => warning.Where == KeyOf(1) && warning.Message.Contains($"generic phrase “{phrase}”", StringComparison.Ordinal));
    }

    [Fact]
    public void An_intro_that_names_more_than_three_stories_is_flagged()
    {
        var picks = Card.Picks.Select(pick => new DraftStory(
            pick.Key,
            pick.Position switch
            {
                1 => "Richard Lloyd öldü",
                2 => "YouTube listelere dönüyor",
                3 => "Oasis törene gitmiyor",
                4 => "GTA VI festivali",
                _ => "The Strokes SNL'de",
            },
            "Gövde.",
            "Önemli.")).ToList();

        DraftResponse Intro(string text) => new(text, "Kapanış.", picks);

        var many = DraftValidator.Validate(Intro("Richard Lloyd, YouTube, Oasis ve GTA konuşuldu. Beş haber, beş dakika."), Card, DraftLanguage.Turkish).Warnings;
        var few = DraftValidator.Validate(Intro("Lloyd'a veda ettik, YouTube döndü. Beş haber, beş dakika."), Card, DraftLanguage.Turkish).Warnings;

        many.ShouldContain(warning => warning.Where == "intro" && warning.Message == "intro names more than three stories.");
        few.ShouldNotContain(warning => warning.Message == "intro names more than three stories.");
    }

    [Fact]
    public void An_over_long_subject_warns_with_the_form_that_gets_published()
    {
        var subject = "Richard Lloyd dies, YouTube returns to the charts and more" + DraftLanguage.SubjectTag;

        var (_, warnings) = DraftValidator.Validate(
            new DraftResponse("Five stories, five minutes.", "Bye.", [], subject, "A preview."),
            Card,
            DraftLanguage.English);

        warnings.ToList().ForEach(warning => output.WriteLine($"{warning.Where}: {warning.Message}"));
        warnings.ShouldContain(warning => warning.Where == "subject"
            && warning.Message == $"subject: {subject.Length} characters (limit 60); published as “Richard Lloyd dies · TBL Weekly”.");
    }

    [Fact]
    public void A_headline_equal_to_a_source_headline_is_one_note_while_body_copies_stay_warnings()
    {
        var key = KeyOf(1);
        const string Title = "Richard Lloyd Television Guitarist Dies at 75 After a Long Illness Friends Said";
        var card = Card with
        {
            Stories = [.. Card.Stories.Select(story => story.Key == key
                ? story with { Articles = [.. story.Articles, new FactArticle("Louder", DateTimeOffset.UnixEpoch, Title, $"{Title} Paolo Ragusa")] }
                : story)],
        };
        var draft = new DraftResponse("Giriş.", "Kapanış.", [new DraftStory(key, Title, $"{Title}.", "Önemli.")]);

        var (_, warnings) = DraftValidator.Validate(draft, card);

        warnings.ShouldContain(warning => warning.Where == key && warning.Message == "headline matches Louder's headline.");
        warnings.ShouldNotContain(warning => warning.Where == key && warning.Message.StartsWith("headline: copies a summary", StringComparison.Ordinal));
        warnings.ShouldContain(warning => warning.Where == key && warning.Message.StartsWith("body: copies a summary", StringComparison.Ordinal));
    }

    [Fact]
    public void Parity_lists_every_missing_item_of_a_story_in_one_warning_and_skips_particles_and_title_case_words()
    {
        var key = Card.Picks.First().Key;
        DraftResponse Text(string headline, string body) =>
            new("Giriş.", "Kapanış.", [new DraftStory(key, headline, body, "Önemli.")]);
        LanguageDraft Draft(DraftLanguage language, DraftResponse text) => new(language, string.Empty, string.Empty, text, [], "m");

        var turkish = Draft(DraftLanguage.Turkish, Text("Richard Lloyd öldü", "Grup öldü."));
        var english = Draft(DraftLanguage.English, Text("Richard Lloyd Dies at 75", "The Killers and St. Vincent played Cameron Winter's set in Los Angeles."));

        var parity = DraftValidator.AddParity([turkish, english], Card)[0].Warnings.Where(warning => warning.Message.Contains("parity", StringComparison.Ordinal)).ToList();

        parity.ForEach(warning => output.WriteLine(warning.Message));
        parity.ShouldHaveSingleItem().Message.ShouldBe("TR/EN parity: “75”, “Killers”, “Cameron Winter's”, “Los Angeles” are in the EN story but not in the TR story.");
    }

    /// <summary>
    /// Regression fixture: the real 2026-W47 draft of the lighter fallback model. Before this round it
    /// produced 50 TR + 9 EN warnings, mostly parity noise from Title Case headline words.
    /// </summary>
    [Fact]
    public void The_fallback_models_real_draft_gets_far_fewer_parity_warnings_and_keeps_the_true_omissions()
    {
        var card = W47Card();
        var sample = DraftPromptBuilder.SampleText(VoiceGuide());
        var drafts = DraftLanguage.All.Select(language =>
        {
            var (clean, warnings) = DraftValidator.Validate(FallbackDraft(language.Code), card, language, new DateOnly(2026, 11, 16), sample);
            return new LanguageDraft(language, string.Empty, string.Empty, clean, warnings, "gemini-flash-lite-latest");
        }).ToList();

        drafts = DraftValidator.AddParity(drafts, card);

        foreach (var draft in drafts)
        {
            draft.Warnings.ToList().ForEach(warning => output.WriteLine($"{draft.Language.Code} {warning.Where}: {warning.Message}"));
        }

        var turkish = drafts[0].Warnings;
        turkish.Count.ShouldBeLessThan(25);
        turkish.Count(warning => warning.Message.Contains("parity", StringComparison.Ordinal)).ShouldBeLessThanOrEqualTo(4);
        turkish.Count(warning => warning.Message.Contains("“VetsAid”", StringComparison.Ordinal)).ShouldBe(1);
        turkish.ShouldContain(warning => warning.Where == "s-41" && warning.Message.StartsWith("omitted: “VetsAid”", StringComparison.Ordinal));
        turkish.ShouldContain(warning => warning.Message.Contains("parity", StringComparison.Ordinal) && warning.Message.Contains("“Ain't That Nothin”", StringComparison.Ordinal));
        turkish.ShouldContain(warning => warning.Message.Contains("parity", StringComparison.Ordinal) && warning.Message.Contains("“Los Angeles”", StringComparison.Ordinal));
        turkish.Where(warning => warning.Message.StartsWith("backup not written", StringComparison.Ordinal)).Select(warning => warning.Where).ShouldBe(["s-77", "s-15", "s-42"]);
        turkish.ShouldContain(warning => warning.Where == "s-46" && warning.Message.StartsWith("the card dates “November 14”", StringComparison.Ordinal));
        turkish.ShouldContain(warning => warning.Where == "intro" && warning.Message == "intro names more than three stories.");
        drafts[1].Warnings.Count.ShouldBeLessThanOrEqualTo(12);
    }
}
