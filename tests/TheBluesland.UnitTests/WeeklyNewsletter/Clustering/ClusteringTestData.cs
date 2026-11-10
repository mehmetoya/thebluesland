using TheBluesland.WeeklyNewsletter.Clustering;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Clustering;

internal static class ClusteringTestData
{
    public static readonly DateTimeOffset Monday = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    public static ClusteringOptions Options(double threshold = 0.22, double maxHoursApart = 96) =>
        new(threshold, TimeSpan.FromHours(maxHoursApart), 300, [new Abbreviation("grand theft auto", "gta"), new Abbreviation("p!nk", "pink")]);

    public static ClusterInput Article(long id, string source, string title, string? summary = null, double hoursAfterMonday = 0) =>
        new(id, source, title, summary, $"https://example.com/{id}", Monday.AddHours(hoursAfterMonday));

    /// <summary>Unrelated filler so week-level IDF behaves like a real week, not a 3-article toy.</summary>
    public static IEnumerable<ClusterInput> Filler(int count, long firstId = 1000) =>
        Enumerable.Range(0, count).Select(index => Article(
            firstId + index,
            $"filler-{index % 5}",
            $"Artist{index} shares single Song{index} ahead of record Record{index}",
            $"Band{index} plays venue{index} in city{index}.",
            hoursAfterMonday: index % 48));
}
