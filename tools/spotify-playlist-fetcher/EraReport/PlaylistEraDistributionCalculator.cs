namespace TheBluesland.SpotifyFetcher.EraReport;

/// <summary>
/// US-023: turns readable release years into playlist-level era tags. The report displays
/// these tags as suggestions; monthly sync persists them for automatic assignment to playlists
/// whose editorial era is only mixed-era (owner decision, 2026-09-07).
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

    /// <summary>
    /// Counts the release years into buckets and applies the rules above. The years are consumed
    /// here and never leave the call (spec 9.4/11.2).
    /// </summary>
    public PlaylistEraDistributionResult Calculate(IReadOnlyCollection<int> releaseYears)
    {
        var bucketCounts = EraBucketMapper.ConcreteBuckets.ToDictionary(bucket => bucket, _ => 0);
        foreach (var year in releaseYears)
        {
            bucketCounts[EraBucketMapper.BucketForYear(year)]++;
        }

        return CalculateFromBucketCounts(bucketCounts);
    }

    /// <summary>
    /// Applies the rules to counts that were measured earlier - the form persisted in
    /// <c>spotify_playlist_cache</c>. Reading tracks from Spotify is by far the most expensive
    /// thing this project does, so once a playlist has been counted, every later question about
    /// thresholds is answered from these four numbers instead of from another crawl.
    /// </summary>
    public PlaylistEraDistributionResult CalculateFromBucketCounts(IReadOnlyDictionary<string, int> bucketCounts)
    {
        var datedTrackCount = bucketCounts.Values.Sum();
        if (datedTrackCount < MinimumDatedTrackCount)
        {
            return new PlaylistEraDistributionResult(
                datedTrackCount,
                HasInsufficientData: true,
                BucketCounts: bucketCounts,
                BucketPercentages: new Dictionary<string, double>(),
                SuggestedEras: []);
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
            bucketCounts,
            bucketPercentages,
            suggestedEras);
    }
}
