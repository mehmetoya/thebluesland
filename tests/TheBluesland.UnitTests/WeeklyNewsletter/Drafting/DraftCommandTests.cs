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
        var handler = new QueueHandler(EchoKeys, EchoKeys);

        var exitCode = await RunAsync(handler);

        exitCode.ShouldBe(0, _error.ToString());
        Directory.GetFiles(_out).Select(Path.GetFileName).Order().ShouldBe(["2026-W47.en.md", "2026-W47.tr.md"]);
        Directory.GetFiles(Path.Combine(_out, "review")).Select(Path.GetFileName).Order().ShouldBe(
            ["2026-W47.backups.en.md", "2026-W47.backups.tr.md", "2026-W47.pr-body.md"]);
        (await File.ReadAllTextAsync(Path.Combine(_out, "review", "2026-W47.pr-body.md"))).ShouldStartWith("## TheBluesland Weekly · 2026-W47");
        TheBluesland.WeeklyNewsletter.Review.WeeklyIssueValidator.ValidateDirectory(_out)
            .ShouldBe(["review/2026-W47.backups.en.md: not an issue file (<year>-W<week>.<tr|en>.md); backups and review files are never merged.",
                       "review/2026-W47.backups.tr.md: not an issue file (<year>-W<week>.<tr|en>.md); backups and review files are never merged.",
                       "review/2026-W47.pr-body.md: not an issue file (<year>-W<week>.<tr|en>.md); backups and review files are never merged."]);
        (await File.ReadAllTextAsync(Path.Combine(_out, "2026-W47.tr.md"))).ShouldContain("Kaynaklar: [Pitchfork](");
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[0].Body.ShouldContain("Write the issue in Turkish");
        handler.Requests[1].Body.ShouldContain("Write the issue in English");
        handler.Requests.ShouldAllBe(request => !Prompt(request.Body).Contains("http", StringComparison.Ordinal));
        _output.ToString().ShouldStartWith("## Weekly draft · 2026-W47 · send 2026-11-16");
        _output.ToString().ShouldContain("<details><summary>TR issue</summary>");
        _output.ToString().ShouldContain("Written by `test-model`.");
    }

    [Fact]
    public async Task RunAsync_asks_once_more_after_a_malformed_answer()
    {
        var handler = new QueueHandler(_ => Ok("{\"not\":\"a draft\"}"), EchoKeys, EchoKeys);

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());
        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task RunAsync_writes_nothing_when_the_answer_is_malformed_twice()
    {
        var handler = new QueueHandler(_ => Ok("nonsense"));

        (await RunAsync(handler)).ShouldBe(1);
        _error.ToString().ShouldContain("Turkish draft failed");
        Directory.Exists(_out).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_writes_nothing_when_gemini_refuses()
    {
        var handler = new QueueHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        (await RunAsync(handler)).ShouldBe(1);
        _error.ToString().ShouldContain("403");
        Directory.Exists(_out).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_gives_the_default_model_one_more_try_before_it_accepts_the_fallback()
    {
        // Turkish: the default fails twice (4 requests each), the fallback writes it. English: the default answers at once.
        var handler = new QueueHandler([.. Enumerable.Repeat<Func<HttpRequestMessage, HttpResponseMessage>>(Overloaded, 8), EchoKeys, EchoKeys]);

        (await RunAsync(handler, model: null)).ShouldBe(0, _error.ToString());

        handler.Requests.Count.ShouldBe(10);
        handler.Requests.Take(8).ShouldAllBe(request => request.Request.RequestUri!.AbsolutePath.Contains(GeminiClient.DefaultModel));
        handler.Requests[8].Request.RequestUri!.AbsolutePath.ShouldContain(GeminiClient.FallbackModel);
        handler.Requests[9].Request.RequestUri!.AbsolutePath.ShouldContain(GeminiClient.DefaultModel);
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
    }

    [Fact]
    public async Task RunAsync_keeps_the_default_model_when_its_second_try_succeeds()
    {
        var handler = new QueueHandler(Overloaded, Overloaded, Overloaded, Overloaded, EchoKeys, EchoKeys);

        (await RunAsync(handler, model: null)).ShouldBe(0, _error.ToString());

        handler.Requests.ShouldAllBe(request => request.Request.RequestUri!.AbsolutePath.Contains(GeminiClient.DefaultModel));
        _output.ToString().ShouldNotContain("DÜŞÜK MODEL");
        _output.ToString().ShouldContain($"Written by `{GeminiClient.DefaultModel}`.");
    }

    [Fact]
    public async Task RunAsync_tells_the_model_the_send_date()
    {
        var handler = new QueueHandler(EchoKeys);

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());

        handler.Requests.ShouldAllBe(request => Prompt(request.Body).Contains("The issue is sent on 2026-11-16.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_keeps_an_explicitly_chosen_model_even_when_it_is_overloaded()
    {
        var handler = new QueueHandler(Overloaded);

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
        var handler = new QueueHandler(EchoKeys);

        (await RunAsync(handler)).ShouldBe(1);
        _error.ToString().ShouldContain("already exists");
        (await File.ReadAllTextAsync(Path.Combine(_out, "2026-W47.tr.md"))).ShouldBe("edited by Mehmet");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_rejects_an_unsafe_model_id()
    {
        var handler = new QueueHandler(EchoKeys);

        (await RunAsync(handler, model: "../x")).ShouldBe(1);
        _error.ToString().ShouldContain("not a valid model id");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_without_api_key_fails_before_reading()
    {
        var handler = new QueueHandler(EchoKeys);

        (await RunAsync(handler, apiKey: null)).ShouldBe(1);
        _error.ToString().ShouldContain("GEMINI_API_KEY is not set.");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_refuses_a_window_with_too_few_stories()
    {
        var handler = new QueueHandler(EchoKeys);

        (await RunAsync(handler, from: new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero), to: new DateTimeOffset(2026, 11, 4, 20, 0, 0, TimeSpan.Zero))).ShouldBe(1);
        _error.ToString().ShouldContain("an issue needs 5");
        handler.Requests.ShouldBeEmpty();
    }

    private Task<int> RunAsync(QueueHandler handler, string? apiKey = "test-key", DateTimeOffset? from = null, DateTimeOffset? to = null, string? model = "test-model") =>
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

    private static HttpResponseMessage Overloaded(HttpRequestMessage request) =>
        new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":{\"message\":\"This model is currently experiencing high demand.\"}}") };

    /// <summary>Answers with a valid draft for exactly the story keys the prompt lists.</summary>
    private static HttpResponseMessage EchoKeys(HttpRequestMessage request)
    {
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        var keys = StoryKey().Matches(Prompt(body)).Select(match => match.Groups[1].Value).ToList();
        return Ok(FakeAnswer(keys));
    }

    private static string Prompt(string requestBody) =>
        JsonDocument.Parse(requestBody).RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK) { Content = new StringContent(GeminiEnvelope(text)) };

    [GeneratedRegex(@"Story key: (s-\d+) ")]
    private static partial Regex StoryKey();
}
