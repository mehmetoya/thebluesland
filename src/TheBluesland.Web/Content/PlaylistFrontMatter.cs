namespace TheBluesland.Web.Content;

/// <summary>
/// The Markdown front-matter fields <see cref="PlaylistContentReader"/> maps. This is a wider
/// subset than <c>tools/spotify-playlist-fetcher</c>'s own PlaylistFrontMatter (which only needs
/// spotifyPlaylistId), because the web render surface additionally needs title/summary/tags/
/// curator note. The two are not shared: per docs/adr/0003-mimari-kapsam.md there is no real
/// dependency boundary to justify sharing a DTO this small. Full schema/taxonomy validation
/// (required fields, value ranges, approved taxonomy lists) is US-006's job, not this reader's.
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
    public string? Status { get; set; }
}
