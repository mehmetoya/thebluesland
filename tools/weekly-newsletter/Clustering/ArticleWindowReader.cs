using Microsoft.EntityFrameworkCore;
using TheBluesland.Data;

namespace TheBluesland.WeeklyNewsletter.Clustering;

/// <summary>
/// Reads one window of stored articles, read-only: <c>[from, to)</c> on
/// <c>published_at ?? fetched_at</c> (story-clustering spec, Design section 1).
/// </summary>
public static class ArticleWindowReader
{
    public static async Task<List<ClusterInput>> ReadAsync(
        string connectionString,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        await using var context = NewsletterDbContext.Create(connectionString);
        return await context.NewsArticles
            .AsNoTracking()
            .Where(article => (article.PublishedAt ?? article.FetchedAt) >= from && (article.PublishedAt ?? article.FetchedAt) < to)
            .Select(article => new ClusterInput(
                article.Id,
                article.Source,
                article.Title,
                article.Summary,
                article.Url,
                article.PublishedAt ?? article.FetchedAt))
            .ToListAsync(cancellationToken);
    }
}
