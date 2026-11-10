using System.Net;
using Shouldly;
using TheBluesland.WeeklyNewsletter.Drafting;
using TheBluesland.WeeklyNewsletter.Ingest;
using TheBluesland.WeeklyNewsletter.Review;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

/// <summary>Editorial Room round 2: prompt rules, lite prompt, subject fitting, date conflicts, ingest cleanup.</summary>
public sealed class EditorialRound2Tests
{
    private static readonly FactCard Card = W46Card();
    private static readonly DateOnly Sent = new(2026, 11, 16);

    [Fact]
    public void The_full_prompt_carries_the_time_fact_role_and_why_it_matters_rules()
    {
        var prompt = DraftPromptBuilder.Build(DraftLanguage.English, VoiceGuide(), Card, Sent);

        prompt.ShouldContain("will be over when");
        prompt.ShouldContain("katılmayacağını açıkladı");
        prompt.ShouldContain("Something that has not started yet is not past");
        prompt.ShouldContain("who (with identity: band, role, \"two surviving");
        prompt.ShouldContain("\"co-invented\" stays \"co-inventor\" / \"mucitlerinden\"");
        prompt.ShouldContain("If two articles in the card disagree on a date or number, leave that detail out.");
        prompt.ShouldContain("must add one fact that the headline and body do not already contain");
        prompt.ShouldContain("Write every backup too; a missing backup is an error.");
        prompt.ShouldContain("If the subject runs over, drop the second hook; never abbreviate.");
        prompt.ShouldContain("headlines may use the short");
        prompt.ShouldContain("Örnekteki hiçbir ifade, cümle parçası ya da olgu taslağa taşınmaz.");
        prompt.ShouldContain("kurgusal örnek");
    }

    [Fact]
    public void The_lite_prompt_drops_the_sample_and_puts_a_numbered_checklist_after_the_fact_card()
    {
        var guide = VoiceGuide();
        var lite = DraftPromptBuilder.Build(DraftLanguage.Turkish, guide, Card, Sent, lite: true);

        lite.ShouldNotContain("Tessaly Brand");
        lite.ShouldNotContain("Orchard Kids");
        lite.ShouldContain("## Türkçe");
        lite.ShouldContain("## English");
        lite.ShouldContain("## Kim konuşuyor");
        lite.ShouldContain("Write the issue in Turkish");
        lite.IndexOf("=== END FACT CARD ===", StringComparison.Ordinal).ShouldBeLessThan(lite.IndexOf("Check every item before you answer:", StringComparison.Ordinal));
        lite.ShouldContain("\n1. Use only facts");
        lite.ShouldContain("The issue is sent on 2026-11-16");
        lite.ShouldContain("Beş haber, beş dakika.");
        lite.ShouldContain("Story key: ");
        lite.ShouldNotContain("http");
        lite.Length.ShouldBeLessThan(DraftPromptBuilder.Build(DraftLanguage.Turkish, guide, Card, Sent).Length);
        DraftPromptBuilder.Build(DraftLanguage.Turkish, "no sample section", Card, Sent, lite: true).ShouldContain("no sample section");
    }

