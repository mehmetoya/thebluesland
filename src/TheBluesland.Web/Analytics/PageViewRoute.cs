namespace TheBluesland.Web.Analytics;

/// <summary>
/// Classifies a request path per docs/specs/visitor-and-playlist-click-analytics.md's Design
/// section 4 route table, so the page-view middleware in <see cref="TheBluesland.Web.WebHostFactory"/>
/// logs precisely the real content routes - never static assets, health checks,
/// sitemap.xml/robots.txt/llms.txt, og-image renders, or the separately-logged <c>/out/{slug}</c>
/// redirect (spec Design section 5).
/// </summary>
public static class PageViewRoute
{
    private static readonly string[] ExactMatchRoutes = ["/", "/about", "/collections", "/privacy", "/terms"];
    private const string PlaylistsPrefix = "/playlists/";
    private const string CollectionsPrefix = "/collections/";

    /// <summary>
    /// True and <paramref name="playlistSlug"/> set when <paramref name="path"/> is a known content
    /// route that should be recorded as a page view. <c>/collections/{slug}</c> classifies as a page
    /// view but leaves <paramref name="playlistSlug"/> null - it is a collection key, not a
    /// playlist, and must not be conflated with one in the stored column.
    /// </summary>
    public static bool TryClassify(string path, out string? playlistSlug)
    {
        playlistSlug = null;

        if (ExactMatchRoutes.Contains(path, StringComparer.Ordinal))
        {
            return true;
        }

        if (TryGetSingleSegment(path, PlaylistsPrefix, out var slug))
        {
            playlistSlug = slug;
            return true;
        }

        return TryGetSingleSegment(path, CollectionsPrefix, out _);
    }

    // A single path segment after the prefix - "/playlists/my-slug" matches, but
    // "/playlists/my-slug/og-image.png" (a further '/') and "/playlists/" (empty) do not.
    private static bool TryGetSingleSegment(string path, string prefix, out string? segment)
    {
        segment = null;
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = path[prefix.Length..];
        if (remainder.Length == 0 || remainder.Contains('/'))
        {
            return false;
        }

        segment = remainder;
        return true;
    }
}
