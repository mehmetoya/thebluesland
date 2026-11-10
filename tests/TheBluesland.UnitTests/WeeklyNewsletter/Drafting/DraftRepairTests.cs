using System.Net;
using Shouldly;
using TheBluesland.WeeklyNewsletter.Drafting;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

/// <summary>issue-drafting spec, Design section 3a: the self-repair pass, with a fake generate delegate (no Gemini).</summary>
public sealed class DraftRepairTests
{
    private static readonly FactCard Card = W46Card();
    private static readonly DateOnly SendDate = new(2026, 11, 9);
    private static readonly string[] Keys = [.. Card.Stories.Select(story => story.Key)];

    private static DraftResponse Answer(string why = "It matters.", string subject = "Two hooks · TBL Weekly") =>
        DraftResponse.Parse(FakeAnswer(Keys).Replace("It matters.", why, StringComparison.Ordinal), Card) with { Subject = subject };

    private static (DraftResponse Text, IReadOnlyList<DraftWarning> Warnings) Validated(DraftResponse answer) =>
        DraftValidator.Validate(answer, Card, DraftLanguage.English, SendDate);

    [Fact]
    public async Task RunAsync_accepts_the_repaired_answer_when_it_has_fewer_warnings()
    {
        var (text, warnings) = Validated(Answer(why: "It is pioneering."));
        warnings.Any(DraftRepair.IsFixable).ShouldBeTrue();
        var calls = new List<string>();

        var (repaired, finalWarnings, report) = await Repair(text, warnings, calls, FakeAnswer(Keys));

        calls.Count.ShouldBe(1);
        report.Status.ShouldBe(RepairStatus.Accepted);
        report.Before.ShouldBe(warnings.Count);
        report.After.ShouldBeLessThan(report.Before);
        finalWarnings.Count.ShouldBe(report.After);
        repaired.Stories.ShouldAllBe(story => story.WhyItMatters == "It matters.");
        calls[0].ShouldContain("PROBLEMS:");
        calls[0].ShouldContain("generic phrase");
        calls[0].ShouldContain("\"whyItMatters\":\"It is pioneering.\"");
        calls[0].ShouldContain("=== FACT CARD ===");
        calls[0].ShouldNotContain("http");
    }

    [Fact]
    public async Task RunAsync_keeps_the_original_when_the_repair_has_no_fewer_warnings()
    {
        var (text, warnings) = Validated(Answer(why: "It is pioneering."));
        var calls = new List<string>();

        // Same text again: a tie keeps the original.
        var (kept, keptWarnings, report) = await Repair(text, warnings, calls, FakeAnswer(Keys).Replace("It matters.", "It is pioneering.", StringComparison.Ordinal));

        report.Status.ShouldBe(RepairStatus.Rejected);
        report.After.ShouldBe(report.Before);
        kept.ShouldBeSameAs(text);
        keptWarnings.ShouldBeSameAs(warnings);

        // Worse text: also rejected.
        var worse = await Repair(text, warnings, calls, FakeAnswer(Keys).Replace("It matters.", "It is pioneering, proving a coveted point.", StringComparison.Ordinal));
        worse.Report.Status.ShouldBe(RepairStatus.Rejected);
        worse.Report.After.ShouldBeGreaterThanOrEqualTo(worse.Report.Before);
        worse.Text.ShouldBeSameAs(text);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("{\"not\":\"a draft\"}")]
    public async Task RunAsync_keeps_the_original_when_the_repaired_answer_is_malformed(string answer)
    {
        var (text, warnings) = Validated(Answer(why: "It is pioneering."));
        var calls = new List<string>();

        var result = await Repair(text, warnings, calls, answer);

        result.Report.Status.ShouldBe(RepairStatus.Failed);
        result.Report.Failure.ShouldNotBeNullOrWhiteSpace();
        result.Text.ShouldBeSameAs(text);
        result.Warnings.ShouldBeSameAs(warnings);
        calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_keeps_the_original_when_the_request_fails_and_never_retries_it_itself()
    {
        var (text, warnings) = Validated(Answer(why: "It is pioneering."));
        var calls = 0;

        var result = await DraftRepair.RunAsync(
            (_, _) =>
            {
                calls++;
                throw new HttpRequestException("Gemini request failed with 429 TooManyRequests", null, HttpStatusCode.TooManyRequests);
            },
            text,
            warnings,
            Card,
            DraftLanguage.English,
            SendDate,
            string.Empty,
            CancellationToken.None);

        calls.ShouldBe(1);
        result.Report.Status.ShouldBe(RepairStatus.Failed);
        result.Report.Failure!.ShouldContain("429");
        result.Text.ShouldBeSameAs(text);
    }

    [Fact]
    public async Task RunAsync_makes_no_request_when_no_warning_is_fixable()
    {
        var (text, warnings) = Validated(Answer());
        warnings.Any(DraftRepair.IsFixable).ShouldBeFalse();
        var calls = new List<string>();

        var result = await Repair(text, warnings, calls, FakeAnswer(Keys));

        calls.ShouldBeEmpty();
        result.Report.ShouldBe(new RepairReport(RepairStatus.NotNeeded, warnings.Count, warnings.Count));
        result.Text.ShouldBeSameAs(text);
    }

    [Fact]
    public async Task RunAsync_repairs_a_subject_over_the_limit()
    {
        var longSubject = "Richard Lloyd died, YouTube returns to Billboard charts · TBL Weekly";
        var (text, warnings) = Validated(Answer(subject: longSubject));
        warnings.ShouldContain(warning => warning.Where == "subject" && DraftRepair.IsFixable(warning));
        var calls = new List<string>();

        var result = await Repair(text, warnings, calls, FakeAnswer(Keys));

        result.Report.Status.ShouldBe(RepairStatus.Accepted);
        result.Text.Subject.ShouldBe("Two hooks · TBL Weekly");
        result.Text.Subject!.Length.ShouldBeLessThanOrEqualTo(DraftLanguage.MaxSubject);
        calls.Count.ShouldBe(1);
        calls[0].ShouldContain("subject");
        calls[0].ShouldContain($"at most {DraftLanguage.MaxSubject - DraftLanguage.SubjectTag.Length} characters");
    }

    [Fact]
    public async Task RunAsync_lets_caller_cancellation_through()
    {
        var (text, warnings) = Validated(Answer(why: "It is pioneering."));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => DraftRepair.RunAsync(
            (_, token) => Task.FromCanceled<string>(token),
            text,
            warnings,
            Card,
            DraftLanguage.English,
            SendDate,
            string.Empty,
            cancelled.Token));
    }

