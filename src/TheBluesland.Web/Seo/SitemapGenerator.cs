using System.Net;
using System.Text;
using TheBluesland.Web.Content;

namespace TheBluesland.Web.Seo;

/// <summary>
/// Builds the <c>/sitemap.xml</c> response body (US-011 AC2, spec 14): the fixed static pages plus
/// every <em>published</em> playlist's canonical detail URL. Callers must pass the result of
/// <see cref="PlaylistContentRepository.FindAllPublishedAsync"/> - drafts are excluded upstream by
/// that method, not by this class - and no query-string variation of any URL is ever emitted (this
/// class only ever appends a bare path, never a query string).
/// </summary>
public static class SitemapGenerator
{
    // "/collections/{slug}" and similar do not exist yet (per WebHostFactory's route table) - kept
    // to only the static pages that actually exist, per this story's scope decision.
    private static readonly string[] StaticPaths = ["/", "/about", "/privacy", "/terms"];

    public static string Generate(HttpContext httpContext, IReadOnlyList<PlaylistContent> publishedPlaylists)
    {
        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="UTF-8"?>""").Append('\n');
        builder.Append("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""").Append('\n');

        foreach (var path in StaticPaths)
        {
            AppendUrl(builder, SiteUrl.BuildAbsolute(httpContext, path));
        }

        foreach (var playlist in publishedPlaylists)
        {
            AppendUrl(builder, SiteUrl.BuildAbsolute(httpContext, $"/playlists/{playlist.Slug}"));
        }

        builder.Append("</urlset>");
        return builder.ToString();
    }

    private static void AppendUrl(StringBuilder builder, string location)
    {
        builder.Append("  <url><loc>").Append(WebUtility.HtmlEncode(location)).Append("</loc></url>").Append('\n');
    }
}
