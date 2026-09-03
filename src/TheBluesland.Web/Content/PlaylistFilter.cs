namespace TheBluesland.Web.Content;

/// <summary>
/// Pure mood/genre/occasion/era filter matching for the home page catalogue (US-009 AC2, spec
/// FR-011): within one dimension (e.g. two selected moods), a playlist matches if it has *any* of
/// the selected values (OR); across dimensions (e.g. mood + occasion both selected), a playlist
/// must satisfy *every* dimension that has an active selection (AND). A dimension with no active
/// selection imposes no constraint. Deliberately independent of the UI/query-string layer (see
/// <see cref="PlaylistFilterCriteria"/>) so it is directly unit-testable and reusable elsewhere
/// (e.g. a future related-playlists tag-overlap feature).
/// </summary>
public static class PlaylistFilter
{
    public static IReadOnlyList<PlaylistContent> Apply(
        IReadOnlyList<PlaylistContent> playlists,
        PlaylistFilterCriteria criteria) =>
        playlists.Where(playlist => Matches(playlist, criteria)).ToList();

    private static bool Matches(PlaylistContent playlist, PlaylistFilterCriteria criteria) =>
        MatchesDimension(playlist.Moods, criteria.Moods) &&
        MatchesDimension(playlist.Genres, criteria.Genres) &&
        MatchesDimension(playlist.Occasions, criteria.Occasions) &&
        MatchesDimension([playlist.Era], criteria.Eras);

    private static bool MatchesDimension(
        IReadOnlyList<string> playlistValues,
        IReadOnlyCollection<string> selectedValues) =>
        selectedValues.Count == 0 ||
        playlistValues.Any(value => selectedValues.Contains(value, StringComparer.Ordinal));
}
