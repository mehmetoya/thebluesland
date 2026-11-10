namespace TheBluesland.Data.Entities;

/// <summary>
/// One ingested news item (docs/specs/weekly-newsletter-news-ingest.md, Design section 1). Written
/// only by tools/weekly-newsletter through the newsletter_writer role; TheBluesland.Web never maps
/// <see cref="NewsletterDbContext"/>. <see cref="Summary"/> is the feed's own excerpt, kept for
/// internal clustering/fact-card use only - never published as-is.
/// </summary>
public sealed class NewsArticle
{
    public long Id { get; init; }

    /// <summary>The <c>id</c> of the source in tools/weekly-newsletter/feeds.yml.</summary>
    public required string Source { get; init; }

    public required string Title { get; init; }

    public required string Url { get; init; }

    /// <summary>De-duplication key; unique (news-ingest spec, Design section 2).</summary>
    public required string NormalizedUrl { get; init; }

    public string? Summary { get; init; }

    public List<string> Categories { get; init; } = [];

    public DateTimeOffset? PublishedAt { get; init; }

    public required DateTimeOffset FetchedAt { get; init; }
}
