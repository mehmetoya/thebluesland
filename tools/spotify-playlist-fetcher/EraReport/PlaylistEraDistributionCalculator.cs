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
/// - Any bucket at or above <see cref="SuggestionThresholdPercentage"/> is suggested - this floor
///   is the same for every bucket, <c>2000s-present</c> included, and stays untouched by the rule
///   below: dozens of measured playlists (2026-09-09) legitimately carry <c>2000s-present</c> as a
///   genuine secondary tag well under majority (e.g. a 45%/40% split with two eras both present),
///   and raising this floor would silently drop that real signal from all of them.
/// - If no single bucket reaches its <see cref="MajorityThresholds"/> entry, the playlist is
///   genuinely spread across periods, so <c>mixed-era</c> is suggested in addition to whichever
///   buckets already qualified above.
/// - If one bucket does reach that majority, <c>mixed-era</c> is not suggested - one period
///   dominates.
///
/// <para><c>2000s-present</c> carries a raised, 75% majority bar (2026-09-09, Mehmet's decision)
/// rather than the 60% every other bucket uses. The first real measurement that day put several
/// large classic-catalogue playlists (Bluesland 63%, Blues Will Save Us 64%, Songs with Blues in
/// Their Names 61%) just over the original 60% line, which Spotify's `album.release_date` metadata
/// makes an unreliable place to draw it for old catalogues: a reissue or compilation's release date
/// often overwrites a track's true original era, and that effect concentrates in exactly this
/// bucket (a track can only misreport as *later* than it truly is, never earlier). Below 75%, such
/// a playlist now suggests <c>2000s-present, mixed-era</c> together rather than <c>2000s-present</c>
/// alone - it still carries the signal, just without a false claim of exclusive dominance.</para>
/// </summary>
public sealed class PlaylistEraDistributionCalculator
{
    private const int MinimumDatedTrackCount = 10;
    private const double SuggestionThresholdPercentage = 0.20;
    private const double DefaultMajorityThresholdPercentage = 0.60;
    private const double TwoThousandsPresentMajorityThresholdPercentage = 0.75;

    private static readonly IReadOnlyDictionary<string, double> MajorityThresholds =
        EraBucketMapper.ConcreteBuckets.ToDictionary(
            bucket => bucket,
            bucket => bucket == EraBucketMapper.TwoThousandsPresent
                ? TwoThousandsPresentMajorityThresholdPercentage
                : DefaultMajorityThresholdPercentage);

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

        var hasDominantBucket = bucketPercentages.Any(entry => entry.Value >= MajorityThresholds[entry.Key]);
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
