namespace TheBluesland.WeeklyNewsletter.Ingest;

public enum FeedRunStatus
{
    Succeeded,
    Failed,
}

/// <summary>One row of the job-summary table (news-ingest spec, Design section 4, step 6).</summary>
public sealed record FeedRunResult(
    string FeedId,
    string FeedName,
    FeedRunStatus Status,
    int Fetched,
    int Added,
    string? Error);

/// <summary>Outcome of one ingest run, in feeds.yml order.</summary>
public sealed record IngestReport(IReadOnlyList<FeedRunResult> Feeds)
{
    public bool AnySucceeded => Feeds.Any(feed => feed.Status == FeedRunStatus.Succeeded);

    public int TotalAdded => Feeds.Sum(feed => feed.Added);
}
