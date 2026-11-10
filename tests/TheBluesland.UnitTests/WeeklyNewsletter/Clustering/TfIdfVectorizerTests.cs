using Shouldly;
using TheBluesland.WeeklyNewsletter.Clustering;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Clustering.ClusteringTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Clustering;

/// <summary>story-clustering spec, Design section 2, step 2.</summary>
public sealed class TfIdfVectorizerTests
{
    private static readonly TextNormalizer Normalizer = new([]);

    [Fact]
    public void Vectorize_weighs_a_rare_term_above_one_that_is_in_every_article()
    {
        var vectors = TfIdfVectorizer.Vectorize(
            [Article(1, "a", "tour sepultura"), Article(2, "b", "tour oasis"), Article(3, "c", "tour rush")],
            Normalizer,
            300);

        // Term ids follow first appearance: 0 = "tour" (df 3), 1 = "sepultura" (df 1).
        var weights = vectors[0].Terms.Zip(vectors[0].Weights).ToDictionary();
        weights[1].ShouldBeGreaterThan(weights[0]);
        // ln((1 + 3) / (1 + 3) + 1) = ln 2: a term in every article still counts, but little.
        (weights[0] / weights[1]).ShouldBe(Math.Log(2) / Math.Log((4.0 / 2) + 1), tolerance: 1e-9);
    }

    [Fact]
    public void Vectorize_returns_unit_length_vectors_and_counts_the_title_twice()
    {
        var vectors = TfIdfVectorizer.Vectorize([Article(1, "a", "oasis", "rush"), Article(2, "b", "other")], Normalizer, 300);

        var weights = vectors[0].Weights;
        Math.Sqrt(weights.Sum(weight => weight * weight)).ShouldBe(1, tolerance: 1e-9);
        // Same df for "oasis" and "rush"; the title term has tf 2, so (1 + ln 2) times the weight.
        (weights[0] / weights[1]).ShouldBe(1 + Math.Log(2), tolerance: 1e-9);
    }

    [Fact]
    public void Vectorize_uses_only_the_configured_summary_prefix_and_tolerates_no_summary()
    {
        var vectors = TfIdfVectorizer.Vectorize([Article(1, "a", "oasis", "rush hidden"), Article(2, "b", "oasis", null)], Normalizer, 4);

        vectors[0].Terms.Length.ShouldBe(2); // "oasis" + "rush"; "hidden" is past 4 characters
        vectors[1].Terms.ShouldHaveSingleItem();
        vectors[0].Dot(vectors[1]).ShouldBeGreaterThan(0);
    }
}
