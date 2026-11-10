using TheBluesland.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Ingest;
using TheBluesland.WeeklyNewsletter.Reporting;

namespace TheBluesland.WeeklyNewsletter.Ranking;

/// <summary>
/// The <c>rank</c> subcommand (story-ranking spec, Design section 5): read the window, cluster,
/// rank, print the pick. Read-only. Same window rules and exit codes as <c>cluster</c>.
/// </summary>
public static class RankCommand
{
    public static async Task<int> RunAsync(
        string toolDirectory,
        string? connectionString,
        DateTimeOffset? from,
        DateTimeOffset? to,
        TimeProvider clock,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var clustering = await ClusteringOptionsLoader.LoadAsync(Path.Combine(toolDirectory, "clustering.yml"), cancellationToken);
        var ranking = await RankingOptionsLoader.LoadAsync(Path.Combine(toolDirectory, "ranking.yml"), cancellationToken);
        var feeds = await FeedConfigLoader.LoadAsync(Path.Combine(toolDirectory, "feeds.yml"), cancellationToken);
        if (!clustering.IsValid || !ranking.IsValid || !feeds.IsValid)
        {
            await error.WriteLineAsync("Configuration is invalid; nothing was read:");
            foreach (var problem in clustering.Errors.Concat(ranking.Errors).Concat(feeds.Errors))
            {
                await error.WriteLineAsync($"  - {problem}");
            }

            return 1;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            await error.WriteLineAsync($"{IngestCommand.ConnectionStringVariable} is not set; nothing was read.");
            return 1;
        }

        var windowEnd = to ?? clock.GetUtcNow();
        var windowStart = from ?? windowEnd - ClusterCommand.DefaultWindow;
        if (windowStart >= windowEnd)
        {
            await error.WriteLineAsync($"--from ({windowStart:O}) must be before --to ({windowEnd:O}).");
            return 1;
        }

        List<ClusterInput> articles;
        try
        {
            articles = await ArticleWindowReader.ReadAsync(connectionString, windowStart, windowEnd, cancellationToken);
        }
        catch (Exception ex) when (DatabaseErrors.Find(ex) is { } database)
        {
            await error.WriteLineAsync($"Database error: {DatabaseErrors.Describe(database)}");
            return 1;
        }

        var stories = StoryClusterer.Cluster(articles, clustering.Options!);
        var result = StoryRanker.Rank(stories, ranking.Options!, clustering.Options!);
        var names = feeds.Feeds.ToDictionary(feed => feed.Id, feed => feed.Name, StringComparer.Ordinal);
        await output.WriteAsync(RankReportFormatter.ToMarkdown(result, stories.Count, windowStart, windowEnd, names));
        return 0;
    }
}
