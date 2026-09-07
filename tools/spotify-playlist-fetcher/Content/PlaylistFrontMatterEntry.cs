namespace TheBluesland.SpotifyFetcher.Content;

/// <summary>
/// One <c>content/playlists/*.md</c> file's identity and current era tags, as read by
/// <see cref="PlaylistFrontMatterReader.ReadAllAsync"/>. Used by US-023's era report to label
/// each playlist and show its current <c>eras</c> next to the Spotify-data-derived suggestion.
/// </summary>
public sealed record PlaylistFrontMatterEntry(
    string SpotifyPlaylistId,
    string Slug,
    IReadOnlyList<string> Eras);
