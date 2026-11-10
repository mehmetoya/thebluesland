using System.Net;
using System.Text.Json;
using Shouldly;
using TheBluesland.WeeklyNewsletter.Drafting;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;
using static TheBluesland.UnitTests.WeeklyNewsletter.Review.ReviewTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

/// <summary>issue-drafting spec, Design section 3b: the final checker and the auto-merge decision, with fake delegates (no Gemini).</summary>
public sealed class DraftCheckerTests
{
    private static readonly DateOnly SendDate = new(2026, 11, 9);

    private static Task<CheckVerdict> Check(Func<string, CancellationToken, Task<string>> generate) =>
        DraftChecker.RunAsync(generate, Answer(), Card, DraftLanguage.English, SendDate, CancellationToken.None);

    [Fact]
    public async Task RunAsync_passes_only_on_pass_true_with_no_problems()
    {
        var prompts = new List<string>();

        var verdict = await Check((prompt, _) =>
        {
            prompts.Add(prompt);
            return Task.FromResult("{\"pass\":true,\"problems\":[]}");
        });

        verdict.Passed.ShouldBeTrue();
        verdict.Problems.ShouldBeEmpty();
        verdict.Failure.ShouldBeNull();
        prompts.Count.ShouldBe(1);
        prompts[0].ShouldContain("=== FACT CARD ===");
        prompts[0].ShouldContain("=== ISSUE TEXT ===");
        prompts[0].ShouldContain("Story s-");
        prompts[0].ShouldNotContain("http");
        prompts[0].ShouldContain("no person reads it");
    }

    [Theory]
    [InlineData("{\"pass\":false,\"problems\":[\"s-1: invented date\"]}", false)]
    [InlineData("{\"pass\":true,\"problems\":[\"s-1: odd wording\"]}", false)]
    public async Task RunAsync_reports_problems_and_doubts_as_not_passed(string answer, bool passed)
    {
        var verdict = await Check((_, _) => Task.FromResult(answer));

        verdict.Passed.ShouldBe(passed);
        verdict.Failure.ShouldBeNull();
        verdict.Problems.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("{\"problems\":[]}")]
    [InlineData("{\"pass\":true}")]
    public async Task RunAsync_counts_a_malformed_answer_as_not_passed(string answer)
    {
        var verdict = await Check((_, _) => Task.FromResult(answer));

        verdict.Passed.ShouldBeFalse();
        verdict.Failure.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RunAsync_counts_a_failed_request_as_not_passed_and_asks_only_once()
    {
        var calls = 0;

        var verdict = await Check((_, _) =>
        {
            calls++;
            throw new HttpRequestException("Gemini request failed with 429 TooManyRequests", null, HttpStatusCode.TooManyRequests);
        });

        calls.ShouldBe(1);
        verdict.Passed.ShouldBeFalse();
        verdict.Failure!.ShouldContain("429");
    }

    [Fact]
    public async Task RunAsync_lets_caller_cancellation_through()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => DraftChecker.RunAsync(
            (_, token) => Task.FromCanceled<string>(token),
            Answer(),
            Card,
            DraftLanguage.English,
            SendDate,
            cancelled.Token));
    }

    [Fact]
    public void IssueText_shows_the_picks_but_no_backup_and_no_link()
    {
        var text = DraftChecker.IssueText(Answer(), Card, DraftLanguage.English);

        Card.Picks.ShouldAllBe(pick => text.Contains($"Story {pick.Key} - headline:", StringComparison.Ordinal));
        Card.Backups.ShouldAllBe(backup => !text.Contains($"Story {backup.Key} ", StringComparison.Ordinal));
        text.ShouldNotContain("http");
        text.ShouldContain("Subject: Two hooks");
    }

    [Fact]
    public void The_schema_asks_for_pass_and_problems()
    {
        JsonSerializer.Serialize(DraftChecker.ResponseSchema).ShouldContain("\"required\":[\"pass\",\"problems\"]");
    }

    private static LanguageDraft DraftWith(CheckVerdict? check, IReadOnlyList<DraftWarning>? warnings = null, string? fellBackFrom = null) =>
        Draft(DraftLanguage.Turkish, warnings: warnings) with { Check = check, FellBackFrom = fellBackFrom };

    [Fact]
    public void Decide_publishes_only_when_both_languages_passed_without_blocking_warnings()
    {
        var pass = new CheckVerdict(true, []);
        var tr = DraftWith(pass, warnings: [new DraftWarning(Card.Picks.First().Key, "headline: name “Foo” is not in the fact card.")]);
        var en = Draft(DraftLanguage.English) with { Check = pass };

        var decision = PublishDecision.Decide([tr, en], Card);

        decision.AutoMerge.ShouldBeTrue();
        decision.Reasons.ShouldBeEmpty();
        decision.ToJson().ShouldContain("\"autoMerge\": true");
    }

    [Fact]
    public void Decide_blocks_on_a_failed_or_missing_check_a_number_a_tense_error_or_the_fallback_model()
    {
        var pass = new CheckVerdict(true, []);
        var en = Draft(DraftLanguage.English) with { Check = pass };
        var pick = Card.Picks.First().Key;
        var backup = Card.Backups.First().Key;

        PublishDecision.Decide([DraftWith(new CheckVerdict(false, ["s-1: invented"])), en], Card).Reasons.ShouldHaveSingleItem().ShouldStartWith("TR: son kontrol geçmedi");
        PublishDecision.Decide([DraftWith(CheckVerdict.Unavailable("HTTP 429")), en], Card).Reasons.ShouldHaveSingleItem().ShouldContain("yanıt veremedi");
        PublishDecision.Decide([DraftWith(null), en], Card).AutoMerge.ShouldBeFalse();
        PublishDecision.Decide([DraftWith(pass, [new DraftWarning(pick, "body: number “777” is not in the fact card.")]), en], Card).AutoMerge.ShouldBeFalse();
        PublishDecision.Decide([DraftWith(pass, [new DraftWarning(pick, "“November 14” is before the send date (2026-11-16) but the text uses future tense (“will”); write past events in past tense.")]), en], Card).AutoMerge.ShouldBeFalse();
        PublishDecision.Decide([DraftWith(pass, fellBackFrom: "gemini-3.8-flash"), en], Card).AutoMerge.ShouldBeFalse();
        // A backup is never published: its warnings do not block.
        PublishDecision.Decide([DraftWith(pass, [new DraftWarning(backup, "body: number “777” is not in the fact card.")]), en], Card).AutoMerge.ShouldBeTrue();
    }

    [Fact]
    public void Style_warnings_never_block()
    {
        var pass = new CheckVerdict(true, []);
        var pick = Card.Picks.First().Key;
        DraftWarning[] style =
        [
            new(pick, "why it matters repeats the headline/body; add a concrete fact from the card."),
            new(pick, "generic phrase “pioneering”: why it matters should state a concrete consequence from the card."),
            new(pick, "omitted: “GTA” is in the card's headlines for this story but not in the text."),
            new("subject", "subject: 68 characters (limit 60); published as “x”."),
            new(pick, "headline: hype word “iconic” (voice guide: no superlatives)."),
        ];

        PublishDecision.Decide([DraftWith(pass, style), Draft(DraftLanguage.English) with { Check = pass }], Card).AutoMerge.ShouldBeTrue();
    }
}
