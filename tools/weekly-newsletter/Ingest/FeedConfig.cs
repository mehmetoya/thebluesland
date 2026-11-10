namespace TheBluesland.WeeklyNewsletter.Ingest;

/// <summary>
/// One source from <c>feeds.yml</c> (news-ingest spec, Design section 3). <see cref="Id"/> is
/// stable for the life of the newsletter: <c>news_article.source</c> stores it.
/// </summary>
public sealed record FeedConfig(string Id, string Name, Uri Url, bool Enabled);

/// <summary>
/// Outcome of reading <c>feeds.yml</c>. An invalid file yields no feeds at all, so a typo can never
/// turn into a silently partial ingest run.
/// </summary>
public sealed record FeedConfigLoadResult(IReadOnlyList<FeedConfig> Feeds, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<FeedConfig> EnabledFeeds => [.. Feeds.Where(feed => feed.Enabled)];

    public static FeedConfigLoadResult Invalid(IReadOnlyList<string> errors) => new([], errors);
}
