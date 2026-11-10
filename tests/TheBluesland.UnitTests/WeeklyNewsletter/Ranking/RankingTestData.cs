using TheBluesland.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Ranking;
using static TheBluesland.UnitTests.WeeklyNewsletter.Clustering.ClusteringTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Ranking;

internal static class RankingTestData
{
    public static readonly IReadOnlyList<StoryTypeRule> Types =
    [
        new("roundup", 0, true, ["best songs"], []),
        new("obituary", 1.5, false, ["dies", "death"], ["near death"]),
        new("industry", 1.0, false, ["charts", "deal"], []),
        new("live", 0.25, false, ["tour", "residency"], []),
    ];

    public static RankingOptions Settings(int picks = 5, int backups = 3, int maxPerType = 2, double related = 0.15, double genreRatio = 0.7, double brandBonus = 0, string[]? brandKeywords = null, string[]? coreSources = null) =>
        new(picks, backups, 1.0, maxPerType, related, ["rock", "blues", "soul", "indie", "guitarist"], genreRatio, brandBonus, brandKeywords ?? [], coreSources ?? [], Types);

    private static long _nextId = 10_000;

    /// <summary>
    /// Thirty excluded (roundup) stories with unrelated words. They are never picked, but they give
    /// the week a realistic IDF, so one shared generic word does not make two stories "related".
    /// </summary>
    public static IEnumerable<Story> Background() =>
        Enumerable.Range(0, 30).Select(index => Story($"Best songs list {index}: Name{index} Other{index} Thing{index} Place{index}"));

    /// <summary>A story reported by <paramref name="sources"/> different outlets with the same headline.</summary>
    public static Story Story(string headline, int sources = 1, string? summary = null, double hoursAfterMonday = 0)
    {
        var articles = Enumerable.Range(0, sources)
            .Select(index => Article(Interlocked.Increment(ref _nextId), $"source-{index}", headline, summary, hoursAfterMonday + index))
            .ToList();
        return new Story($"s-{articles[0].Id}", articles, articles.Select(article => article.Source).ToList());
    }
}
