using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

public sealed class ZZAnalyzeTaxonomy
{
    [Fact]
    public async Task Dump()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var contentDirectory = Path.Combine(repoRoot, "content", "playlists");

        var reader = new PlaylistContentReader();
        var all = await reader.ReadAllAsync(contentDirectory, CancellationToken.None);
        var published = all.Where(p => p.IsPublished).ToList();

        var outDir = Path.Combine(AppContext.BaseDirectory, "taxonomy-analysis");
        Directory.CreateDirectory(outDir);

        var moodCounts = published.SelectMany(p => p.Moods).GroupBy(x => x).OrderByDescending(g => g.Count());
        var eraCounts = published.GroupBy(p => p.Era).OrderByDescending(g => g.Count());
        var occasionCounts = published.SelectMany(p => p.Occasions).GroupBy(x => x).OrderByDescending(g => g.Count());

        await File.WriteAllLinesAsync(
            Path.Combine(outDir, "taxonomy-distribution.txt"),
            new[] { "=== Mood ===" }
                .Concat(moodCounts.Select(g => $"{g.Count()} {g.Key}"))
                .Concat(new[] { "", "=== Era ===" })
                .Concat(eraCounts.Select(g => $"{g.Count()} {g.Key}"))
                .Concat(new[] { "", "=== Occasion ===" })
                .Concat(occasionCounts.Select(g => $"{g.Count()} {g.Key}")));

        var allText = published
            .OrderBy(p => p.Slug)
            .Select(p =>
                $"--- {p.Slug} | moods=[{string.Join(",", p.Moods)}] era={p.Era} occasions=[{string.Join(",", p.Occasions)}] ---\n" +
                $"SUMMARY: {p.Summary}\n" +
                $"NOTE: {p.CuratorNote}\n");

        await File.WriteAllLinesAsync(Path.Combine(outDir, "all-playlists-taxonomy-scan.txt"), allText);
    }
}
