using TheBluesland.WeeklyNewsletter.Ingest;

// TheBluesland Weekly pipeline tool (docs/specs/weekly-newsletter.md). Each pipeline stage is a
// subcommand; only "ingest" exists so far (docs/specs/weekly-newsletter-news-ingest.md).
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

if (args.Length > 0 && string.Equals(args[0], "ingest", StringComparison.Ordinal))
{
    var feedsPath = args.Length > 1 && args[1].Length > 0
        ? args[1]
        : Path.Combine(Directory.GetCurrentDirectory(), "tools", "weekly-newsletter", "feeds.yml");

    return await IngestAsync(feedsPath, cts.Token);
}

Console.Error.WriteLine("Usage: ingest [path/to/feeds.yml]");
return 1;

static async Task<int> IngestAsync(string feedsPath, CancellationToken cancellationToken)
{
    var config = await FeedConfigLoader.LoadAsync(feedsPath, cancellationToken);
    if (!config.IsValid)
    {
        Console.Error.WriteLine($"feeds.yml is invalid ({feedsPath}); no feed was fetched:");
        foreach (var error in config.Errors)
        {
            Console.Error.WriteLine($"  - {error}");
        }

        return 1;
    }

    // T7/T8 (tasks/todo.md) replace this with the real fetch-and-store run.
    Console.WriteLine($"{config.EnabledFeeds.Count} enabled feed(s) of {config.Feeds.Count} configured.");
    return 0;
}
