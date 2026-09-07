namespace TheBluesland.SpotifyFetcher.EraReport;

/// <summary>
/// US-023: turns a playlist's readable track release years into a suggested <c>eras</c> value
/// set. This is a curatorial suggestion, not a rule the taxonomy enforces (spec 8.6 taxonomy
/// governance - Mehmet decides which suggestion to accept, through a normal content pull
/// request); the thresholds below are a starting point and can be tuned in this one place if the
/// suggestion turns out too aggressive or too timid once run against the real 85-playlist
/// backlog.
///
/// Rules (all percentages are of the tracks that had a readable release year, not the playlist's
/// total track count):
/// - Fewer than <see cref="MinimumDatedTrackCount"/> dated tracks: no suggestion at all (too
///   little signal - most likely a short or heavily-local-file playlist).
/// - Any bucket at or above <see cref="SuggestionThresholdPercentage"/> is suggested.
/// - If no single bucket reaches <see cref="MajorityThresholdPercentage"/>, the playlist is
///   genuinely spread across periods, so <c>mixed-era</c> is suggested in addition to whichever
///   buckets already qualified above.
/// - If one bucket does reach that majority, <c>mixed-era</c> is not suggested - one period
///   dominates.
/// </summary>
public sealed class PlaylistEraDistributionCalculator
{
    private const int MinimumDatedTrackCount = 10;
    private const double SuggestionThresholdPercentage = 0.20;
    private const double MajorityThresholdPercentage = 0.60;

    public PlaylistEraDistributionResult Calculate(IReadOnlyCollection<int> releaseYears)
    {
        var datedTrackCount = releaseYears.Count;
        if (datedTrackCount < MinimumDatedTrackCount)
        {
            return new PlaylistEraDistributionResult(
                datedTrackCount,
                HasInsufficientData: true,
                BucketPercentages: new Dictionary<string, double>(),
                SuggestedEras: []);
        }

        var bucketCounts = EraBucketMapper.ConcreteBuckets.ToDictionary(bucket => bucket, _ => 0);
        foreach (var year in releaseYears)
        {
            bucketCounts[EraBucketMapper.BucketForYear(year)]++;
        }

        var bucketPercentages = bucketCounts.ToDictionary(
            entry => entry.Key,
            entry => (double)entry.Value / datedTrackCount);

        var suggestedEras = EraBucketMapper.ConcreteBuckets
            .Where(bucket => bucketPercentages[bucket] >= SuggestionThresholdPercentage)
            .ToList();

        var hasDominantBucket = bucketPercentages.Values.Any(percentage => percentage >= MajorityThresholdPercentage);
        if (!hasDominantBucket)
        {
            suggestedEras.Add(EraBucketMapper.MixedEra);
        }

        return new PlaylistEraDistributionResult(
            datedTrackCount,
            HasInsufficientData: false,
            bucketPercentages,
            suggestedEras);
    }
}
