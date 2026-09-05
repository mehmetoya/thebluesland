using TheBluesland.Web.Content;

if (args.Length != 2 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: playlist-taxonomy-report <content-directory> <output-directory>");
    return 1;
}

var contentDirectory = Path.GetFullPath(args[0]);
var outDir = Path.GetFullPath(args[1]);
Directory.CreateDirectory(outDir);
var reader = new PlaylistContentReader();
var all = await reader.ReadAllAsync(contentDirectory, CancellationToken.None);
var published = all.Where(p => p.IsPublished).ToList();

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
Console.WriteLine($"Reports written to {outDir}");
return 0;
