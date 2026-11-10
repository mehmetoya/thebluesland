using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using TheBluesland.Data;

namespace TheBluesland.WeeklyNewsletter.Ingest;

/// <summary>
/// The <c>ingest</c> subcommand end to end (news-ingest spec, Design section 4): validate
/// feeds.yml and the connection string before any network call, fetch and store, apply the 8-week
/// retention, print the job-summary table. The summary is printed even when the database fails,
/// because it is the only place ingest health is visible. Exit code: 0 when at least one feed
/// succeeded and the database writes worked; 1 otherwise - a single broken feed must not turn the
/// two-hourly workflow red.
/// </summary>
public static class IngestCommand
{
    public const string ConnectionStringVariable = "NEON_NEWSLETTER_CONNECTION_STRING";

    public static async Task<int> RunAsync(
        string feedsPath,
        string? connectionString,
        HttpClient httpClient,
        TimeProvider clock,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var config = await FeedConfigLoader.LoadAsync(feedsPath, cancellationToken);
        if (!config.IsValid)
        {
            await error.WriteLineAsync($"feeds.yml is invalid ({feedsPath}); no feed was fetched:");
            foreach (var problem in config.Errors)
            {
                await error.WriteLineAsync($"  - {problem}");
            }

            return 1;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            await error.WriteLineAsync(
                $"{ConnectionStringVariable} is not set; no feed was fetched. It must hold the newsletter_writer role's connection string.");
            return 1;
        }

        NewsletterDbContext CreateContext() => NewsletterDbContext.Create(connectionString);

        var service = new NewsIngestService(new RssFeedClient(httpClient), CreateContext, clock);
        var report = await service.RunAsync(config.EnabledFeeds, cancellationToken);

        int? removed = null;
        if (report.DatabaseError is null)
        {
            try
            {
                removed = await RemoveExpiredAsync(CreateContext, clock.GetUtcNow() - NewsIngestService.Retention, cancellationToken);
            }
            catch (DbException ex)
            {
                report = report with { DatabaseError = $"Retention delete failed: {ex.GetType().Name}: {ex.Message}" };
            }
        }

        await output.WriteAsync(IngestReportFormatter.ToMarkdown(report, removed, clock.GetUtcNow()));
        if (report.DatabaseError is not null)
        {
            // Type and message only - no stack trace in the public Actions log.
            await error.WriteLineAsync($"Database error: {report.DatabaseError}");
        }

        return report.Healthy ? 0 : 1;
    }

    /// <summary>
    /// Deletes articles whose effective time (published, else fetched) is older than
    /// <paramref name="cutoff"/> - the same coalesce the time index is built on.
    /// </summary>
    private static async Task<int> RemoveExpiredAsync(
        Func<NewsletterDbContext> createContext,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        await using var context = createContext();
        return await context.NewsArticles
            .Where(article => (article.PublishedAt ?? article.FetchedAt) < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
