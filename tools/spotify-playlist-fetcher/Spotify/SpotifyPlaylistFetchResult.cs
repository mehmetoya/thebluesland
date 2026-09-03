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

    public sealed record Found(SpotifyPlaylistSummary Summary) : SpotifyPlaylistFetchResult;

    public sealed record NotFound : SpotifyPlaylistFetchResult;
}
