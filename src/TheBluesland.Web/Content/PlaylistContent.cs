namespace TheBluesland.Web.Content;

/// <summary>
/// The editorial fields the render surface needs (title, summary, mood/genre/occasion/era tags,
/// curator note, catalogue ordering and featured status). Full schema/taxonomy validation is
/// US-006's job; this type trusts whatever <see cref="PlaylistContentReader"/> found in the front
/// matter. <see cref="Eras"/>, <see cref="PublishedAt"/> and <see cref="DisplayOrder"/> were added
/// for US-009's home-page sort/filter; <see cref="PublishedAt"/> stays nullable because draft
/// content may omit it (US-006). <see cref="PreviousSlugs"/> was added for US-010 AC5/FR-020: old
/// slugs a visitor might still land on, resolved to a permanent redirect to <see cref="Slug"/> by
/// <see cref="PlaylistContentRepository.FindByPreviousSlugAsync"/>.
///
/// <see cref="Eras"/> became a list on 2026-09-06 (was a single <c>string Era</c>). 94 of the 120
/// published playlists were tagged <c>mixed-era</c>, which made the era filter nearly useless: most
/// of this catalogue is genuinely multi-decade by design ("five decades", "genre archive"), so the
/// fix was never more era *values* but letting one playlist carry the specific decades it actually
/// spans alongside <c>mixed-era</c>. Now shaped exactly like Moods/Genres/Occasions, which also let
/// the validator drop its bespoke single-value era branch for the shared array path.
/// </summary>
public sealed record PlaylistContent(
    string Slug,
    string SpotifyPlaylistId,
    string Title,
    string Summary,
    IReadOnlyList<string> Moods,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Occasions,
    IReadOnlyList<string> Eras,
    string CuratorNote,
    bool IsPublished,
    bool Featured,
    int DisplayOrder,
    DateOnly? PublishedAt,
    IReadOnlyList<string> PreviousSlugs);
