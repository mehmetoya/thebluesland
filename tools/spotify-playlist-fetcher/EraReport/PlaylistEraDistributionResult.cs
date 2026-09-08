namespace TheBluesland.SpotifyFetcher.EraReport;

/// <summary>
/// Output of <see cref="PlaylistEraDistributionCalculator.Calculate"/> for a single playlist.
/// Deliberately carries only aggregate numbers (a track count and per-bucket percentages) - never
/// a single release year, track title or track id - so nothing that reaches a report or console
/// line can violate spec section 9.4/11.2's "no track-level data" limit.
/// </summary>
/// <param name="DatedTrackCount">Tracks that had a readable release year; the denominator.</param>
/// <param name="HasInsufficientData">True when there was too little signal to suggest anything.</param>
/// <param name="BucketCounts">
/// Dated tracks per concrete bucket. This is the durable form of the measurement: percentages and
/// suggestions are both derived from it, so storing these counts lets any later threshold change be
/// recomputed without reading a single track from Spotify again (see
/// <see cref="PlaylistEraDistributionCalculator.CalculateFromBucketCounts"/>).
/// </param>
/// <param name="BucketPercentages">The same counts as shares of <paramref name="DatedTrackCount"/>.</param>
/// <param name="SuggestedEras">Buckets that cleared the thresholds, plus mixed-era where it applies.</param>
public sealed record PlaylistEraDistributionResult(
    int DatedTrackCount,
    bool HasInsufficientData,
    IReadOnlyDictionary<string, int> BucketCounts,
    IReadOnlyDictionary<string, double> BucketPercentages,
    IReadOnlyList<string> SuggestedEras);
