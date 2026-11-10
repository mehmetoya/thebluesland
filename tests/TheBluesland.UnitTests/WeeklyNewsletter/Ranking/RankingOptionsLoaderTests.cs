using Shouldly;
using TheBluesland.UnitTests.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Ranking;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Ranking;

/// <summary>ranking.yml is the only tuning surface (story-ranking spec, Design section 1).</summary>
public sealed class RankingOptionsLoaderTests
{
    [Fact]
    public async Task LoadAsync_repository_ranking_yml_is_valid_and_ends_with_the_live_type()
    {
        var result = await RankingOptionsLoader.LoadAsync(RepoFiles.Find("tools/weekly-newsletter/ranking.yml"), CancellationToken.None);

        result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors));
        result.Options!.Picks.ShouldBe(5);
        result.Options.Backups.ShouldBe(3);
        result.Options.Types[0].Excluded.ShouldBeTrue();
    }

    [Fact]
    public void Parse_reports_every_problem_together()
    {
        var result = RankingOptionsLoader.Parse(
            """
            picks: 0
            backups: 3
            coverageWeight: 1
            maxPerType: 2
            relatedThreshold: 0.15
            genreKeywords: [Rock]
            genreSwapMinRatio: 0.7
            types:
              - name: obituary
                bonus: 1.5
                phrases: [dies, Passed-Away]
              - name: obituary
                phrases: [dead]
              - name: release
                bonus: 0
                phrases: [song]
            """);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain("picks must be a positive number.");
        result.Errors.ShouldContain(error => error.Contains("'Rock'", StringComparison.Ordinal));
        result.Errors.ShouldContain(error => error.Contains("'Passed-Away'", StringComparison.Ordinal));
        result.Errors.ShouldContain(error => error.Contains("unique name", StringComparison.Ordinal));
        result.Errors.ShouldContain(error => error.Contains("built-in fallback", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_rejects_bad_brand_settings()
    {
        var result = RankingOptionsLoader.Parse(
            """
            picks: 5
            backups: 3
            coverageWeight: 1
            maxPerType: 2
            relatedThreshold: 0.15
            genreKeywords: [rock]
            genreSwapMinRatio: 0.7
            brandBonus: -1
            brandKeywords: [Blues]
            coreSources: [Louder_Sound]
            types:
              - name: obituary
                bonus: 1.5
                phrases: [dies]
            """);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain("brandBonus must be zero or positive.");
        result.Errors.ShouldContain(error => error.Contains("brandKeywords: 'Blues'", StringComparison.Ordinal));
        result.Errors.ShouldContain(error => error.Contains("coreSources: 'Louder_Sound'", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_rejects_unknown_keys()
    {
        RankingOptionsLoader.Parse("pickz: 5").Errors.ShouldHaveSingleItem().ShouldContain("not valid YAML");
    }
}
