using TheBluesland.WeeklyNewsletter.Ingest;
using TheBluesland.WeeklyNewsletter.Reporting;

namespace TheBluesland.WeeklyNewsletter.Clustering;

/// <summary>
/// The <c>cluster</c> subcommand (story-clustering spec, Design section 4): read one window of
/// stored articles, cluster them, print the job-summary report. Read-only - it never writes to the
/// database. The window is <c>[from, to)</c> on <c>published_at ?? fetched_at</c>; by default the
/// seven days before now. Exit code 0 on success (an empty window included), 1 on bad
/// configuration or a database error.
/// </summary>
public static class ClusterCommand
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(7);

    public static async Task<int> RunAsync(
        string clusteringPath,
        string feedsPath,
        string? connectionString,
        DateTimeOffset? from,
        DateTimeOffset? to,
        TimeProvider clock,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var options = await ClusteringOptionsLoader.LoadAsync(clusteringPath, cancellationToken);
        var feeds = await FeedConfigLoader.LoadAsync(feedsPath, cancellationToken);
        if (!options.IsValid || !feeds.IsValid)
        {
            await error.WriteLineAsync("Configuration is invalid; nothing was read:");
            foreach (var problem in options.Errors.Concat(feeds.Errors))
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
        var windowStart = from ?? windowEnd - DefaultWindow;
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

        var stories = StoryClusterer.Cluster(articles, options.Options!);
        var names = feeds.Feeds.ToDictionary(feed => feed.Id, feed => feed.Name, StringComparer.Ordinal);
        await output.WriteAsync(ClusterReportFormatter.ToMarkdown(stories, windowStart, windowEnd, options.Options!, names));
        return 0;
    }
}
