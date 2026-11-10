namespace TheBluesland.WeeklyNewsletter.Ranking;

/// <summary>Values from <c>ranking.yml</c> (story-ranking spec, Design sections 1–2).</summary>
public sealed record RankingOptions(
    int Picks,
    int Backups,
    double CoverageWeight,
    int MaxPerType,
    double RelatedThreshold,
    IReadOnlyList<string> GenreKeywords,
    double GenreSwapMinRatio,
    IReadOnlyList<StoryTypeRule> Types);

/// <summary>One story type, tried in file order; the first whose phrases match wins.</summary>
public sealed record StoryTypeRule(string Name, double Bonus, bool Excluded, IReadOnlyList<string> Phrases, IReadOnlyList<string> Exclude);

public sealed record RankingOptionsLoadResult(RankingOptions? Options, IReadOnlyList<string> Errors)
{
    public bool IsValid => Options is not null;
}
