namespace TheBluesland.Web.Content;

/// <summary>
/// The manually-assigned editorial "tags" for a playlist: moods, genres, occasions and era
/// combined (US-010 AC1 groups all four as one visible tag list, unlike the pre-US-010 view which
/// omitted era). A blank <see cref="PlaylistContent.Era"/> - <see cref="PlaylistContentReader"/>
/// defaults it to <see cref="string.Empty"/> when front matter omits the field - is excluded, so
/// two playlists that both simply have no era never appear to "share" that blank value. Shared by
/// <see cref="RelatedPlaylistRanking"/> and the playlist detail view so the tag definition and the
/// blank-era exclusion live in exactly one place.
/// </summary>
public static class PlaylistTags
{
    public static IReadOnlyList<string> All(PlaylistContent playlist) =>
        [.. playlist.Moods, .. playlist.Genres, .. playlist.Occasions, .. EraOrEmpty(playlist.Era)];

    private static IEnumerable<string> EraOrEmpty(string era) =>
        era is { Length: > 0 } ? [era] : [];
}
