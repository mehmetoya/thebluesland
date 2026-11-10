using TheBluesland.WeeklyNewsletter.Ingest;

// TheBluesland Weekly pipeline tool (docs/specs/weekly-newsletter.md). Each pipeline stage is a
// subcommand; only "ingest" exists so far (docs/specs/weekly-newsletter-news-ingest.md). Runs only
// from GitHub Actions (newsletter-ingest.yml) or Mehmet's own machine.
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

    using var httpClient = new HttpClient();
    return await IngestCommand.RunAsync(
        feedsPath,
        Environment.GetEnvironmentVariable(IngestCommand.ConnectionStringVariable),
        httpClient,
        TimeProvider.System,
        Console.Out,
        Console.Error,
        cts.Token);
}

Console.Error.WriteLine("Usage: ingest [path/to/feeds.yml]");
return 1;
