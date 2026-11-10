using System.Globalization;
using TheBluesland.WeeklyNewsletter.Clustering;

namespace TheBluesland.WeeklyNewsletter.Ranking;

/// <summary>A story with its score and the reasons behind it.</summary>
public sealed record RankedStory(Story Story, double Score, string Type, double Bonus, bool GenreMatch);

/// <summary>A story passed over by a diversity rule, with the rule that skipped it.</summary>
public sealed record SkippedStory(RankedStory Story, string Reason);

public sealed record RankingResult(
    IReadOnlyList<RankedStory> Picks,
    IReadOnlyList<RankedStory> Backups,
    IReadOnlyList<SkippedStory> Skipped,
    string GenreSlot);

/// <summary>
/// Chooses the issue's stories (story-ranking spec, Design sections 1–2). Pure and deterministic.
/// Score = distinct sources × coverage weight + type bonus; ties go to more articles, then the
/// more recent story, then the key. Picks are taken greedily in score order, skipping a story when
/// its type already has <see cref="RankingOptions.MaxPerType"/> picks or when it is related
/// (TF-IDF centroid cosine ≥ <see cref="RankingOptions.RelatedThreshold"/>) to a story already
/// chosen. If no pick matches a genre keyword, the best genre story replaces the last pick when its
/// score is at least <see cref="RankingOptions.GenreSwapMinRatio"/> of that pick's. Backups follow
/// with the same rules among themselves.
/// </summary>
public static class StoryRanker
{
    public static RankingResult Rank(IReadOnlyList<Story> stories, RankingOptions ranking, ClusteringOptions clustering)
    {
        var centroids = Centroids(stories, clustering);
        var skipped = new List<SkippedStory>();
        var candidates = new List<RankedStory>();

        foreach (var story in stories)
        {
            var type = StoryTypeClassifier.Classify(story, ranking.Types);
            var ranked = new RankedStory(
                story,
                (story.SourceCount * ranking.CoverageWeight) + type.Bonus,
                type.Name,
                type.Bonus,
                StoryTypeClassifier.MatchesGenre(story, ranking.GenreKeywords));
            if (type.Excluded)
            {
                skipped.Add(new SkippedStory(ranked, $"{type.Name}: not a news story"));
            }
            else
            {
                candidates.Add(ranked);
            }
        }

        candidates = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Story.Articles.Count)
            .ThenByDescending(candidate => candidate.Story.LastSeen)
            .ThenBy(candidate => candidate.Story.Key, StringComparer.Ordinal)
            .ToList();

        var picks = Select(candidates, [], ranking.Picks, ranking, centroids, skipped);
        var genreSlot = ApplyGenreSlot(picks, candidates, ranking, centroids);
        var backups = Select(candidates, picks, ranking.Backups, ranking, centroids, skipped);

        return new RankingResult(picks, backups, skipped, genreSlot);
    }

    private static List<RankedStory> Select(
        List<RankedStory> candidates,
        List<RankedStory> alreadyChosen,
        int count,
        RankingOptions ranking,
        Dictionary<string, Dictionary<int, double>> centroids,
        List<SkippedStory> skipped)
    {
        var chosen = new List<RankedStory>();
        foreach (var candidate in candidates)
        {
            if (chosen.Count == count)
            {
                break;
            }

            if (alreadyChosen.Contains(candidate))
            {
                continue;
            }

            if (chosen.Count(story => story.Type == candidate.Type) >= ranking.MaxPerType)
            {
                skipped.Add(new SkippedStory(candidate, $"{candidate.Type}: already {ranking.MaxPerType} chosen"));
                continue;
            }

            var related = alreadyChosen.Concat(chosen)
                .FirstOrDefault(story => Cosine(centroids[story.Story.Key], centroids[candidate.Story.Key]) >= ranking.RelatedThreshold);
            if (related is not null)
            {
                skipped.Add(new SkippedStory(candidate, $"related to “{related.Story.Articles[0].Title}”"));
                continue;
            }

            chosen.Add(candidate);
        }

        return chosen;
    }

    private static string ApplyGenreSlot(
        List<RankedStory> picks,
        List<RankedStory> candidates,
        RankingOptions ranking,
        Dictionary<string, Dictionary<int, double>> centroids)
    {
        var matching = picks.Where(pick => pick.GenreMatch).ToList();
        if (matching.Count > 0)
        {
            return $"not needed ({string.Join(", ", matching.Select(pick => pick.Story.Articles[0].Title))})";
        }

        if (picks.Count < ranking.Picks)
        {
            return "not applied (fewer stories than picks)";
        }

        var last = picks[^1];
        var others = picks.Take(picks.Count - 1).ToList();
        var replacement = candidates.FirstOrDefault(candidate =>
            candidate.GenreMatch
            && !picks.Contains(candidate)
            && others.Count(pick => pick.Type == candidate.Type) < ranking.MaxPerType
            && others.All(pick => Cosine(centroids[pick.Story.Key], centroids[candidate.Story.Key]) < ranking.RelatedThreshold));

        if (replacement is null)
        {
            return "no genre story available";
        }

        if (replacement.Score < ranking.GenreSwapMinRatio * last.Score)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"kept: best genre story “{replacement.Story.Articles[0].Title}” scored {replacement.Score:0.00}, below {ranking.GenreSwapMinRatio * 100:0}% of #{picks.Count}");
        }

        picks[^1] = replacement;
        return $"swapped in “{replacement.Story.Articles[0].Title}” for “{last.Story.Articles[0].Title}”";
    }

    /// <summary>Unit-length sum of each story's article vectors, vectorized over the whole window.</summary>
    private static Dictionary<string, Dictionary<int, double>> Centroids(IReadOnlyList<Story> stories, ClusteringOptions clustering)
    {
        var articles = stories.SelectMany(story => story.Articles).OrderBy(article => article.Id).ToList();
        var vectors = TfIdfVectorizer.Vectorize(articles, new TextNormalizer(clustering.Abbreviations), clustering.SummaryCharacters);
        var byId = articles.Select((article, index) => (article.Id, Vector: vectors[index])).ToDictionary(pair => pair.Id, pair => pair.Vector);

        var centroids = new Dictionary<string, Dictionary<int, double>>(StringComparer.Ordinal);
        foreach (var story in stories)
        {
            var sum = new Dictionary<int, double>();
            foreach (var article in story.Articles.OrderBy(article => article.Id))
            {
                var vector = byId[article.Id];
                for (var i = 0; i < vector.Terms.Length; i++)
                {
                    sum[vector.Terms[i]] = sum.GetValueOrDefault(vector.Terms[i]) + vector.Weights[i];
                }
            }

            var length = Math.Sqrt(sum.Values.Sum(weight => weight * weight));
            centroids[story.Key] = length == 0 ? sum : sum.ToDictionary(pair => pair.Key, pair => pair.Value / length);
        }

        return centroids;
    }

    private static double Cosine(Dictionary<int, double> left, Dictionary<int, double> right)
    {
        var (small, large) = left.Count <= right.Count ? (left, right) : (right, left);
        return small.Sum(pair => pair.Value * large.GetValueOrDefault(pair.Key));
    }
}
