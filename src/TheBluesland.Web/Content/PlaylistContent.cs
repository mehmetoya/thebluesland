namespace TheBluesland.Web.Content;

/// <summary>
/// The editorial fields the minimal render surface needs (title, summary, mood/genre/occasion
/// tags, curator note). Full schema/taxonomy validation is US-006's job; this type trusts
/// whatever <see cref="PlaylistContentReader"/> found in the front matter.
/// </summary>
public sealed record PlaylistContent(
    string Slug,
    string SpotifyPlaylistId,
    string Title,
    string Summary,
    IReadOnlyList<string> Moods,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Occasions,
    string CuratorNote,
    bool IsPublished);
