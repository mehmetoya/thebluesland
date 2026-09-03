namespace TheBluesland.Web.Content;

/// <summary>
/// The approved v0.2 editorial taxonomy (spec section 8.2-8.5), used by
/// <see cref="PlaylistContentValidator"/> to reject unapproved mood/genre/occasion/era values.
/// Expanding these lists is a content change, not an architecture change (spec section 8.1) -
/// this class only enforces whatever list is current here; it is not the taxonomy's source of
/// truth.
/// </summary>
internal static class PlaylistTaxonomy
{
    public static readonly IReadOnlyCollection<string> Moods =
    [
        "melancholic",
        "warm",
        "energetic",
        "raw",
        "nostalgic",
    ];

    public static readonly IReadOnlyCollection<string> Genres =
    [
        "blues",
        "blues-rock",
        "rock",
        "soul",
        "jazz",
        "anadolu-rock",
    ];

    public static readonly IReadOnlyCollection<string> Occasions =
    [
        "late-night",
        "night-drive",
        "road-trip",
        "slow-evening",
        "headphones",
    ];

    public static readonly IReadOnlyCollection<string> Eras =
    [
        "pre-1970",
        "1970s",
        "1980s-1990s",
        "2000s-present",
        "mixed-era",
    ];
}