    [Fact]
    public void RepairLine_names_the_outcome_for_the_pr_body()
    {
        DraftReportFormatter.RepairLine(new RepairReport(RepairStatus.Accepted, 7, 3), turkish: true).ShouldBe("Otomatik düzeltme: 7 uyarı → 3 uyarı\n\n");
        DraftReportFormatter.RepairLine(new RepairReport(RepairStatus.NotNeeded, 0, 0), turkish: true).ShouldBe("Otomatik düzeltme: gerekmedi\n\n");
        DraftReportFormatter.RepairLine(new RepairReport(RepairStatus.Rejected, 4, 4), turkish: true).ShouldContain("ilk taslak korundu");
        DraftReportFormatter.RepairLine(new RepairReport(RepairStatus.Failed, 4, 4, "quota"), turkish: true).ShouldContain("quota");
        DraftReportFormatter.RepairLine(null, turkish: true).ShouldBeEmpty();
    }

    [Fact]
    public void BuildRepair_lists_only_the_given_warnings_and_stays_inside_the_data_boundary()
    {
        var prompt = DraftPromptBuilder.BuildRepair(DraftLanguage.Turkish, Card, SendDate, "{\"intro\":\"x\"}", [new DraftWarning("s-1", "generic phrase “pioneering”: why it matters should state a concrete consequence from the card.")]);

        prompt.ShouldContain("written in Turkish");
        prompt.ShouldContain("- [s-1] generic phrase “pioneering”");
        prompt.ShouldContain("{\"intro\":\"x\"}");
        prompt.ShouldContain("NOT in the headline or body");
        prompt.ShouldNotContain("http");
        prompt.ShouldNotContain("Tessaly Brand");
    }

    [Fact]
    public void Prompts_ask_for_new_context_and_state_the_subject_room()
    {
        foreach (var lite in new[] { false, true })
        {
            var prompt = DraftPromptBuilder.Build(DraftLanguage.English, VoiceGuide(), Card, SendDate, lite);

            prompt.ShouldContain("NOT in");
            prompt.ShouldContain("history, a consequence, or what to");
            prompt.ShouldContain("at most 60 characters in total");
            prompt.ShouldContain("(13 characters)");
            prompt.ShouldContain("at most 47");
        }
    }

    private static Task<(DraftResponse Text, IReadOnlyList<DraftWarning> Warnings, RepairReport Report)> Repair(
        DraftResponse text,
        IReadOnlyList<DraftWarning> warnings,
        List<string> calls,
        string answer) =>
        DraftRepair.RunAsync(
            (prompt, _) =>
            {
                calls.Add(prompt);
                return Task.FromResult(answer);
            },
            text,
            warnings,
            Card,
            DraftLanguage.English,
            SendDate,
            string.Empty,
            CancellationToken.None);
}
