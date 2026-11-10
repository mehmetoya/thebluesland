namespace TheBluesland.WeeklyNewsletter.Clustering;

/// <summary>
/// Articles that report the same event. Coverage is <see cref="SourceCount"/>: two articles from
/// the same source count once (story-clustering spec, Assumptions 3).
/// </summary>
/// <param name="Key">"s-" + the smallest article id; the same input always yields the same key.</param>
/// <param name="Articles">Oldest first; ties broken by id.</param>
/// <param name="Sources">Distinct sources in the order they first reported the story.</param>
public sealed record Story(
    string Key,
    IReadOnlyList<ClusterInput> Articles,
    IReadOnlyList<string> Sources)
{
    public int SourceCount => Sources.Count;

    public DateTimeOffset FirstSeen => Articles[0].EffectiveAt;

    public DateTimeOffset LastSeen => Articles[^1].EffectiveAt;
}
