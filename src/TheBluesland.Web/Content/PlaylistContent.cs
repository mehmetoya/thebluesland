namespace TheBluesland.Web.Content;

/// <summary>
/// The editorial fields the render surface needs (title, summary, mood/genre/occasion/era tags,
/// curator note, catalogue ordering and featured priority). Full schema/taxonomy validation is
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
///
/// <see cref="FeaturedOrder"/> replaced a plain <c>bool Featured</c> on 2026-09-10
/// (docs/specs/catalogue-priority-and-follower-sort.md): the bool was parsed and validated but
/// never actually consumed by any sort or render code, and Mehmet wanted an explicit, hand-picked
/// priority order for the home page rather than an unordered flag. Null means not featured.
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
    int? FeaturedOrder,
    int DisplayOrder,
    DateOnly? PublishedAt,
    IReadOnlyList<string> PreviousSlugs)
{
    /// <summary>The marker line that starts the optional Turkish curator note in a playlist file's body.</summary>
    public const string TurkishNoteMarker = "<!-- lang:tr -->";

    /// <summary>Optional Turkish title/summary/curator note (docs/specs/site-i18n.md, "Content fallback").
    /// Nothing sets them today: all editorial content is English, so every Turkish page falls back.</summary>
    public string? TitleTr { get; init; }

    public string? SummaryTr { get; init; }

    public string? CuratorNoteTr { get; init; }

    /// <summary>
    /// The title to show on a page in <paramref name="languageCode"/> and the <c>lang</c> attribute its
    /// element needs: null when the text already matches the page language, "en" when a Turkish page
    /// shows the English original because no Turkish text exists.
    /// </summary>
    public (string Text, string? Lang) TitleIn(string languageCode) => Pick(languageCode, Title, TitleTr);

    public (string Text, string? Lang) SummaryIn(string languageCode) => Pick(languageCode, Summary, SummaryTr);

    public (string Text, string? Lang) CuratorNoteIn(string languageCode) => Pick(languageCode, CuratorNote, CuratorNoteTr);

    private static (string Text, string? Lang) Pick(string languageCode, string english, string? turkish) =>
        languageCode != "tr" ? (english, null)
        : string.IsNullOrWhiteSpace(turkish) ? (english, "en")
        : (turkish, null);
}
