namespace TheBluesland.SpotifyFetcher.EraReport;

/// <summary>
/// Output of <see cref="PlaylistEraDistributionCalculator.Calculate"/> for a single playlist.
/// Deliberately carries only aggregate numbers (a track count and per-bucket percentages) - never
/// a single release year, track title or track id - so nothing that reaches a report or console
/// line can violate spec section 9.4/11.2's "no track-level data" limit.
/// </summary>
public sealed record PlaylistEraDistributionResult(
    int DatedTrackCount,
    bool HasInsufficientData,
    IReadOnlyDictionary<string, double> BucketPercentages,
    IReadOnlyList<string> SuggestedEras);
