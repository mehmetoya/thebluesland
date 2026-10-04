namespace TheBluesland.Web.Content;

/// <summary>
/// Ranks other published playlists by weighted editorial-tag overlap with the playlist currently
/// being viewed. Mood and genre matches carry the most weight, followed by occasion and era, since
/// they are stronger indicators of a related listening experience than a shared time period alone.
/// The current playlist is always excluded, as is any candidate with zero shared tags. Ties break
/// the same way the home page catalogue orders playlists (<see cref="PlaylistCatalogueSort"/>:
/// displayOrder ascending, then publishedAt descending) so the result is deterministic. A pure
/// function (no I/O), directly unit-testable independent of <see cref="PlaylistContentRepository"/>.
/// </summary>
public static class RelatedPlaylistRanking
{
    private const int MaxResults = 3;
    private const int MoodWeight = 4;
    private const int GenreWeight = 3;
    private const int OccasionWeight = 2;
    private const int EraWeight = 1;

    public static IReadOnlyList<PlaylistContent> Apply(
        IReadOnlyList<PlaylistContent> publishedPlaylists,
        PlaylistContent current) =>
        publishedPlaylists
            .Where(playlist => !string.Equals(playlist.Slug, current.Slug, StringComparison.Ordinal))
            .Select(playlist => (Playlist: playlist, MatchScore: CalculateMatchScore(playlist, current)))
            .Where(candidate => candidate.MatchScore > 0)
            .OrderByDescending(candidate => candidate.MatchScore)
            .ThenBy(candidate => candidate.Playlist.DisplayOrder)
            .ThenByDescending(candidate => candidate.Playlist.PublishedAt)
            .Take(MaxResults)
            .Select(candidate => candidate.Playlist)
            .ToList();

    private static int CalculateMatchScore(PlaylistContent candidate, PlaylistContent current) =>
        CountSharedTags(candidate.Moods, current.Moods) * MoodWeight
        + CountSharedTags(candidate.Genres, current.Genres) * GenreWeight
        + CountSharedTags(candidate.Occasions, current.Occasions) * OccasionWeight
        + CountSharedTags(candidate.Eras, current.Eras) * EraWeight;

    private static int CountSharedTags(IReadOnlyList<string> candidateTags, IReadOnlyList<string> currentTags)
    {
        var currentTagSet = currentTags.ToHashSet(StringComparer.Ordinal);
        return candidateTags.Count(currentTagSet.Contains);
    }
}
