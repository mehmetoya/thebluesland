using Shouldly;
using TheBluesland.SpotifyFetcher.EraReport;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// US-023: pins the suggestion thresholds Mehmet chose (fewer than 10 dated tracks -> no
/// suggestion; >=20% -> suggest that bucket; no bucket reaching 60% -> also suggest mixed-era;
/// one bucket reaching 60% -> do not suggest mixed-era).
/// </summary>
public sealed class PlaylistEraDistributionCalculatorTests
{
    private readonly PlaylistEraDistributionCalculator _calculator = new();

    [Fact]
    public void Calculate_reports_insufficient_data_when_fewer_than_ten_tracks_have_a_readable_year()
    {
        var releaseYears = Enumerable.Repeat(1978, 9).ToArray(); // one below the minimum

        var result = _calculator.Calculate(releaseYears);

        result.HasInsufficientData.ShouldBeTrue();
        result.DatedTrackCount.ShouldBe(9);
        result.SuggestedEras.ShouldBeEmpty();
    }

    [Fact]
    public void Calculate_suggests_only_the_dominant_bucket_and_not_mixed_era_when_one_bucket_reaches_sixty_percent()
    {
        // 7/10 = 70% 1970s (>=60% majority), 3/10 = 30% 2000s-present (>=20% suggestion floor).
        var releaseYears = Enumerable.Repeat(1975, 7).Concat(Enumerable.Repeat(2010, 3)).ToArray();

        var result = _calculator.Calculate(releaseYears);

        result.HasInsufficientData.ShouldBeFalse();
        result.DatedTrackCount.ShouldBe(10);
        result.SuggestedEras.ShouldBe([EraBucketMapper.Seventies, EraBucketMapper.TwoThousandsPresent], ignoreOrder: true);
        result.SuggestedEras.ShouldNotContain(EraBucketMapper.MixedEra);
    }

    [Fact]
    public void Calculate_adds_mixed_era_when_no_single_bucket_reaches_sixty_percent()
    {
        // 4/10 pre-1970, 3/10 1970s, 3/10 1980s-1990s - each >=20%, none >=60%.
        var releaseYears = Enumerable.Repeat(1960, 4)
            .Concat(Enumerable.Repeat(1975, 3))
            .Concat(Enumerable.Repeat(1990, 3))
            .ToArray();

        var result = _calculator.Calculate(releaseYears);

        result.SuggestedEras.ShouldBe(
            [EraBucketMapper.PreSeventies, EraBucketMapper.Seventies, EraBucketMapper.EightiesNineties, EraBucketMapper.MixedEra],
            ignoreOrder: true);
    }

    [Fact]
    public void Calculate_excludes_a_bucket_below_the_twenty_percent_suggestion_floor()
    {
        // 8/10 = 80% 1970s (majority), 2/10 = 20% 2000s-present is exactly at the floor and stays
        // included; a third case below adds a single track that would push a bucket under 20%.
        var releaseYears = Enumerable.Repeat(1975, 9).Concat(Enumerable.Repeat(2010, 1)).ToArray(); // 90%/10%

        var result = _calculator.Calculate(releaseYears);

        result.SuggestedEras.ShouldBe([EraBucketMapper.Seventies]); // the 10% bucket is excluded
        result.SuggestedEras.ShouldNotContain(EraBucketMapper.TwoThousandsPresent);
        result.SuggestedEras.ShouldNotContain(EraBucketMapper.MixedEra); // 90% >= 60% majority
    }

    [Fact]
    public void Calculate_computes_bucket_percentages_relative_to_the_dated_track_count()
    {
        var releaseYears = Enumerable.Repeat(1975, 5).Concat(Enumerable.Repeat(2010, 5)).ToArray();

        var result = _calculator.Calculate(releaseYears);

        result.BucketPercentages[EraBucketMapper.Seventies].ShouldBe(0.5);
        result.BucketPercentages[EraBucketMapper.TwoThousandsPresent].ShouldBe(0.5);
        result.BucketPercentages[EraBucketMapper.PreSeventies].ShouldBe(0.0);
        result.BucketPercentages[EraBucketMapper.EightiesNineties].ShouldBe(0.0);
    }
}
