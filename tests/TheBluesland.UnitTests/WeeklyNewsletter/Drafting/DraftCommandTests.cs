using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using TheBluesland.Data.Entities;
using TheBluesland.UnitTests.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Drafting;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

/// <summary>issue-drafting spec, Design section 5: the "draft" command end to end with a fake Gemini.</summary>
public sealed partial class DraftCommandTests : IAsyncLifetime
{
    private static readonly DateTimeOffset From = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 11, 10, 11, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly string _out = Path.Combine(Path.GetTempPath(), $"weekly-{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = NewsletterDbContext.Create(_postgres.GetConnectionString());
        await context.Database.MigrateAsync();
        context.NewsArticles.AddRange(W46Articles().Select(article => new NewsArticle
        {
            Source = article.Source,
            Title = article.Title,
            Summary = article.Summary,
            Url = article.Url,
            NormalizedUrl = article.Url,
            PublishedAt = article.EffectiveAt,
            FetchedAt = article.EffectiveAt,
        }));
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (Directory.Exists(_out))
        {
            Directory.Delete(_out, recursive: true);
        }

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_writes_both_languages_and_their_backups_and_reports()
    {
        var handler = new Script();

        var exitCode = await RunAsync(handler);

        exitCode.ShouldBe(0, _error.ToString());
        Directory.GetFiles(_out).Select(Path.GetFileName).Order().ShouldBe(["2026-W47.en.md", "2026-W47.tr.md"]);
        Directory.GetFiles(Path.Combine(_out, "review")).Select(Path.GetFileName).Order().ShouldBe(
            ["2026-W47.backups.en.md", "2026-W47.backups.tr.md", "2026-W47.pr-body.md", "2026-W47.publish.json"]);
        (await File.ReadAllTextAsync(Path.Combine(_out, "review", "2026-W47.pr-body.md"))).ShouldStartWith("## TheBluesland Weekly · 2026-W47");
        TheBluesland.WeeklyNewsletter.Review.WeeklyIssueValidator.ValidateDirectory(_out)
            .ShouldBe(["review/2026-W47.backups.en.md: not an issue file (<year>-W<week>.<tr|en>.md); backups and review files are never merged.",
                       "review/2026-W47.backups.tr.md: not an issue file (<year>-W<week>.<tr|en>.md); backups and review files are never merged.",
                       "review/2026-W47.pr-body.md: not an issue file (<year>-W<week>.<tr|en>.md); backups and review files are never merged.",
                       "review/2026-W47.publish.json: not an issue file (<year>-W<week>.<tr|en>.md); backups and review files are never merged."]);
        (await File.ReadAllTextAsync(Path.Combine(_out, "2026-W47.tr.md"))).ShouldContain("Kaynaklar: [Pitchfork](");
        (await File.ReadAllTextAsync(Path.Combine(_out, "2026-W47.tr.md"))).TrimEnd().ShouldEndWith("*Bu sayı TheBluesland ve Gemini'nin elinden.*");
        (await File.ReadAllTextAsync(Path.Combine(_out, "2026-W47.en.md"))).TrimEnd().ShouldEndWith("*This issue comes from TheBluesland and Gemini.*");
        // Per language: one write and one final check; no repair and no regeneration for a clean answer.
        handler.Requests.Count.ShouldBe(4);
        handler.Requests[0].Body.ShouldContain("Write the issue in Turkish");
        Prompt(handler.Requests[1].Body).ShouldContain("final fact-checker");
        handler.Requests[2].Body.ShouldContain("Write the issue in English");
        Prompt(handler.Requests[3].Body).ShouldContain("final fact-checker");
        handler.Requests.ShouldAllBe(request => !Prompt(request.Body).Contains("http", StringComparison.Ordinal));
        _output.ToString().ShouldStartWith("## Weekly draft · 2026-W47 · send 2026-11-16");
        _output.ToString().ShouldContain("<details><summary>TR issue</summary>");
        _output.ToString().ShouldContain("Written by `test-model`.");
        (await File.ReadAllTextAsync(Path.Combine(_out, "review", "2026-W47.pr-body.md"))).ShouldContain("Son kontrol: **geçti**");
        (await Decision()).AutoMerge.ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_repairs_a_language_once_and_reports_the_warning_drop()
    {
        // Turkish answers with a stock phrase, its one repair is clean, English needs no repair.
        var handler = new Script(writes: [StockPhrase, Clean], repairs: [Clean]);

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());

        handler.Requests.Count.ShouldBe(5);
        Prompt(handler.Requests[1].Body).ShouldContain("PROBLEMS:");
        Prompt(handler.Requests[3].Body).ShouldContain("Write the issue in English");
        var body = await File.ReadAllTextAsync(Path.Combine(_out, "review", "2026-W47.pr-body.md"));
        var line = TurkishRepairLine().Match(body);
        line.Success.ShouldBeTrue(body);
        int.Parse(line.Groups["before"].Value).ShouldBeGreaterThan(int.Parse(line.Groups["after"].Value));
        body.ShouldContain("Otomatik düzeltme: gerekmedi");
        (await File.ReadAllTextAsync(Path.Combine(_out, "2026-W47.tr.md"))).ShouldNotContain("pioneering");
    }

    [Fact]
    public async Task RunAsync_keeps_the_draft_and_notes_it_when_the_repair_request_fails()
    {
        var handler = new Script(writes: [StockPhrase, Clean], repairs: [_ => new HttpResponseMessage(HttpStatusCode.Forbidden)]);

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());

        handler.Requests.Count.ShouldBe(5);
        _error.ToString().ShouldContain("Self-repair of the Turkish draft failed");
        var body = await File.ReadAllTextAsync(Path.Combine(_out, "review", "2026-W47.pr-body.md"));
        body.ShouldContain("düzeltme isteği başarısız");
        body.ShouldContain("ilk taslak korundu");
        (await File.ReadAllTextAsync(Path.Combine(_out, "2026-W47.tr.md"))).ShouldContain("pioneering");
    }

