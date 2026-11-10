using TheBluesland.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Ingest;
using TheBluesland.WeeklyNewsletter.Ranking;
using TheBluesland.WeeklyNewsletter.Reporting;
using TheBluesland.WeeklyNewsletter.Review;

namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>
/// The <c>draft</c> subcommand (issue-drafting spec, Design section 5): window → clusters → pick →
/// fact card → one Gemini call per language → checks → at most one self-repair call per language
/// (<see cref="DraftRepair"/>) → issue files, plus backups and the PR body in
/// a review/ folder that is never merged (issue-review spec) → job summary.
/// Writes only files; never the database, never a branch. Exit 0 when both languages were written
/// (warnings included); 1 on bad configuration, too few stories, a database or Gemini failure, or
/// a model answer that is malformed twice, or an issue file that already exists (never overwritten).
/// </summary>
public static class DraftCommand
{
    /// <summary>How long the default model rests before its one extra try (a busy model rarely recovers in seconds).</summary>
    public static readonly TimeSpan DefaultPrimaryRetryPause = TimeSpan.FromSeconds(90);

    public static async Task<int> RunAsync(
        string toolDirectory,
        string voiceGuidePath,
        string outputDirectory,
        string? connectionString,
        string? apiKey,
        string? model,
        DateTimeOffset? from,
        DateTimeOffset? to,
        HttpClient httpClient,
        TimeProvider clock,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken,
        TimeSpan? retryDelay = null,
        TimeSpan? primaryRetryPause = null)
    {
        var clustering = await ClusteringOptionsLoader.LoadAsync(Path.Combine(toolDirectory, "clustering.yml"), cancellationToken);
        var ranking = await RankingOptionsLoader.LoadAsync(Path.Combine(toolDirectory, "ranking.yml"), cancellationToken);
        var feeds = await FeedConfigLoader.LoadAsync(Path.Combine(toolDirectory, "feeds.yml"), cancellationToken);
        var problems = clustering.Errors.Concat(ranking.Errors).Concat(feeds.Errors).ToList();
        if (!File.Exists(voiceGuidePath))
        {
            problems.Add($"Voice guide not found: {voiceGuidePath}");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            problems.Add($"{IngestCommand.ConnectionStringVariable} is not set.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            problems.Add($"{GeminiClient.ApiKeyVariable} is not set.");
        }

        if (!string.IsNullOrWhiteSpace(model) && !GeminiClient.IsValidModel(model))
        {
            problems.Add($"{GeminiClient.ModelVariable} '{model}' is not a valid model id (lowercase letters, digits, '.' and '-').");
        }

        if (from is not null && to is not null && from >= to)
        {
            problems.Add("--from must be before --to.");
        }

        if (problems.Count > 0)
        {
            await error.WriteLineAsync("Cannot draft; nothing was read or written:");
            foreach (var problem in problems)
            {
                await error.WriteLineAsync($"  - {problem}");
            }

            return 1;
        }

        var week = IssueWeek.Resolve(from, to, clock.GetUtcNow());

        List<ClusterInput> articles;
        try
        {
            articles = await ArticleWindowReader.ReadAsync(connectionString!, week.From, week.To, cancellationToken);
        }
        catch (Exception ex) when (DatabaseErrors.Find(ex) is { } database)
        {
            await error.WriteLineAsync($"Database error: {DatabaseErrors.Describe(database)}");
            return 1;
        }

        var targets = DraftLanguage.All
            .SelectMany(language => new[] { IssueMarkdownWriter.IssueFileName(week, language), IssueMarkdownWriter.BackupsFileName(week, language) })
            .Append(IssueMarkdownWriter.PullRequestBodyFileName(week))
            .Append(IssueMarkdownWriter.PublishDecisionFileName(week))
            .Select(name => Path.Combine(outputDirectory, name))
            .ToList();
        if (targets.FirstOrDefault(File.Exists) is { } existing)
        {
            // Never overwrite a draft Mehmet may already have edited; delete it first to redraft.
            await error.WriteLineAsync($"{existing} already exists; delete it to draft {week.Id} again. Nothing was written.");
            return 1;
        }

        var stories = StoryClusterer.Cluster(articles, clustering.Options!);
        var result = StoryRanker.Rank(stories, ranking.Options!, clustering.Options!);
        if (result.Picks.Count < ranking.Options!.Picks)
        {
            await error.WriteLineAsync($"Only {result.Picks.Count} stories in {week.Id}'s window ({articles.Count} articles); an issue needs {ranking.Options.Picks}. Nothing was written.");
            return 1;
        }

        var names = feeds.Feeds.ToDictionary(feed => feed.Id, feed => feed.Name, StringComparer.Ordinal);
        var card = FactCard.From(result, names, clustering.Options!.SummaryCharacters);
        var voiceGuide = await File.ReadAllTextAsync(voiceGuidePath, cancellationToken);
        // An explicitly chosen model is respected; only the default falls back when it is overloaded.
        var primary = new GeminiClient(httpClient, apiKey!, string.IsNullOrWhiteSpace(model) ? GeminiClient.DefaultModel : model, retryDelay);
        var fallback = string.IsNullOrWhiteSpace(model) ? new GeminiClient(httpClient, apiKey!, GeminiClient.FallbackModel, retryDelay, GeminiClient.FallbackTemperature) : null;
        var pause = primaryRetryPause ?? DefaultPrimaryRetryPause;

        var drafts = new List<LanguageDraft>();
        foreach (var language in DraftLanguage.All)
        {
            var prompt = DraftPromptBuilder.Build(language, voiceGuide, card, week.SendDate);
            var gemini = primary;
            var usedPrompt = prompt;
            string? fellBackFrom = null;
            DraftResponse response;
            try
            {
                try
                {
                    response = await WriteAsync(primary, prompt, card, cancellationToken);
                }
                catch (HttpRequestException ex) when (fallback is not null && GeminiClient.IsUnavailable(ex) && !cancellationToken.IsCancellationRequested)
                {
                    // The lighter model writes visibly worse text, so the default gets one more chance
                    // after a pause before the draft is accepted from the fallback (content review ED-1).
                    await error.WriteLineAsync($"{primary.Model} is unavailable ({ex.Message}); retrying it once in {pause.TotalSeconds:0}s before using {fallback.Model} for the {language.Name} draft.");
                    await Task.Delay(pause, cancellationToken);
                    try
                    {
                        response = await WriteAsync(primary, prompt, card, cancellationToken);
                    }
                    catch (HttpRequestException retry) when (GeminiClient.IsUnavailable(retry) && !cancellationToken.IsCancellationRequested)
                    {
                        await error.WriteLineAsync($"{primary.Model} is still unavailable ({retry.Message}); writing the {language.Name} draft with {fallback.Model}.");
                        fellBackFrom = primary.Model;
                        gemini = fallback;
                        // The lighter model gets the short checklist prompt without the sample issue it would copy.
                        usedPrompt = DraftPromptBuilder.Build(language, voiceGuide, card, week.SendDate, lite: true);
                        response = await WriteAsync(fallback, usedPrompt, card, cancellationToken);
                    }
                }
            }
            catch (Exception ex) when ((ex is HttpRequestException or DraftFormatException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                await error.WriteLineAsync($"{language.Name} draft failed: {ex.Message} Nothing was written.");
                return 1;
            }

            var sampleText = DraftPromptBuilder.SampleText(voiceGuide);
            var draft = await FinishAsync(week, language, card, sampleText, gemini, fellBackFrom, response, error, cancellationToken);

            // A draft the final checker or a blocking validator problem rejects gets ONE regenerate + repair + check
            // cycle with the same model and prompt. A checker that could not answer (quota, HTTP) does not trigger it:
            // asking again would spend more quota on the same outage. The draft never fails the run.
            if (NeedsAnotherCycle(draft, card))
            {
                try
                {
                    var again = await WriteAsync(gemini, usedPrompt, card, cancellationToken);
                    var second = await FinishAsync(week, language, card, sampleText, gemini, fellBackFrom, again, error, cancellationToken);
                    draft = PublishDecision.Problems(second, card).Count == 0 ? second with { Regenerated = true } : draft with { Regenerated = true };
                }
                catch (Exception ex) when ((ex is HttpRequestException or DraftFormatException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
                {
                    await error.WriteLineAsync($"Regenerating the {language.Name} draft failed ({ex.Message}); keeping the first draft.");
                }
            }

            drafts.Add(draft);
        }

        drafts = DraftValidator.AddParity(drafts, card);

        Directory.CreateDirectory(Path.Combine(outputDirectory, IssueMarkdownWriter.ReviewDirectory));
        foreach (var draft in drafts)
        {
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, IssueMarkdownWriter.IssueFileName(week, draft.Language)), draft.IssueMarkdown, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, IssueMarkdownWriter.BackupsFileName(week, draft.Language)), draft.BackupsMarkdown, cancellationToken);
        }

        var rankReport = RankReportFormatter.ToMarkdown(result, stories.Count, week.From, week.To, names);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, IssueMarkdownWriter.PullRequestBodyFileName(week)),
            PullRequestBodyFormatter.ToMarkdown(week, card, drafts, rankReport),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, IssueMarkdownWriter.PublishDecisionFileName(week)),
            PublishDecision.Decide(drafts, card).ToJson(),
            cancellationToken);

        await output.WriteAsync(DraftReportFormatter.ToMarkdown(week, card, drafts));
        return 0;
    }

    /// <summary>True when the draft failed on content (a checker verdict or a blocking validator problem) and not merely because the checker could not answer.</summary>
    private static bool NeedsAnotherCycle(LanguageDraft draft, FactCard card) =>
        draft.Check is { Failure: null, Passed: false } || PublishDecision.BlockingWarnings(draft, card).Any();

    /// <summary>Validate → optional repair (one request) → final checker (one request) for one written answer.</summary>
    private static async Task<LanguageDraft> FinishAsync(
        IssueWeek week,
        DraftLanguage language,
        FactCard card,
        string sampleText,
        GeminiClient gemini,
        string? fellBackFrom,
        DraftResponse response,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var (validated, firstWarnings) = DraftValidator.Validate(response, card, language, week.SendDate, sampleText);

        // One optional repair request per language and cycle, with the model that wrote the draft; it can never fail the run.
        var (clean, warnings, repair) = await DraftRepair.RunAsync(
            (repairPrompt, token) => gemini.GenerateJsonAsync(repairPrompt, DraftPromptBuilder.ResponseSchema, token, GeminiClient.RepairMaxAttempts),
            validated,
            firstWarnings,
            card,
            language,
            week.SendDate,
            sampleText,
            cancellationToken);
        if (repair.Status == RepairStatus.Failed)
        {
            await error.WriteLineAsync($"Self-repair of the {language.Name} draft failed ({repair.Failure}); keeping the original draft.");
        }

        // The final checker reads what subscribers will read. Its failure is a not-passed verdict, never a failed run.
        var check = await DraftChecker.RunAsync(
            (checkPrompt, token) => gemini.GenerateJsonAsync(checkPrompt, DraftChecker.ResponseSchema, token, GeminiClient.RepairMaxAttempts),
            clean,
            card,
            language,
            week.SendDate,
            cancellationToken);
        if (check.Failure is not null)
        {
            await error.WriteLineAsync($"Final check of the {language.Name} draft could not run ({check.Failure}); counted as not passed.");
        }

        return new LanguageDraft(
            language,
            IssueMarkdownWriter.Issue(week, language, clean, card),
            IssueMarkdownWriter.Backups(week, language, clean, card),
            clean,
            warnings,
            gemini.Model,
            fellBackFrom,
            repair,
            check);
    }

    /// <summary>One call, and one more if the answer is malformed (spec, Design section 3).</summary>
    private static async Task<DraftResponse> WriteAsync(GeminiClient gemini, string prompt, FactCard card, CancellationToken cancellationToken)
    {
        try
        {
            return DraftResponse.Parse(await gemini.GenerateJsonAsync(prompt, DraftPromptBuilder.ResponseSchema, cancellationToken), card);
        }
        catch (DraftFormatException)
        {
            return DraftResponse.Parse(await gemini.GenerateJsonAsync(prompt, DraftPromptBuilder.ResponseSchema, cancellationToken), card);
        }
    }
}
