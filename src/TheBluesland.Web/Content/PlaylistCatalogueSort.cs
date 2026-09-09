using TheBluesland.Web.Cache;

namespace TheBluesland.Web.Content;

/// <summary>
/// Sorts the published catalogue for the home page. Rewritten 2026-09-10
/// (docs/specs/catalogue-priority-and-follower-sort.md): the previous displayOrder/publishedAt
/// rule differentiated almost nothing in practice - 118 of 120 playlists had no displayOrder, and
/// all 120 shared the identical publishedAt (a batch-import artifact) - and Mehmet wanted a
/// hand-picked priority list instead, plus Spotify's real follower count for everything else
/// (verified: neither publishedAt, SyncedAt, nor git history for content/playlists/*.md carries
/// any real per-playlist recency signal to sort by).
///
/// Featured playlists (<see cref="PlaylistContent.FeaturedOrder"/> set) sort first, by that number
/// ascending - an explicit, editorially-controlled priority, not a ranking score. Everything else
/// follows, by Spotify follower count descending; a playlist with no follower data (cache
/// unavailable, never synced) sorts after every playlist that has one, falling back to <see
/// cref="PlaylistContentReader"/>'s read order (stable sort) rather than being placed arbitrarily.
///
/// A pure function (no I/O) so it stays directly unit-testable; the follower counts it needs are
/// supplied by the caller (<see cref="PlaylistContentRepository"/>, from the same 5-minute
/// <see cref="PlaylistCacheSignalsCache"/> read that already refreshes computed eras).
/// </summary>
public static class PlaylistCatalogueSort
{
    public static IReadOnlyList<PlaylistContent> Apply(
        IReadOnlyList<PlaylistContent> playlists,
        IReadOnlyDictionary<string, PlaylistCacheSignals> cacheSignals) =>
        playlists
            .OrderBy(playlist => playlist.FeaturedOrder is { } order ? order : int.MaxValue)
            .ThenByDescending(playlist => FollowerCount(playlist, cacheSignals) ?? -1)
            .ToList();

    private static int? FollowerCount(
        PlaylistContent playlist,
        IReadOnlyDictionary<string, PlaylistCacheSignals> cacheSignals) =>
        cacheSignals.TryGetValue(playlist.SpotifyPlaylistId, out var signals) ? signals.FollowerCount : null;
}
