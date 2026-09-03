namespace TheBluesland.Web.Content;

/// <summary>
/// The editorial fields the render surface needs (title, summary, mood/genre/occasion/era tags,
/// curator note, catalogue ordering and featured status). Full schema/taxonomy validation is
/// US-006's job; this type trusts whatever <see cref="PlaylistContentReader"/> found in the front
/// matter. <see cref="Era"/>, <see cref="PublishedAt"/> and <see cref="DisplayOrder"/> were added
/// for US-009's home-page sort/filter; <see cref="PublishedAt"/> stays nullable because draft
/// content may omit it (US-006).
/// </summary>
public sealed record PlaylistContent(
    string Slug,
    string SpotifyPlaylistId,
    string Title,
    string Summary,
    IReadOnlyList<string> Moods,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Occasions,
    string Era,
    string CuratorNote,
    bool IsPublished,
    bool Featured,
    int DisplayOrder,
    DateOnly? PublishedAt);
