namespace TheBluesland.WeeklyNewsletter.Clustering;

/// <summary>
/// Weekly TF-IDF over single words (story-clustering spec, Design section 2, step 2). The headline
/// counts twice, then the first <see cref="ClusteringOptions.SummaryCharacters"/> of the summary.
/// IDF comes from the same week, so words that are everywhere this week ("tour", "album") weigh
/// little. Two-word phrases were measured and dropped: they lowered recall on paraphrased
/// headlines (2026-W46: recall 0.96 → 0.76 at the same precision).
/// </summary>
public static class TfIdfVectorizer
{
    /// <summary>Unit-length sparse vectors, one per input, in input order.</summary>
    public static IReadOnlyList<SparseVector> Vectorize(
        IReadOnlyList<ClusterInput> articles,
        TextNormalizer normalizer,
        int summaryCharacters)
    {
        var termIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var documents = new List<Dictionary<int, int>>(articles.Count);
        var documentFrequency = new List<int>();

        foreach (var article in articles)
        {
            var title = normalizer.Tokenize(article.Title);
            var summary = normalizer.Tokenize(Truncate(article.Summary, summaryCharacters));
            var counts = new Dictionary<int, int>();
            foreach (var token in title.Concat(title).Concat(summary))
            {
                if (!termIds.TryGetValue(token, out var id))
                {
                    id = termIds.Count;
                    termIds.Add(token, id);
                    documentFrequency.Add(0);
                }

                counts[id] = counts.GetValueOrDefault(id) + 1;
            }

            foreach (var id in counts.Keys)
            {
                documentFrequency[id]++;
            }

            documents.Add(counts);
        }

        var total = articles.Count;
        return documents
            .Select(counts =>
            {
                var terms = counts.Keys.Order().ToArray();
                var weights = terms
                    .Select(id => (1 + Math.Log(counts[id])) * Math.Log(((1.0 + total) / (1.0 + documentFrequency[id])) + 1))
                    .ToArray();
                var length = Math.Sqrt(weights.Sum(weight => weight * weight));
                if (length > 0)
                {
                    for (var i = 0; i < weights.Length; i++)
                    {
                        weights[i] /= length;
                    }
                }

                return new SparseVector(terms, weights);
            })
            .ToList();
    }

    private static string? Truncate(string? text, int characters) =>
        text is null || text.Length <= characters ? text : text[..characters];
}

/// <summary>Term ids ascending, with matching weights.</summary>
public sealed record SparseVector(int[] Terms, double[] Weights)
{
    public double Dot(SparseVector other)
    {
        double sum = 0;
        int i = 0, j = 0;
        while (i < Terms.Length && j < other.Terms.Length)
        {
            if (Terms[i] == other.Terms[j])
            {
                sum += Weights[i++] * other.Weights[j++];
            }
            else if (Terms[i] < other.Terms[j])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return sum;
    }
}
