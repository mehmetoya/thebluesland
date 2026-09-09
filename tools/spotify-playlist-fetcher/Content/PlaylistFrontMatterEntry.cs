namespace TheBluesland.SpotifyFetcher.Content;

/// <summary>
/// One <c>content/playlists/*.md</c> file's identity and current era tags, as read by
/// <see cref="PlaylistFrontMatterReader.ReadAllAsync"/>. Used by US-023's era report to label
/// each playlist and show its current <c>eras</c> next to the Spotify-data-derived suggestion.
///
/// <see cref="Status"/> and <see cref="FilePath"/> exist for the auto-unpublish-private-playlists
/// sync check (<c>PlaylistCacheSyncService</c>), which needs to know a file's current publish
/// status and where to rewrite it - never used to derive anything in the other direction.
/// </summary>
public sealed record PlaylistFrontMatterEntry(
    string SpotifyPlaylistId,
    string Slug,
    IReadOnlyList<string> Eras,
    string? Status = null,
    string FilePath = "");
