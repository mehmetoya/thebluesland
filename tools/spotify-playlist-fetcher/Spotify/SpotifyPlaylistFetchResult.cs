namespace TheBluesland.SpotifyFetcher.Spotify;

/// <summary>
/// Outcome of fetching one playlist from the Spotify Web API. Only an explicit "not found"
/// response (404, or 403/410 treated as inaccessible) produces <see cref="NotFound"/>; any other
/// unexpected error propagates as an exception instead, so a transient failure never causes an
/// existing cache row to be marked unavailable (spec section 16.1).
/// </summary>
public abstract record SpotifyPlaylistFetchResult
{
    protected SpotifyPlaylistFetchResult()
    {
    }

    /// <param name="Summary">Playlist-level facts read from Spotify.</param>
    /// <param name="TrackAggregatesSkipped">
    /// True when the playlist's <c>snapshot_id</c> matched the caller's known one, so the paginated
    /// track pass was not made at all (US-024). <see cref="SpotifyPlaylistSummary.Artists"/> and
    /// <see cref="SpotifyPlaylistSummary.ComputedEras"/> are then empty/null placeholders that carry
    /// no information: the caller must keep the values it already has rather than write these.
    /// </param>
    public sealed record Found(SpotifyPlaylistSummary Summary, bool TrackAggregatesSkipped = false)
        : SpotifyPlaylistFetchResult;

    public sealed record NotFound : SpotifyPlaylistFetchResult;
}
