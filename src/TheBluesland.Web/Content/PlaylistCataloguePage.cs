namespace TheBluesland.Web.Content;

/// <summary>
/// Server-side, zero-JS progressive loading for the home page catalogue (US-019, spec 12.3's
/// "no client-side/API pagination" constraint): page N reveals the first N * <see cref="PageSize"/>
/// matching playlists cumulatively, not a windowed page N..2N slice - so a shared deep link with
/// "?page=3" reproduces exactly the same amount of content a visitor would see after clicking
/// "Show more" twice from page 1. Deliberately independent of the UI/query-string layer (see
/// <see cref="PlaylistFilter"/>'s own doc comment for the same reasoning) so it is directly
/// unit-testable.
/// </summary>
public static class PlaylistCataloguePage
{
    public const int PageSize = 24;

    public static IReadOnlyList<PlaylistContent> Take(IReadOnlyList<PlaylistContent> playlists, int? pageQuery)
    {
        var visibleCount = Math.Min(playlists.Count, NormalizePage(pageQuery) * PageSize);
        return playlists.Take(visibleCount).ToList();
    }

    public static bool HasMore(IReadOnlyList<PlaylistContent> playlists, int? pageQuery) =>
        NormalizePage(pageQuery) * PageSize < playlists.Count;

    public static int NextPage(int? pageQuery) => NormalizePage(pageQuery) + 1;

    // Anything not a positive integer (missing, zero, negative, or a tampered-with value) falls
    // back to page 1 rather than throwing or showing nothing.
    private static int NormalizePage(int? pageQuery) => pageQuery is > 0 ? pageQuery.Value : 1;
}
