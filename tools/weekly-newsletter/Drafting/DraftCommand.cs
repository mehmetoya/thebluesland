using TheBluesland.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Ingest;
using TheBluesland.WeeklyNewsletter.Ranking;
using TheBluesland.WeeklyNewsletter.Reporting;

namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>
/// The <c>draft</c> subcommand (issue-drafting spec, Design section 5): window → clusters → pick →
/// fact card → one Gemini call per language → checks → issue and backups files → job summary.
/// Writes only files; never the database, never a branch. Exit 0 when both languages were written
/// (warnings included); 1 on bad configuration, too few stories, a database or Gemini failure, or
/// a model answer that is malformed twice, or an issue file that already exists (never overwritten).
/// </summary>
public static class DraftCommand
{
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
        CancellationToken cancellationToken)
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

        IssueWeek week;
        if (to is { } end)
        {
            week = IssueWeek.ForWindow(from ?? end - ClusterCommand.DefaultWindow, end);
        }
        else
        {
            week = IssueWeek.ForDraftAt(clock.GetUtcNow());
            week = from is { } start ? week with { From = start } : week;
        }

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
            .Select(name => Path.Combine(outputDirectory, name))
            .ToList();
        if (targets.FirstOrDefault(File.Exists) is { } existing)
        {
            // Never overwrite a draft Mehmet may already have edited; delete it first to redraft.
            await error.WriteLineAsync($"{existing} already exists; delete it to draft {week.Id} again. Nothing was written.");
            return 1;
        }

        var result = StoryRanker.Rank(StoryClusterer.Cluster(articles, clustering.Options!), ranking.Options!, clustering.Options!);
        if (result.Picks.Count < ranking.Options!.Picks)
        {
            await error.WriteLineAsync($"Only {result.Picks.Count} stories in {week.Id}'s window ({articles.Count} articles); an issue needs {ranking.Options.Picks}. Nothing was written.");
            return 1;
        }

        var names = feeds.Feeds.ToDictionary(feed => feed.Id, feed => feed.Name, StringComparer.Ordinal);
        var card = FactCard.From(result, names, clustering.Options!.SummaryCharacters);
        var voiceGuide = await File.ReadAllTextAsync(voiceGuidePath, cancellationToken);
        var gemini = new GeminiClient(httpClient, apiKey!, string.IsNullOrWhiteSpace(model) ? GeminiClient.DefaultModel : model);

        var drafts = new List<LanguageDraft>();
        foreach (var language in DraftLanguage.All)
        {
            DraftResponse response;
            try
            {
                response = await WriteAsync(gemini, DraftPromptBuilder.Build(language, voiceGuide, card), card, cancellationToken);
            }
            catch (Exception ex) when ((ex is HttpRequestException or DraftFormatException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                await error.WriteLineAsync($"{language.Name} draft failed: {ex.Message} Nothing was written.");
                return 1;
            }

            var (clean, warnings) = DraftValidator.Validate(response, card);
            drafts.Add(new LanguageDraft(
                language,
                IssueMarkdownWriter.Issue(week, language, clean, card),
                IssueMarkdownWriter.Backups(week, language, clean, card),
                warnings));
        }

        Directory.CreateDirectory(outputDirectory);
        foreach (var draft in drafts)
        {
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, IssueMarkdownWriter.IssueFileName(week, draft.Language)), draft.IssueMarkdown, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, IssueMarkdownWriter.BackupsFileName(week, draft.Language)), draft.BackupsMarkdown, cancellationToken);
        }

        await output.WriteAsync(DraftReportFormatter.ToMarkdown(week, card, drafts));
        return 0;
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
