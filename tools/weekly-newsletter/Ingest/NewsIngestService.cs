using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using TheBluesland.Data;

namespace TheBluesland.WeeklyNewsletter.Ingest;

/// <summary>
/// One ingest run (news-ingest spec, Design section 4): fetch every enabled feed with bounded
/// parallelism, isolate each feed's failure, normalize URLs, and write all new items in a single
/// <c>INSERT ... ON CONFLICT (normalized_url) DO NOTHING</c>, so re-runs are idempotent and an
/// existing row is never rewritten. Items already older than the retention window are not
/// inserted (they would be deleted again in the same run and re-reported as "new" every time).
/// Caller cancellation is not a feed failure and propagates; a database failure is reported, not
/// thrown, so the job summary is still written.
/// </summary>
public sealed class NewsIngestService(
    RssFeedClient feedClient,
    Func<NewsletterDbContext> createContext,
    TimeProvider clock)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7 * 8);

    private const int MaxParallelFeeds = 4;

    // jsonb_to_recordset turns one JSON parameter into rows, so the whole batch is one statement
    // (no per-row round trips) and text[] categories travel as JSON arrays.
    private const string InsertSql =
        """
        insert into newsletter.news_article (source, title, url, normalized_url, summary, categories, published_at, fetched_at)
        select x.source, x.title, x.url, x.normalized_url, x.summary, coalesce(x.categories, '{}'), x.published_at, @fetched_at
        from jsonb_to_recordset(@items) as x(
            source text, title text, url text, normalized_url text, summary text, categories text[], published_at timestamptz)
        on conflict (normalized_url) do nothing
        returning source
        """;

    private static readonly JsonSerializerOptions RowJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<IngestReport> RunAsync(IReadOnlyList<FeedConfig> feeds, CancellationToken cancellationToken)
    {
        var fetched = new ConcurrentDictionary<string, FeedFetch>(StringComparer.Ordinal);

        await Parallel.ForEachAsync(
            feeds,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelFeeds, CancellationToken = cancellationToken },
            async (feed, token) => fetched[feed.Id] = await FetchOneAsync(feed, token));

        var fetchedAt = clock.GetUtcNow();
        var cutoff = fetchedAt - Retention;

        // feeds.yml order, not completion order: when two feeds carry the same story, the
        // earlier-listed source deterministically owns the row.
        var rows = feeds
            .SelectMany(feed => fetched[feed.Id].Items)
            .Where(item => (item.PublishedAt ?? fetchedAt) >= cutoff)
            .Select(item => new ArticleRow(
                item.Source,
                item.Title,
                item.Url.AbsoluteUri,
                ArticleUrlNormalizer.Normalize(item.Url),
                item.Summary,
                item.Categories,
                item.PublishedAt))
            .DistinctBy(row => row.NormalizedUrl, StringComparer.Ordinal)
            .ToList();

        Dictionary<string, int> addedBySource;
        string? databaseError = null;
        try
        {
            addedBySource = await InsertAsync(rows, fetchedAt, cancellationToken);
        }
        catch (DbException ex)
        {
            addedBySource = [];
            databaseError = $"{ex.GetType().Name}: {ex.Message}";
        }

        var results = feeds
            .Select(feed => fetched[feed.Id] is { Error: null } ok
                ? new FeedRunResult(feed.Id, feed.Name, FeedRunStatus.Succeeded, ok.Items.Count, addedBySource.GetValueOrDefault(feed.Id), null)
                : new FeedRunResult(feed.Id, feed.Name, FeedRunStatus.Failed, 0, 0, fetched[feed.Id].Error))
            .ToList();

        return new IngestReport(results, databaseError);
    }

    /// <summary>
    /// Anything that goes wrong with one feed - HTTP status, timeout, unreadable XML, a connection
    /// dropped mid-body, a regex timeout on hostile content - fails only that feed. Only the
    /// caller's own cancellation escapes.
    /// </summary>
    private async Task<FeedFetch> FetchOneAsync(FeedConfig feed, CancellationToken cancellationToken)
    {
        try
        {
            return new FeedFetch(await feedClient.FetchAsync(feed, cancellationToken), null);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            return new FeedFetch([], Describe(ex));
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } status } => $"HTTP {(int)status} {status}",
        HttpRequestException http => $"HTTP error: {http.Message}",
        TimeoutException timeout => timeout.Message,
        System.Xml.XmlException xml => $"Unreadable feed: {xml.Message}",
        IOException io => $"Connection dropped: {io.Message}",
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };

    private async Task<Dictionary<string, int>> InsertAsync(
        List<ArticleRow> rows,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        await using var context = createContext();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(InsertSql, connection);
        command.Parameters.Add(new NpgsqlParameter("items", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(rows, RowJson) });
        command.Parameters.Add(new NpgsqlParameter("fetched_at", NpgsqlDbType.TimestampTz) { Value = fetchedAt.UtcDateTime });

        var added = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var source = reader.GetString(0);
            added[source] = added.GetValueOrDefault(source) + 1;
        }

        return added;
    }

    private sealed record FeedFetch(IReadOnlyList<FeedItem> Items, string? Error);

    private sealed record ArticleRow(
        string Source,
        string Title,
        string Url,
        string NormalizedUrl,
        string? Summary,
        IReadOnlyList<string> Categories,
        DateTimeOffset? PublishedAt);
}
