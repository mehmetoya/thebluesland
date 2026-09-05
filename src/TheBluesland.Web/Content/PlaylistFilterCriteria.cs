namespace TheBluesland.Web.Content;

/// <summary>
/// The active mood/genre/occasion/era selections for the home page catalogue filter (US-009 AC2,
/// spec FR-010/FR-011). An empty collection for a dimension means "no constraint from that
/// dimension" (matches everything), not "match nothing" - see <see cref="PlaylistFilter"/>.
/// </summary>
public sealed record PlaylistFilterCriteria(
    IReadOnlyCollection<string> Moods,
    IReadOnlyCollection<string> Genres,
    IReadOnlyCollection<string> Occasions,
    IReadOnlyCollection<string> Eras)
{
    public static readonly PlaylistFilterCriteria None = new([], [], [], []);

    /// <summary>True when at least one dimension has an active selection (US-009 AC4: the empty
    /// "no results" state and its clear-filters affordance only apply when filters are active).</summary>
    public bool IsActive => Moods.Count > 0 || Genres.Count > 0 || Occasions.Count > 0 || Eras.Count > 0;

    /// <summary>Total active selections across all four dimensions - US-021's mobile full-screen
    /// panel shows one combined "Filters (N)" trigger, unlike US-018's per-dimension counts.</summary>
    public int ActiveCount => Moods.Count + Genres.Count + Occasions.Count + Eras.Count;
}
