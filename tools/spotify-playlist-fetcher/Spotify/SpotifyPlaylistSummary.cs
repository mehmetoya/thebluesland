namespace TheBluesland.SpotifyFetcher.Spotify;

/// <summary>
/// Playlist-level facts read from the Spotify Web API for one playlist. Deliberately mirrors the
/// non-track-level column set of <c>spotify_playlist_cache</c> (spec section 9.4). No track title,
/// track id, duration, ISRC or audio-feature field is ever represented here, even transiently -
/// see <see cref="SpotifyPlaylistClient"/> for how <see cref="Artists"/> is derived without
/// retaining any other per-track data.
/// </summary>
public sealed record SpotifyPlaylistSummary
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? CoverImageUrl { get; init; }

    public required int TrackCount { get; init; }

    public required string[] Artists { get; init; }

    /// <summary>Automatically calculated playlist-level era tags; null before the first era sync, empty when data is insufficient.</summary>
    public string[]? ComputedEras { get; init; }

    /// <summary>
    /// Dated tracks per era bucket in <c>EraBucketMapper.ConcreteBuckets</c> order - the aggregate
    /// the era tags were derived from, kept so the measurement survives the crawl (US-026).
    /// </summary>
    public int[]? EraBucketCounts { get; init; }

    public string? SnapshotId { get; init; }
}
