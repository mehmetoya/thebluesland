namespace TheBluesland.Web.Content;

/// <summary>
/// The manually-assigned editorial "tags" for a playlist: moods, genres, occasions and eras
/// combined (US-010 AC1 groups all four as one visible tag list, unlike the pre-US-010 view which
/// omitted era). Shared by <see cref="RelatedPlaylistRanking"/> and the playlist detail view so the
/// tag definition lives in exactly one place.
///
/// Before 2026-09-06 this had to special-case a blank single-valued era so two playlists with no
/// era never appeared to "share" that empty string. <see cref="PlaylistContent.Eras"/> is a list
/// now, so "no era" is simply an empty list that contributes nothing - the special case is gone.
/// </summary>
public static class PlaylistTags
{
    public static IReadOnlyList<string> All(PlaylistContent playlist) =>
        [.. playlist.Moods, .. playlist.Genres, .. playlist.Occasions, .. playlist.Eras];
}
