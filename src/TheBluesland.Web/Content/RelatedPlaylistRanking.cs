namespace TheBluesland.Web.Content;

/// <summary>
/// Ranks other published playlists by shared-tag overlap with the playlist currently being viewed
/// (US-010 AC4, spec FR-023: "up to three published playlists with the strongest overlap in
/// manually assigned tags"). Tags come from <see cref="PlaylistTags"/> (moods, genres, occasions
/// and era combined, blank era excluded). The current playlist is always excluded, as is any
/// candidate with zero shared tags (sharing nothing isn't "related"). Ties in shared-tag count
/// break the same way the home page catalogue orders playlists (<see cref="PlaylistCatalogueSort"/>:
/// displayOrder ascending, then publishedAt descending) rather than an arbitrary/unstable order, so
/// the result is deterministic. A pure function (no I/O), à la <see cref="PlaylistFilter"/> and
/// <see cref="PlaylistCatalogueSort"/>, so it is directly unit-testable independent of
/// <see cref="PlaylistContentRepository"/>.
/// </summary>
public static class RelatedPlaylistRanking
{
    private const int MaxResults = 3;

    public static IReadOnlyList<PlaylistContent> Apply(
        IReadOnlyList<PlaylistContent> publishedPlaylists,
        PlaylistContent current) =>
        publishedPlaylists
            .Where(playlist => !string.Equals(playlist.Slug, current.Slug, StringComparison.Ordinal))
            .Select(playlist => (Playlist: playlist, SharedTagCount: CountSharedTags(playlist, current)))
            .Where(candidate => candidate.SharedTagCount > 0)
            .OrderByDescending(candidate => candidate.SharedTagCount)
            .ThenBy(candidate => candidate.Playlist.DisplayOrder)
            .ThenByDescending(candidate => candidate.Playlist.PublishedAt)
            .Take(MaxResults)
            .Select(candidate => candidate.Playlist)
            .ToList();

    private static int CountSharedTags(PlaylistContent candidate, PlaylistContent current)
    {
        var currentTags = PlaylistTags.All(current).ToHashSet(StringComparer.Ordinal);
        return PlaylistTags.All(candidate).Count(tag => currentTags.Contains(tag));
    }
}