    [Fact]
    public async Task The_fallback_temperature_is_lower_than_the_default()
    {
        async Task<string> Body(double temperature)
        {
            var handler = new QueueHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(GeminiEnvelope("{}")) });
            await new GeminiClient(new HttpClient(handler), "k", "m", TimeSpan.Zero, temperature).GenerateJsonAsync("p", new { }, CancellationToken.None);
            return handler.Requests.Single().Body;
        }

        (await Body(GeminiClient.DefaultTemperature)).ShouldContain("\"temperature\":0.4");
        (await Body(GeminiClient.FallbackTemperature)).ShouldContain("\"temperature\":0.2");
    }

    [Fact]
    public void A_subject_that_runs_over_drops_the_second_hook_instead_of_ending_in_an_ellipsis()
    {
        var english = IssueMarkdownWriter.FitSubject("Tessaly Brand dies, Velvet Harbour skip the prize ceremony" + DraftLanguage.SubjectTag);
        var turkish = IssueMarkdownWriter.FitSubject("Tessaly Brand öldü ve Velvet Harbour ödül gecesine gitmedi" + DraftLanguage.SubjectTag);
        var fits = IssueMarkdownWriter.FitSubject("Tessaly Brand dies, Maren Sloane goes solo" + DraftLanguage.SubjectTag);
        var noSeparator = IssueMarkdownWriter.FitSubject("Tessalybrandwentquietlyintothatgoodnightaftermanyyears" + DraftLanguage.SubjectTag);

        english.ShouldBe("Tessaly Brand dies" + DraftLanguage.SubjectTag);
        turkish.ShouldBe("Tessaly Brand öldü" + DraftLanguage.SubjectTag);
        fits.ShouldBe("Tessaly Brand dies, Maren Sloane goes solo" + DraftLanguage.SubjectTag);
        noSeparator.Length.ShouldBeLessThanOrEqualTo(DraftLanguage.MaxSubject);
        noSeparator.ShouldEndWith(DraftLanguage.SubjectTag);
        new[] { english, turkish }.ShouldAllBe(subject => !subject.Contains('…', StringComparison.Ordinal));
    }

    [Fact]
    public void Summaries_of_one_story_that_state_different_dates_are_listed_for_the_editor()
    {
        var story = new FactStory(
            "s-1",
            1,
            false,
            "news",
            "test",
            [
                new FactArticle("Stereogum", DateTimeOffset.UnixEpoch, "Band sets tour", "The tour starts Jan 27 in Leeds."),
                new FactArticle("Ultimate Classic Rock", DateTimeOffset.UnixEpoch, "Band sets tour", "Opening night is January 29th."),
                new FactArticle("Consequence", DateTimeOffset.UnixEpoch, "Band sets tour", "The first show is on Jan. 29."),
            ],
            []);
        var agreeing = story with { Articles = [story.Articles[1], story.Articles[2]] };

        SourceDateConflicts.Describe(story).ShouldBe("Jan 27 (Stereogum) / Jan 29 (Ultimate Classic Rock, Consequence)");
        SourceDateConflicts.Describe(agreeing).ShouldBeNull();

        var card = new FactCard([story]);
        var body = PullRequestBodyFormatter.ToMarkdown(Week46, card, [Review.ReviewTestData.Draft(DraftLanguage.Turkish, Review.ReviewTestData.Answer()), Review.ReviewTestData.Draft(DraftLanguage.English, Review.ReviewTestData.Answer())], "## x\n\n| a |\n");
        body.ShouldContain("dates differ across sources: Jan 27 (Stereogum) / Jan 29 (Ultimate Classic Rock, Consequence)");
    }

    [Fact]
    public void An_ingested_summary_loses_a_leading_headline_repeat_and_a_trailing_headline_with_the_authors_name()
    {
        const string Title = "New Order Announce 2027 US Tour Dates";
        var xml = $"""
            <rss version="2.0"><channel><title>t</title><link>https://x.example.com</link><description>d</description>
            <item><title>{Title}</title><link>https://x.example.com/a</link><description>The band's first US tour since 2023 will feature support from Future Islands. {Title} Paolo Ragusa</description></item>
            <item><title>{Title}</title><link>https://x.example.com/b</link><description>{Title}: The band's first US tour since 2023.</description></item>
            <item><title>{Title}</title><link>https://x.example.com/c</link><description>{Title}</description></item>
            <item><title>Short</title><link>https://x.example.com/d</link><description>Short notes. Short</description></item>
            </channel></rss>
            """;

        var items = RssFeedClient.Parse("t", new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)));

        items[0].Summary.ShouldBe("The band's first US tour since 2023 will feature support from Future Islands.");
        items[1].Summary.ShouldBe("The band's first US tour since 2023.");
        items[2].Summary.ShouldBeNull();
        items[3].Summary.ShouldBe("Short notes. Short");
    }
}
