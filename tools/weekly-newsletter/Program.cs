using System.Globalization;
using TheBluesland.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Delivery;
using TheBluesland.WeeklyNewsletter.Drafting;
using TheBluesland.WeeklyNewsletter.Ingest;
using TheBluesland.WeeklyNewsletter.Ranking;
using TheBluesland.WeeklyNewsletter.Review;

// TheBluesland Weekly pipeline tool (docs/specs/weekly-newsletter.md). Each pipeline stage is a
// subcommand: "ingest" (docs/specs/weekly-newsletter-news-ingest.md), "cluster"
// (docs/specs/weekly-newsletter-story-clustering.md) and "rank"
// (docs/specs/weekly-newsletter-story-ranking.md); "draft" (docs/specs/weekly-newsletter-issue-drafting.md);
// "issue-week" and "validate-weekly" (docs/specs/weekly-newsletter-issue-review.md); "deliver"
// (docs/specs/weekly-newsletter-issue-delivery.md). Runs only from
// GitHub Actions or Mehmet's own machine, from the repository root.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

const string Usage = """
    Usage:
      ingest [path/to/feeds.yml]
      cluster [--from <date>] [--to <date>]   (UTC, yyyy-MM-dd[THH:mm[:ss]]; default: the 7 days before now)
      rank    [--from <date>] [--to <date>]   (same window; prints the 5 picks and 3 backups)
      draft   [--from <date>] [--to <date>] [--out <dir>]
              (default window: the 7 days before the last Sunday 10:00 Europe/Istanbul; default out: content/weekly)
      issue-week [--from <date>] [--to <date>]   (prints the issue id "draft" would write, e.g. 2026-W47)
      validate-weekly [path/to/content/weekly]
      deliver [--content <dir>] [--dry-run]   (schedules merged issues in Kit; default content: content/weekly)
      kit-sync-tags [--dry-run]   (tags each language's confirmed form subscribers with its delivery tag; Kit free plan has no form rules)
      kit-tags [tag ...]   (creates the delivery tags in Kit if missing and prints their ids; default: weekly-tr weekly-en)
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

    case "validate-weekly":
        {
            var directory = args.Length > 1 && args[1].Length > 0 ? args[1] : Path.Combine(Directory.GetCurrentDirectory(), "content", "weekly");
            var problems = WeeklyIssueValidator.ValidateDirectory(directory);
            foreach (var problem in problems)
            {
                Console.Error.WriteLine(problem);
            }

            Console.WriteLine(problems.Count == 0 ? $"content/weekly is valid ({directory})." : $"{problems.Count} problem(s) in {directory}.");
            return problems.Count == 0 ? 0 : 1;
        }

    case "kit-tags":
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(1) };
            return await KitTagsCommand.RunAsync(
                args.Length > 1 ? args[1..] : KitTagsCommand.DefaultTags,
                Environment.GetEnvironmentVariable(KitClient.ApiKeyVariable),
                httpClient,
                Console.Out,
                Console.Error,
                cts.Token);
        }

    case "kit-sync-tags":
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(1) };
            return await KitSyncTagsCommand.RunAsync(
                toolDirectory,
                Environment.GetEnvironmentVariable(KitClient.ApiKeyVariable),
                args.Skip(1).Contains("--dry-run"),
                httpClient,
                Console.Out,
                Console.Error,
                cts.Token);
        }

    case "deliver":
        {
            var contentDirectory = Path.Combine(Directory.GetCurrentDirectory(), "content", "weekly");
            var dryRun = false;
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] == "--dry-run")
                {
                    dryRun = true;
                }
                else if (args[i] == "--content" && i + 1 < args.Length && args[i + 1].Length > 0)
                {
                    contentDirectory = Path.GetFullPath(args[++i]);
                }
                else
                {
                    Console.Error.WriteLine($"Unrecognized or invalid argument '{args[i]}'.");
                    Console.Error.WriteLine(Usage);
                    return 1;
                }
            }

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(1) };
            return await DeliverCommand.RunAsync(
                toolDirectory,
                contentDirectory,
                Environment.GetEnvironmentVariable(KitClient.ApiKeyVariable),
                dryRun,
                httpClient,
                TimeProvider.System,
                Console.Out,
                Console.Error,
                cts.Token);
        }

    case "cluster" or "rank" or "draft" or "issue-week":
        {
            DateTimeOffset? from = null, to = null;
            var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "content", "weekly");
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

                if (args[0] == "draft" && args[i] == "--out" && i + 1 < args.Length && args[i + 1].Length > 0)
                {
                    outputDirectory = Path.GetFullPath(args[++i]);
                    continue;
                }

                Console.Error.WriteLine($"Unrecognized or invalid argument '{args[i]}'.");
                Console.Error.WriteLine(Usage);
                return 1;
            }

            if (args[0] == "issue-week")
            {
                Console.WriteLine(IssueWeek.Resolve(from, to, TimeProvider.System.GetUtcNow()).Id);
                return 0;
            }

            if (args[0] == "draft")
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
                return await DraftCommand.RunAsync(
                    toolDirectory,
                    Path.Combine(Directory.GetCurrentDirectory(), "docs", "weekly", "ses-rehberi.md"),
                    outputDirectory,
                    connectionString,
                    Environment.GetEnvironmentVariable(GeminiClient.ApiKeyVariable),
                    Environment.GetEnvironmentVariable(GeminiClient.ModelVariable),
                    from,
                    to,
                    httpClient,
                    TimeProvider.System,
                    Console.Out,
                    Console.Error,
                    cts.Token);
            }

            return args[0] == "rank"
                ? await RankCommand.RunAsync(toolDirectory, connectionString, from, to, TimeProvider.System, Console.Out, Console.Error, cts.Token)
                : await ClusterCommand.RunAsync(
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

// ISO dates only, so "11/03/2026" cannot be silently read as March or November.
static bool TryParseUtc(string text, out DateTimeOffset value) =>
    DateTimeOffset.TryParseExact(
        text.TrimEnd('Z'),
        ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"],
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
        out value);
