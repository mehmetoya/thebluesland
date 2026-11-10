namespace TheBluesland.WeeklyNewsletter.Ingest;

/// <summary>
/// One parsed feed entry, before URL normalization and storage (news-ingest spec, Design section 1).
/// <see cref="Summary"/> is plain text for internal use only (clustering and the fact card) - it is
/// never published as-is.
/// </summary>
public sealed record FeedItem(
    string Source,
    string Title,
    Uri Url,
    DateTimeOffset? PublishedAt,
    string? Summary,
    IReadOnlyList<string> Categories);
