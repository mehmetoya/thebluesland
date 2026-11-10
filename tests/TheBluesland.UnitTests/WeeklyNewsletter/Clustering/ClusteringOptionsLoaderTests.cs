using Shouldly;
using TheBluesland.WeeklyNewsletter.Clustering;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Clustering;

/// <summary>clustering.yml is the only calibration surface (story-clustering spec, Design section 2, step 5).</summary>
public sealed class ClusteringOptionsLoaderTests
{
    [Fact]
    public void Parse_reads_every_setting()
    {
        var result = ClusteringOptionsLoader.Parse(
            """
            similarityThreshold: 0.3
            maxHoursApart: 72
            summaryCharacters: 200
            abbreviations:
              - from: grand theft auto
                to: gta
            """);

        result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors));
        result.Options!.SimilarityThreshold.ShouldBe(0.3);
        result.Options.MaxApart.ShouldBe(TimeSpan.FromHours(72));
        result.Options.SummaryCharacters.ShouldBe(200);
        result.Options.Abbreviations.ShouldBe([new Abbreviation("grand theft auto", "gta")]);
    }

    [Fact]
    public void Parse_reports_every_invalid_value_together()
    {
        var result = ClusteringOptionsLoader.Parse(
            """
            similarityThreshold: 1.5
            maxHoursApart: 0
            summaryCharacters: -1
            abbreviations:
              - from: Grand Theft Auto
                to: gta
              - from: p!nk
            """);

        result.IsValid.ShouldBeFalse();
        result.Errors.Count.ShouldBe(5);
    }

    [Fact]
    public void Parse_rejects_unknown_keys_so_typos_cannot_fall_back_to_defaults()
    {
        var result = ClusteringOptionsLoader.Parse("similarityTreshold: 0.2\nmaxHoursApart: 96\nsummaryCharacters: 300\n");

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ShouldContain("not valid YAML");
    }

    [Fact]
    public void Parse_reports_missing_settings()
    {
        ClusteringOptionsLoader.Parse(string.Empty).Errors.Count.ShouldBe(3);
    }

    [Fact]
    public async Task LoadAsync_repository_clustering_yml_is_valid()
    {
        var result = await ClusteringOptionsLoader.LoadAsync(RepoFiles.Find("tools/weekly-newsletter/clustering.yml"), CancellationToken.None);

        result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors));
    }
}
