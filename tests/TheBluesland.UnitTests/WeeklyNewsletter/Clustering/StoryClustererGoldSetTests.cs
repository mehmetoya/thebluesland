using System.Text.Json;
using Shouldly;
using TheBluesland.WeeklyNewsletter.Clustering;
using Xunit;
using Xunit.Abstractions;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Clustering;

/// <summary>
/// story-clustering spec, Design section 3: the method is judged on a real, hand-labelled week.
/// Pairwise precision ≥ 0.95 and recall ≥ 0.80 with the repository's clustering.yml. A calibration
/// change that lowers either fails this test.
/// </summary>
public sealed class StoryClustererGoldSetTests(ITestOutputHelper output)
{
    private const double MinPrecision = 0.95;
    private const double MinRecall = 0.80;

    [Theory]
    [InlineData("2026-W46.json")]
    public async Task Repository_settings_meet_the_precision_and_recall_bar(string goldSet)
    {
        var options = (await ClusteringOptionsLoader.LoadAsync(RepoFiles.Find("tools/weekly-newsletter/clustering.yml"), CancellationToken.None)).Options!;
        var gold = await LoadAsync(goldSet);

        var stories = StoryClusterer.Cluster(gold.Articles.Select(article => article.Input).ToList(), options);
        var (precision, recall, errors) = Score(gold, stories);

        output.WriteLine($"{goldSet}: precision {precision:0.000}, recall {recall:0.000}");
        errors.ForEach(output.WriteLine);
        precision.ShouldBeGreaterThanOrEqualTo(MinPrecision, string.Join('\n', errors));
        recall.ShouldBeGreaterThanOrEqualTo(MinRecall, string.Join('\n', errors));
    }

    [Fact]
    public async Task Traps_in_the_gold_set_stay_separate()
    {
        var options = (await ClusteringOptionsLoader.LoadAsync(RepoFiles.Find("tools/weekly-newsletter/clustering.yml"), CancellationToken.None)).Options!;
        var gold = await LoadAsync("2026-W46.json");

        var stories = StoryClusterer.Cluster(gold.Articles.Select(article => article.Input).ToList(), options);
        var storyOf = stories.SelectMany(story => story.Articles.Select(article => (article.Id, story.Key))).ToDictionary();

        // McCartney billionaire (6) vs McCartney & Ringo (41, 67); Pink Floyd (38) vs P!NK (43, 103);
        // two different Taylor Swift stories (10, 81); wah-wah list vs wah-wah inventor (3, 8).
        storyOf[6].ShouldNotBe(storyOf[41]);
        storyOf[38].ShouldNotBe(storyOf[43]);
        storyOf[10].ShouldNotBe(storyOf[81]);
        storyOf[3].ShouldNotBe(storyOf[8]);
    }

    private static (double Precision, double Recall, List<string> Errors) Score(GoldSet gold, IReadOnlyList<Story> stories)
    {
        var storyOf = stories.SelectMany(story => story.Articles.Select(article => (article.Id, story.Key))).ToDictionary();
        int truePositives = 0, falsePositives = 0, falseNegatives = 0;
        var errors = new List<string>();
        var articles = gold.Articles;

        for (var i = 0; i < articles.Count; i++)
        {
            for (var j = i + 1; j < articles.Count; j++)
            {
                var (left, right) = (articles[i], articles[j]);
                if (gold.IsAmbiguous(left.Story, right.Story))
                {
                    continue;
                }

                var expected = left.Story is not null && left.Story == right.Story;
                var actual = storyOf[left.Input.Id] == storyOf[right.Input.Id];
                if (expected && actual)
                {
                    truePositives++;
                }
                else if (actual)
                {
                    falsePositives++;
                    errors.Add($"wrongly merged: {left.Input.Title} | {right.Input.Title}");
                }
                else if (expected)
                {
                    falseNegatives++;
                    errors.Add($"missed: {left.Input.Title} | {right.Input.Title}");
                }
            }
        }

        var precision = truePositives + falsePositives == 0 ? 1 : (double)truePositives / (truePositives + falsePositives);
        var recall = truePositives + falseNegatives == 0 ? 1 : (double)truePositives / (truePositives + falseNegatives);
        return (precision, recall, errors);
    }

    private static async Task<GoldSet> LoadAsync(string name)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "newsletter-clustering", name));
        var document = await JsonSerializer.DeserializeAsync<GoldDocument>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException(name);
        return new GoldSet(
            document.Articles.Select(article => new GoldArticle(
                new ClusterInput(article.Id, article.Source, article.Title, article.Summary, article.Url, article.EffectiveAt),
                article.Story)).ToList(),
            document.AmbiguousGroups.Select(group => group.ToHashSet(StringComparer.Ordinal)).ToList());
    }

    private sealed record GoldSet(List<GoldArticle> Articles, List<HashSet<string>> AmbiguousGroups)
    {
        public bool IsAmbiguous(string? left, string? right) =>
            left is not null && right is not null && left != right
            && AmbiguousGroups.Any(group => group.Contains(left) && group.Contains(right));
    }

    private sealed record GoldArticle(ClusterInput Input, string? Story);

    private sealed record GoldDocument(List<GoldDocumentArticle> Articles, List<List<string>> AmbiguousGroups);

    private sealed record GoldDocumentArticle(long Id, string Source, string Title, string? Summary, string Url, DateTimeOffset EffectiveAt, string? Story);
}