    [Fact]
    public async Task RunAsync_never_makes_more_than_one_repair_request_per_language()
    {
        // The repair answer is as bad as the draft: rejected, and no second repair follows.
        var handler = new Script(writes: [StockPhrase], repairs: [StockPhrase]);

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());

        handler.Requests.Count.ShouldBe(6);
        handler.Requests.Count(request => Prompt(request.Body).Contains("PROBLEMS:", StringComparison.Ordinal)).ShouldBe(2);
        (await File.ReadAllTextAsync(Path.Combine(_out, "review", "2026-W47.pr-body.md"))).ShouldContain("ilk taslak korundu");
    }

    [Fact]
    public async Task RunAsync_publishes_automatically_when_both_languages_pass_the_final_check()
    {
        (await RunAsync(new Script())).ShouldBe(0, _error.ToString());

        var decision = await Decision();
        decision.AutoMerge.ShouldBeTrue();
        decision.Reasons.ShouldBeEmpty();
        (await File.ReadAllTextAsync(Path.Combine(_out, "review", "2026-W47.pr-body.md"))).ShouldContain("Otomatik yayın: evet");
    }

    [Fact]
    public async Task RunAsync_regenerates_once_when_the_checker_fails_and_keeps_a_passing_second_draft()
    {
        var handler = new Script(checks: [Verdict(pass: false, "s-1: invented date"), Verdict(pass: true), Verdict(pass: true)]);

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());

        // Turkish: write, check (fail), regenerate, check (pass); English: write, check (pass).
        handler.Requests.Count.ShouldBe(6);
        Prompt(handler.Requests[2].Body).ShouldContain("Write the issue in Turkish");
        (await Decision()).AutoMerge.ShouldBeTrue();
        (await File.ReadAllTextAsync(Path.Combine(_out, "review", "2026-W47.pr-body.md"))).ShouldContain("bir kez yeniden yazma");
    }

    [Fact]
    public async Task RunAsync_does_not_publish_when_the_second_draft_fails_too_and_never_tries_a_third()
    {
        var handler = new Script(checks: [Verdict(pass: false, "s-1: invented date"), Verdict(pass: false, "s-1: still invented"), Verdict(pass: true)]);

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());

        handler.Requests.Count.ShouldBe(6);
        var decision = await Decision();
        decision.AutoMerge.ShouldBeFalse();
        decision.Reasons.ShouldHaveSingleItem().ShouldContain("TR: son kontrol geçmedi");
        var body = await File.ReadAllTextAsync(Path.Combine(_out, "review", "2026-W47.pr-body.md"));
        body.ShouldContain("Otomatik yayın: hayır");
        body.ShouldContain("needs-human");
        body.ShouldContain("Son kontrol: **geçmedi**");
    }

    [Theory]
    [InlineData(403)]
    [InlineData(200)]
    public async Task RunAsync_counts_a_checker_that_cannot_answer_as_not_passed_without_failing_or_regenerating(int status)
    {
        Func<HttpRequestMessage, HttpResponseMessage> broken = status == 200 ? _ => Ok("{\"verdict\":\"maybe\"}") : _ => new HttpResponseMessage(HttpStatusCode.Forbidden);
        var handler = new Script(checks: [broken]);

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());

        handler.Requests.Count.ShouldBe(4);
        _error.ToString().ShouldContain("Final check of the Turkish draft could not run");
        var decision = await Decision();
        decision.AutoMerge.ShouldBeFalse();
        decision.Reasons.Count.ShouldBe(2);
        decision.Reasons.ShouldAllBe(reason => reason.Contains("yanıt veremedi", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_blocks_publishing_on_an_unsupported_number_even_when_the_checker_passes()
    {
        // Every answer carries a number the card does not state: the second cycle does not help, nothing publishes.
        var handler = new Script(writes: [InventedNumber]);

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());

        handler.Requests.Count(request => Prompt(request.Body).Contains("Write the issue in Turkish", StringComparison.Ordinal)).ShouldBe(2);
        var decision = await Decision();
        decision.AutoMerge.ShouldBeFalse();
        decision.Reasons.ShouldContain(reason => reason.Contains("engelleyen doğrulama uyarısı", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_asks_once_more_after_a_malformed_answer()
    {
        var handler = new Script(writes: [_ => Ok("{\"not\":\"a draft\"}"), Clean, Clean]);

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());
        handler.Requests.Count.ShouldBe(5);
    }

    [Fact]
    public async Task RunAsync_writes_nothing_when_the_answer_is_malformed_twice()
    {
        var handler = new Script(writes: [_ => Ok("nonsense")]);

        (await RunAsync(handler)).ShouldBe(1);
        _error.ToString().ShouldContain("Turkish draft failed");
        Directory.Exists(_out).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_writes_nothing_when_gemini_refuses()
    {
        var handler = new Script(writes: [_ => new HttpResponseMessage(HttpStatusCode.Forbidden)]);

        (await RunAsync(handler)).ShouldBe(1);
        _error.ToString().ShouldContain("403");
        Directory.Exists(_out).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_gives_the_default_model_one_more_try_before_it_accepts_the_fallback()
    {
        // Turkish: the default fails twice (4 requests each), the fallback writes it. English: the default answers at once.
        var handler = new Script(writes: [.. Enumerable.Repeat<Func<HttpRequestMessage, HttpResponseMessage>>(Overloaded, 8), Clean]);

        (await RunAsync(handler, model: null)).ShouldBe(0, _error.ToString());

        handler.Requests.Count.ShouldBe(12);
        handler.Requests.Take(8).ShouldAllBe(request => request.Request.RequestUri!.AbsolutePath.Contains(GeminiClient.DefaultModel));
        handler.Requests[8].Request.RequestUri!.AbsolutePath.ShouldContain(GeminiClient.FallbackModel);
        handler.Requests[10].Request.RequestUri!.AbsolutePath.ShouldContain(GeminiClient.DefaultModel);
        // The fallback writes at a lower temperature from the short checklist prompt, without the sample issue.
        handler.Requests[8].Body.ShouldContain("\"temperature\":0.2");
        handler.Requests[8].Body.ShouldContain("Check every item before you answer");
        handler.Requests[8].Body.ShouldNotContain("Tessaly Brand");
        handler.Requests[0].Body.ShouldContain("\"temperature\":0.4");
        handler.Requests[0].Body.ShouldContain("Tessaly Brand");
        _output.ToString().ShouldContain($"Written by `{GeminiClient.FallbackModel}` because `{GeminiClient.DefaultModel}` was unavailable");
        _output.ToString().ShouldContain("**[DÜŞÜK MODEL]**");
        _error.ToString().ShouldContain($"writing the Turkish draft with {GeminiClient.FallbackModel}");
        (await File.ReadAllTextAsync(Path.Combine(_out, "review", "2026-W47.pr-body.md"))).ShouldContain("**[DÜŞÜK MODEL]**");
        // The lighter model never publishes on its own, even with a passing checker.
        var decision = await Decision();
        decision.AutoMerge.ShouldBeFalse();
        decision.Reasons.ShouldHaveSingleItem().ShouldContain("TR: daha hafif yedek modelle");
    }

    [Fact]
    public async Task RunAsync_keeps_the_default_model_when_its_second_try_succeeds()
    {
        var handler = new Script(writes: [Overloaded, Overloaded, Overloaded, Overloaded, Clean]);

        (await RunAsync(handler, model: null)).ShouldBe(0, _error.ToString());

        handler.Requests.ShouldAllBe(request => request.Request.RequestUri!.AbsolutePath.Contains(GeminiClient.DefaultModel));
        _output.ToString().ShouldNotContain("DÜŞÜK MODEL");
        _output.ToString().ShouldContain($"Written by `{GeminiClient.DefaultModel}`.");
    }

    [Fact]
    public async Task RunAsync_tells_the_model_the_send_date()
    {
        var handler = new Script();

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());

        handler.Requests.ShouldAllBe(request => Prompt(request.Body).Contains("The issue is sent on 2026-11-16.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_keeps_an_explicitly_chosen_model_even_when_it_is_overloaded()
    {
        var handler = new Script(writes: [Overloaded]);

        (await RunAsync(handler, model: "chosen-model")).ShouldBe(1);
        handler.Requests.Count.ShouldBe(4);
        handler.Requests.ShouldAllBe(request => request.Request.RequestUri!.AbsolutePath.Contains("chosen-model"));
        Directory.Exists(_out).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_never_overwrites_an_existing_draft()
    {
        Directory.CreateDirectory(_out);
        await File.WriteAllTextAsync(Path.Combine(_out, "2026-W47.tr.md"), "edited by Mehmet");
        var handler = new Script();

        (await RunAsync(handler)).ShouldBe(1);
        _error.ToString().ShouldContain("already exists");
        (await File.ReadAllTextAsync(Path.Combine(_out, "2026-W47.tr.md"))).ShouldBe("edited by Mehmet");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_rejects_an_unsafe_model_id()
    {
        var handler = new Script();

        (await RunAsync(handler, model: "../x")).ShouldBe(1);
        _error.ToString().ShouldContain("not a valid model id");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_without_api_key_fails_before_reading()
    {
        var handler = new Script();

        (await RunAsync(handler, apiKey: null)).ShouldBe(1);
        _error.ToString().ShouldContain("GEMINI_API_KEY is not set.");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_refuses_a_window_with_too_few_stories()
    {
        var handler = new Script();

        (await RunAsync(handler, from: new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero), to: new DateTimeOffset(2026, 11, 4, 20, 0, 0, TimeSpan.Zero))).ShouldBe(1);
        _error.ToString().ShouldContain("an issue needs 5");
        handler.Requests.ShouldBeEmpty();
    }

    private Task<int> RunAsync(HttpMessageHandler handler, string? apiKey = "test-key", DateTimeOffset? from = null, DateTimeOffset? to = null, string? model = "test-model") =>
        DraftCommand.RunAsync(
            Path.GetDirectoryName(RepoFiles.Find("tools/weekly-newsletter/ranking.yml"))!,
            RepoFiles.Find("docs/weekly/ses-rehberi.md"),
            _out,
            _postgres.GetConnectionString(),
            apiKey,
            model,
            from ?? From,
            to ?? To,
            new HttpClient(handler),
            TimeProvider.System,
            _output,
            _error,
            CancellationToken.None,
            TimeSpan.Zero,
            TimeSpan.Zero);

    private async Task<(bool AutoMerge, List<string> Reasons)> Decision()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_out, "review", "2026-W47.publish.json")));
        return (
            document.RootElement.GetProperty("autoMerge").GetBoolean(),
            [.. document.RootElement.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()!)]);
    }

    private static HttpResponseMessage Overloaded(HttpRequestMessage request) =>
        new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":{\"message\":\"This model is currently experiencing high demand.\"}}") };

    /// <summary>A valid answer for exactly the story keys the prompt lists; headlines carry no digits, so the number check stays quiet.</summary>
    private static HttpResponseMessage Clean(HttpRequestMessage request) => Answer(request, "It matters.", "Body text.");

    /// <summary>Like <see cref="Clean"/>, but the why-it-matters lines use a stock phrase, so the validator asks for a repair.</summary>
    private static HttpResponseMessage StockPhrase(HttpRequestMessage request) => Answer(request, "It is pioneering.", "Body text.");

    /// <summary>Like <see cref="Clean"/>, but every body states a number the fact card does not.</summary>
    private static HttpResponseMessage InventedNumber(HttpRequestMessage request) => Answer(request, "It matters.", "Body text with 7777 items.");

    private static HttpResponseMessage Answer(HttpRequestMessage request, string why, string body)
    {
        var keys = StoryKey().Matches(Prompt(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())).Select(match => match.Groups[1].Value).ToList();
        return Ok(JsonSerializer.Serialize(new
        {
            intro = "Five stories, five minutes.",
            outro = "See you next Monday.",
            subject = "Two hooks · TBL Weekly",
            preview = "A different preview line.",
            stories = keys.Select(key => new { key, headline = $"Headline {new string([.. key.Where(char.IsDigit).Select(digit => (char)('a' + digit - '0'))])}", body, whyItMatters = why }),
        }));
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Verdict(bool pass, params string[] problems) =>
        _ => Ok(JsonSerializer.Serialize(new { pass, problems }));

    private static string Prompt(string requestBody) =>
        JsonDocument.Parse(requestBody).RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK) { Content = new StringContent(GeminiEnvelope(text)) };

    [GeneratedRegex(@"Story key: (s-\d+) ")]
    private static partial Regex StoryKey();

    [GeneratedRegex(@"Otomatik düzeltme: (?<before>\d+) uyarı → (?<after>\d+) uyarı")]
    private static partial Regex TurkishRepairLine();

    /// <summary>
    /// A fake Gemini that tells the three kinds of request apart by their prompt (write, repair, final check) and
    /// answers each kind from its own queue; the last answer of a queue repeats. Records every request.
    /// </summary>
    private sealed class Script(
        Func<HttpRequestMessage, HttpResponseMessage>[]? writes = null,
        Func<HttpRequestMessage, HttpResponseMessage>[]? repairs = null,
        Func<HttpRequestMessage, HttpResponseMessage>[]? checks = null) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>[] _writes = writes ?? [Clean];
        private readonly Func<HttpRequestMessage, HttpResponseMessage>[] _repairs = repairs ?? [Clean];
        private readonly Func<HttpRequestMessage, HttpResponseMessage>[] _checks = checks ?? [Verdict(pass: true)];
        private int _write;
        private int _repair;
        private int _check;

        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            var prompt = Prompt(body);
            var respond = prompt.Contains("final fact-checker", StringComparison.Ordinal)
                ? _checks[Math.Min(_check++, _checks.Length - 1)]
                : prompt.Contains("PROBLEMS:", StringComparison.Ordinal)
                    ? _repairs[Math.Min(_repair++, _repairs.Length - 1)]
                    : _writes[Math.Min(_write++, _writes.Length - 1)];
            return respond(request);
        }
    }
}
