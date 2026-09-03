namespace TheBluesland.Web.Content;

/// <summary>
/// The Markdown front-matter fields <see cref="PlaylistContentReader"/> maps. This is a wider
/// subset than <c>tools/spotify-playlist-fetcher</c>'s own PlaylistFrontMatter (which only needs
/// spotifyPlaylistId), because the web render surface additionally needs title/summary/tags/
/// curator note/era/publishedAt/displayOrder/featured (US-009 widened this beyond US-005's
/// original render-only set: sorting and filtering on the home page need them). <c>previousSlugs</c>
/// was added for US-010 AC5/FR-020 (permanent redirects from an old slug). The two are not
/// shared: per docs/adr/0003-mimari-kapsam.md there is no real dependency boundary to justify
/// sharing a DTO this small. <c>schemaVersion</c> stays validation-only (<see
/// cref="PlaylistValidationFrontMatter"/>) - the render path never needs it. Full schema/taxonomy
/// validation (required fields, value ranges, approved taxonomy lists) is US-006's job, not this
/// reader's.
/// </summary>
public sealed class PlaylistFrontMatter
{
    public string? Slug { get; set; }
    public string? SpotifyPlaylistId { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string[]? Moods { get; set; }
    public string[]? Genres { get; set; }
    public string[]? Occasions { get; set; }
    public string? Era { get; set; }
    public string? PublishedAt { get; set; }
    public bool? Featured { get; set; }
    public int? DisplayOrder { get; set; }
    public string? Status { get; set; }
    public string[]? PreviousSlugs { get; set; }
}
