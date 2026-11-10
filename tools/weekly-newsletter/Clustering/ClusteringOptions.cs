namespace TheBluesland.WeeklyNewsletter.Clustering;

/// <summary>Values from <c>clustering.yml</c> (story-clustering spec, Design section 2).</summary>
public sealed record ClusteringOptions(
    double SimilarityThreshold,
    TimeSpan MaxApart,
    int SummaryCharacters,
    IReadOnlyList<Abbreviation> Abbreviations);

/// <summary>A lowercase phrase replaced before punctuation is stripped.</summary>
public sealed record Abbreviation(string From, string To);

public sealed record ClusteringOptionsLoadResult(ClusteringOptions? Options, IReadOnlyList<string> Errors)
{
    public bool IsValid => Options is not null;
}
