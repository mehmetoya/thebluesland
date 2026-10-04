namespace TheBluesland.Web.Content;

/// <summary>
/// Re-orders the already-related playlist set for a selected editorial mix mode. Preferences use
/// exact approved taxonomy values rather than substring guesses, and ties preserve the related
/// ranking's deterministic order.
/// </summary>
public static class PlaylistMixRanking
{
    private static readonly HashSet<string> EnergeticGenres =
        new(["blues-rock", "rock", "punk", "metal", "funk", "electronic"], StringComparer.Ordinal);
    private static readonly HashSet<string> EnergeticOccasions =
        new(["night-drive", "road-trip", "dancing"], StringComparer.Ordinal);
    private static readonly HashSet<string> RelaxedMoods =
        new(["warm", "melancholic", "nostalgic"], StringComparer.Ordinal);
    private static readonly HashSet<string> RelaxedGenres =
        new(["soul", "jazz", "folk"], StringComparer.Ordinal);
    private static readonly HashSet<string> RelaxedOccasions =
        new(["late-night", "slow-evening", "focus", "headphones"], StringComparer.Ordinal);
    private static readonly HashSet<string> DeeperCutGenres =
        new(["jazz", "classical", "folk", "world"], StringComparer.Ordinal);

    public static IReadOnlyList<PlaylistContent> Apply(
        IReadOnlyList<PlaylistContent> relatedPlaylists,
        PlaylistContent current,
        PlaylistMixMode mode)
    {
        var candidates = relatedPlaylists
            .Where(playlist => !string.Equals(playlist.Slug, current.Slug, StringComparison.Ordinal))
            .Select((playlist, index) => (Playlist: playlist, OriginalOrder: index));

        if (mode == PlaylistMixMode.SameVibe)
        {
            return candidates.Select(candidate => candidate.Playlist).ToList();
        }

        return candidates
            .OrderByDescending(candidate => Score(candidate.Playlist, mode))
            .ThenBy(candidate => candidate.OriginalOrder)
            .Select(candidate => candidate.Playlist)
            .ToList();
    }

    private static int Score(PlaylistContent playlist, PlaylistMixMode mode) =>
        mode switch
        {
            PlaylistMixMode.MoreEnergetic =>
                CountMatches(playlist.Moods, ["energetic"]) * 5
                + CountMatches(playlist.Occasions, EnergeticOccasions) * 3
                + CountMatches(playlist.Genres, EnergeticGenres),
            PlaylistMixMode.RelaxedFlow =>
                CountMatches(playlist.Moods, RelaxedMoods) * 4
                + CountMatches(playlist.Occasions, RelaxedOccasions) * 3
                + CountMatches(playlist.Genres, RelaxedGenres),
            PlaylistMixMode.DeeperCuts =>
                CountMatches(playlist.Occasions, ["deep-listen"]) * 5
                + CountMatches(playlist.Genres, DeeperCutGenres) * 2
                + CountMatches(playlist.Moods, ["raw", "nostalgic"]),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

    private static int CountMatches(IReadOnlyList<string> tags, IEnumerable<string> preferredTags)
    {
        var preferredTagSet = preferredTags as HashSet<string>
            ?? preferredTags.ToHashSet(StringComparer.Ordinal);
        return tags.Count(preferredTagSet.Contains);
    }
}
