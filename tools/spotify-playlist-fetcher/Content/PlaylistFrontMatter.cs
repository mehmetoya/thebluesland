namespace TheBluesland.SpotifyFetcher.Content;

/// <summary>
/// The narrow subset of Markdown front-matter fields the sync tool needs. Other editorial fields
/// (title, moods, genres, curator note body, ...) are intentionally not mapped here - full
/// front-matter validation belongs to content validation (US-006). YamlDotNet is configured to
/// ignore any front-matter field not declared below.
/// </summary>
public sealed class PlaylistFrontMatter
{
    public string? SpotifyPlaylistId { get; set; }
}
