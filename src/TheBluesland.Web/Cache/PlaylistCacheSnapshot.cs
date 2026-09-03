namespace TheBluesland.Web.Cache;

/// <summary>
/// A visitor-facing summary of a playlist's Spotify-sourced cache state. <see cref="Unavailable"/>
/// covers three distinct backend situations identically, per spec FR-024/16.1: no cache row yet,
/// a row with <c>is_available = false</c>, and a database that could not be reached at all. All
/// three must degrade to the same "no player, editorial content only" render outcome.
/// </summary>
public sealed record PlaylistCacheSnapshot(bool IsPlayable, int? TrackCount, string? CoverImageUrl)
{
    public static readonly PlaylistCacheSnapshot Unavailable = new(IsPlayable: false, TrackCount: null, CoverImageUrl: null);
}
