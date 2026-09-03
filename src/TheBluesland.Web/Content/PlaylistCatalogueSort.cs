namespace TheBluesland.Web.Content;

/// <summary>
/// Sorts the published catalogue for the home page (US-009 AC1, spec FR-001: "ordered by
/// displayOrder, then by publishedAt descending"). Spec section 9.2 leaves displayOrder's
/// direction unstated ("integer, defaults to 0" - no direction given), so this resolves the
/// ambiguity as ascending: lower values are the conventional meaning of a manually curated
/// "position" field (position 0 shown first), matching how displayOrder is used elsewhere as an
/// editorial ordering knob rather than a ranking score. publishedAt descending is then the
/// tiebreaker for playlists that share a displayOrder (e.g. everything left at the default 0) -
/// newest first - rather than the primary key. A pure function (no I/O) so it is directly
/// unit-testable independent of <see cref="PlaylistContentRepository"/>.
/// </summary>
public static class PlaylistCatalogueSort
{
    public static IReadOnlyList<PlaylistContent> Apply(IReadOnlyList<PlaylistContent> playlists) =>
        playlists
            .OrderBy(playlist => playlist.DisplayOrder)
            .ThenByDescending(playlist => playlist.PublishedAt)
            .ToList();
}
