namespace TheBluesland.SpotifyFetcher.Content;

/// <summary>
/// The narrow subset of Markdown front-matter fields the sync tool needs. Other editorial fields
/// (title, moods, genres, curator note body, ...) are intentionally not mapped here - full
/// front-matter validation belongs to content validation (US-006). YamlDotNet is configured to
/// ignore any front-matter field not declared below.
///
/// <see cref="Slug"/> and <see cref="Eras"/> exist only for US-023's read-only era report (to
/// label each playlist in the report and show the current vs. suggested <c>eras</c> side by
/// side) - this tool never writes either field back to the file.
///
/// <see cref="Status"/> exists for the auto-unpublish-private-playlists sync check: it is read
/// here, but any write-back goes through <see cref="PlaylistFrontMatterWriter"/>'s targeted line
/// replace, never through re-serializing this type.
/// </summary>
public sealed class PlaylistFrontMatter
{
    public string? SpotifyPlaylistId { get; set; }

    public string? Slug { get; set; }

    public List<string>? Eras { get; set; }

    public string? Status { get; set; }
}
