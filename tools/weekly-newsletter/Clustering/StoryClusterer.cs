namespace TheBluesland.WeeklyNewsletter.Clustering;

/// <summary>
/// Groups a week's articles into stories (story-clustering spec, Design section 2, steps 3–4).
/// Pure and deterministic: input order does not matter, ties are broken by article id.
/// <para>
/// Two articles are linked only if they come from different sources, are at most
/// <see cref="ClusteringOptions.MaxApart"/> apart, and their cosine similarity counts; stories then
/// merge by average linkage while the mean similarity of all their article pairs is at least
/// <see cref="ClusteringOptions.SimilarityThreshold"/>. Average linkage (not single linkage) keeps
/// one shared neighbour from chaining two different events together.
/// </para>
/// </summary>
public static class StoryClusterer
{
    public static IReadOnlyList<Story> Cluster(IReadOnlyList<ClusterInput> input, ClusteringOptions options)
    {
        var articles = input.OrderBy(article => article.Id).ToList();
        var count = articles.Count;
        if (count == 0)
        {
            return [];
        }

        var vectors = TfIdfVectorizer.Vectorize(articles, new TextNormalizer(options.Abbreviations), options.SummaryCharacters);

        // sums[a, b]: sum of pairwise similarities between the members of clusters a and b.
        var sums = new double[count, count];
        for (var i = 0; i < count; i++)
        {
            for (var j = i + 1; j < count; j++)
            {
                if (CanLink(articles[i], articles[j], options.MaxApart))
                {
                    sums[i, j] = sums[j, i] = vectors[i].Dot(vectors[j]);
                }
            }
        }

        // Cluster slot c holds members[c] (null once merged away); slot index = smallest member
        // index, because merges always keep the lower slot.
        var members = Enumerable.Range(0, count).Select(index => new List<int> { index }).ToArray<List<int>?>();

        while (true)
        {
            var best = (Score: double.NegativeInfinity, A: -1, B: -1);
            for (var a = 0; a < count; a++)
            {
                if (members[a] is not { } left)
                {
                    continue;
                }

                for (var b = a + 1; b < count; b++)
                {
                    if (members[b] is not { } right || sums[a, b] <= 0)
                    {
                        continue;
                    }

                    var score = sums[a, b] / (left.Count * right.Count);
                    // Strictly greater: on a tie the earliest (a, b) pair wins, which is deterministic.
                    if (score >= options.SimilarityThreshold && score > best.Score)
                    {
                        best = (score, a, b);
                    }
                }
            }

            if (best.A < 0)
            {
                break;
            }

            var (keep, drop) = (best.A, best.B);
            members[keep]!.AddRange(members[drop]!);
            members[drop] = null;
            for (var c = 0; c < count; c++)
            {
                if (members[c] is not null && c != keep)
                {
                    sums[keep, c] = sums[c, keep] = sums[keep, c] + sums[drop, c];
                }
            }
        }

        return members
            .OfType<List<int>>()
            .Select(indexes => ToStory(indexes.Select(index => articles[index])))
            .OrderByDescending(story => story.SourceCount)
            .ThenBy(story => story.FirstSeen)
            .ThenBy(story => story.Articles.Min(article => article.Id))
            .ToList();
    }

    private static bool CanLink(ClusterInput left, ClusterInput right, TimeSpan maxApart) =>
        !string.Equals(left.Source, right.Source, StringComparison.Ordinal)
        && (left.EffectiveAt - right.EffectiveAt).Duration() <= maxApart;

    private static Story ToStory(IEnumerable<ClusterInput> members)
    {
        var ordered = members.OrderBy(article => article.EffectiveAt).ThenBy(article => article.Id).ToList();
        return new Story(
            $"s-{ordered.Min(article => article.Id)}",
            ordered,
            ordered.Select(article => article.Source).Distinct(StringComparer.Ordinal).ToList());
    }
}
