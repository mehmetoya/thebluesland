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
        // Added 2026-09-05 (Mehmet's approval) when the catalogue widened from a hand-picked
        // blues/rock showcase to cover every public playlist he owns - the original 6 values
        // were sized for 8-15 blues/rock-adjacent playlists (spec 8.1), not a personal library
        // spanning punk, metal, indie, folk, classical, electronic, world and country.
        "punk",
        "metal",
        "indie",
        "folk",
        "funk",
        "country",
        "classical",
        "electronic",
        "world",
        "pop",
    ];

    public static readonly IReadOnlyCollection<string> Occasions =
    [
        "late-night",
        "night-drive",
        "road-trip",
        "slow-evening",
        "headphones",
        // Added 2026-09-06 (US-017, Mehmet's approval) after US-020's content-analysis pass over
        // all 120 playlists found no gap in mood or era but two occasion themes that recur in
        // curator note prose without fitting any existing value: background/concentration
        // listening and dance/party listening.
        "focus",
        "dancing",
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
