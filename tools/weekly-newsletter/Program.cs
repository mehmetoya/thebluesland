using System.Globalization;
using TheBluesland.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Ingest;

// TheBluesland Weekly pipeline tool (docs/specs/weekly-newsletter.md). Each pipeline stage is a
// subcommand: "ingest" (docs/specs/weekly-newsletter-news-ingest.md) and "cluster"
// (docs/specs/weekly-newsletter-story-clustering.md). Runs only from GitHub Actions or Mehmet's
// own machine, from the repository root.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

const string Usage = """
    Usage:
      ingest [path/to/feeds.yml]
      cluster [--from <date>] [--to <date>]   (UTC; default: the 7 days before now)
    """;

var toolDirectory = Path.Combine(Directory.GetCurrentDirectory(), "tools", "weekly-newsletter");
var connectionString = Environment.GetEnvironmentVariable(IngestCommand.ConnectionStringVariable);

switch (args.FirstOrDefault())
{
    case "ingest":
    {
        var feedsPath = args.Length > 1 && args[1].Length > 0 ? args[1] : Path.Combine(toolDirectory, "feeds.yml");
        using var httpClient = new HttpClient();
        return await IngestCommand.RunAsync(feedsPath, connectionString, httpClient, TimeProvider.System, Console.Out, Console.Error, cts.Token);
    }

    case "cluster":
    {
        DateTimeOffset? from = null, to = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] is "--from" or "--to" && i + 1 < args.Length && TryParseUtc(args[i + 1], out var value))
            {
                if (args[i] == "--from")
                {
                    from = value;
                }
                else
                {
                    to = value;
                }

                i++;
                continue;
            }

            Console.Error.WriteLine($"Unrecognized or invalid argument '{args[i]}'.");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        return await ClusterCommand.RunAsync(
            Path.Combine(toolDirectory, "clustering.yml"),
            Path.Combine(toolDirectory, "feeds.yml"),
            connectionString,
            from,
            to,
            TimeProvider.System,
            Console.Out,
            Console.Error,
            cts.Token);
    }

    default:
        Console.Error.WriteLine(Usage);
        return 1;
}

static bool TryParseUtc(string text, out DateTimeOffset value) =>
    DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
