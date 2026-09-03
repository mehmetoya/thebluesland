namespace TheBluesland.Data.Entities;

/// <summary>
/// Spotify-owned facts about a playlist, refreshed once a month by the out-of-process sync tool
/// (<c>tools/spotify-playlist-fetcher</c>) and read-only from the web application. Joined to the
/// editorial Markdown content by <see cref="SpotifyPlaylistId"/>.
///
/// This entity intentionally excludes every track-level field (title, track id, duration, ISRC,
/// audio-feature data): those are read transiently in memory during sync solely to compute
/// <see cref="TrackCount"/> and <see cref="Artists"/>, and are never persisted. See spec section
/// 9.4, section 11.2 and Spotify Developer Policy §14.
/// </summary>
public sealed class SpotifyPlaylistCacheEntry
{
    /// <summary>Same value as the editorial <c>spotifyPlaylistId</c> front-matter field; the join key.</summary>
    public required string SpotifyPlaylistId { get; set; }

    /// <summary>Spotify's own playlist name (may differ from TheBluesland's editorial title).</summary>
    public required string Name { get; set; }

    /// <summary>Spotify's own playlist description.</summary>
    public string? Description { get; set; }

    /// <summary>Spotify-hosted cover image URL, referenced only, never downloaded or re-hosted.</summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>Total track count as of last sync.</summary>
    public required int TrackCount { get; set; }

    /// <summary>Distinct contributing artist display names; order not significant.</summary>
    public required string[] Artists { get; set; }

    /// <summary>Spotify's own change-detection token, stored for future incremental sync.</summary>
    public string? SpotifySnapshotId { get; set; }

    /// <summary>UTC time of the last successful sync.</summary>
    public required DateTimeOffset SyncedAt { get; set; }

    /// <summary>False if the last sync could not find the playlist on Spotify.</summary>
    public required bool IsAvailable { get; set; }
}
