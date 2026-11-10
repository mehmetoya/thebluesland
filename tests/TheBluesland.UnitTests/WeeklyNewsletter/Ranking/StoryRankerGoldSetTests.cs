using System.Text.Json;
using Shouldly;
using TheBluesland.UnitTests.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Ranking;
using Xunit;
using Xunit.Abstractions;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Ranking;

/// <summary>
/// story-ranking spec, Design section 4: Mehmet's own five for a real week is the reference. With
/// the repository's clustering.yml and ranking.yml, at least four of the algorithm's five picks
/// must be his, and all five of his must be among the algorithm's eight (picks + backups).
/// </summary>
public sealed class StoryRankerGoldSetTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Repository_settings_agree_with_Mehmets_pick_for_2026_W46()
    {
        var clustering = (await ClusteringOptionsLoader.LoadAsync(RepoFiles.Find("tools/weekly-newsletter/clustering.yml"), CancellationToken.None)).Options!;
        var ranking = await RankingOptionsLoader.LoadAsync(RepoFiles.Find("tools/weekly-newsletter/ranking.yml"), CancellationToken.None);
        ranking.IsValid.ShouldBeTrue(string.Join("; ", ranking.Errors));

        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        using var gold = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixtures, "newsletter-ranking", "2026-W46.json")));
        using var week = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixtures, gold.RootElement.GetProperty("articles").GetString()!)));
        var articles = week.RootElement.GetProperty("articles").EnumerateArray()
            .Select(article => new ClusterInput(
                article.GetProperty("id").GetInt64(),
                article.GetProperty("source").GetString()!,
                article.GetProperty("title").GetString()!,
                article.GetProperty("summary").GetString(),
                article.GetProperty("url").GetString()!,
                article.GetProperty("effectiveAt").GetDateTimeOffset()))
            .ToList();
        var mehmet = gold.RootElement.GetProperty("picks").EnumerateArray().Select(pick => pick.GetProperty("articleId").GetInt64()).ToList();

        var result = StoryRanker.Rank(StoryClusterer.Cluster(articles, clustering), ranking.Options!, clustering);

        result.Picks.Concat(result.Backups).Select((pick, index) => $"{(index < 5 ? $"#{index + 1}" : $"B{index - 4}")} {pick.Score:0.00} {pick.Type} {pick.Story.Articles[0].Title}").ToList().ForEach(output.WriteLine);
        var picked = result.Picks.Select(pick => pick.Story.Articles.Select(article => article.Id).ToHashSet()).ToList();
        var shortlist = result.Picks.Concat(result.Backups).Select(pick => pick.Story.Articles.Select(article => article.Id).ToHashSet()).ToList();

        mehmet.Count(id => picked.Any(story => story.Contains(id))).ShouldBeGreaterThanOrEqualTo(4);
        mehmet.ShouldAllBe(id => shortlist.Any(story => story.Contains(id)));
    }
}
