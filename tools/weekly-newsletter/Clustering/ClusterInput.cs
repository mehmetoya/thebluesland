namespace TheBluesland.WeeklyNewsletter.Clustering;

/// <summary>
/// One stored article as story-clustering sees it (story-clustering spec, Design section 1).
/// <see cref="EffectiveAt"/> is <c>published_at ?? fetched_at</c>.
/// </summary>
public sealed record ClusterInput(
    long Id,
    string Source,
    string Title,
    string? Summary,
    string Url,
    DateTimeOffset EffectiveAt);
