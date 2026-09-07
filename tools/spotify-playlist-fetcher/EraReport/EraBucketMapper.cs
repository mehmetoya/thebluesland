namespace TheBluesland.SpotifyFetcher.EraReport;

/// <summary>
/// Maps a Spotify track release year to one of the four "concrete" era buckets from
/// <c>src/TheBluesland.Web/Content/PlaylistTaxonomy.cs</c>'s <c>Eras</c> list. <c>mixed-era</c>,
/// the taxonomy's fifth value, is intentionally not a year bucket here - it is a distribution
/// outcome (see <see cref="PlaylistEraDistributionCalculator"/>), not something a single release
/// year maps to. This tool has no project reference to <c>TheBluesland.Web</c> (spec 8.6:
/// taxonomy ownership stays with the web project's validator); if that taxonomy's era values
/// ever change, these bucket names must be updated to match by hand.
/// </summary>
public static class EraBucketMapper
{
    public const string PreSeventies = "pre-1970";
    public const string Seventies = "1970s";
    public const string EightiesNineties = "1980s-1990s";
    public const string TwoThousandsPresent = "2000s-present";
    public const string MixedEra = "mixed-era";

    public static readonly IReadOnlyList<string> ConcreteBuckets =
    [
        PreSeventies,
        Seventies,
        EightiesNineties,
        TwoThousandsPresent,
    ];

    public static string BucketForYear(int year) => year switch
    {
        < 1970 => PreSeventies,
        <= 1979 => Seventies,
        <= 1999 => EightiesNineties,
        _ => TwoThousandsPresent,
    };
}
