using System.Net;
using System.Text;
using TheBluesland.Web.Content;
using TheBluesland.Web.Localization;

namespace TheBluesland.Web.Seo;

/// <summary>
/// Builds the <c>/sitemap.xml</c> response body (US-011 AC2, spec 14): the fixed static pages plus
/// every <em>published</em> playlist's canonical detail URL, each listed once per language
/// (<c>/tr/...</c> and <c>/en/...</c>) with <c>xhtml:link</c> alternates (tr, en, x-default -> the
/// English URL) so search engines pair the two (docs/specs/site-i18n.md). Callers must pass the
/// result of <see cref="PlaylistContentRepository.FindAllPublishedAsync"/> - drafts are excluded
/// upstream by that method, not by this class - and no query-string variation of any URL is ever
/// emitted (this class only ever appends a bare path, never a query string).
/// </summary>
public static class SitemapGenerator
{
    private static readonly string[] StaticPaths = ["/", "/collections", "/weekly", "/about", "/privacy", "/terms"];

    public static string Generate(HttpContext httpContext, IReadOnlyList<PlaylistContent> publishedPlaylists)
    {
        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="UTF-8"?>""").Append('\n');
        builder.Append("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">""").Append('\n');

        foreach (var path in StaticPaths)
        {
            AppendUrl(builder, httpContext, path);
        }

        foreach (var collection in PlaylistCollections.All)
        {
            AppendUrl(builder, httpContext, $"/collections/{collection.Slug}");
        }

        foreach (var playlist in publishedPlaylists)
        {
            AppendUrl(builder, httpContext, $"/playlists/{playlist.Slug}");
        }

        builder.Append("</urlset>");
        return builder.ToString();
    }

    // One <url> per language, each carrying the full alternate set (a page lists itself too).
    private static void AppendUrl(StringBuilder builder, HttpContext httpContext, string neutralPath)
    {
        foreach (var code in SiteLanguage.Supported)
        {
            builder.Append("  <url><loc>").Append(Absolute(httpContext, code, neutralPath)).Append("</loc>");
            foreach (var alternate in SiteLanguage.Supported)
            {
                AppendAlternate(builder, alternate, Absolute(httpContext, alternate, neutralPath));
            }

            AppendAlternate(builder, "x-default", Absolute(httpContext, SiteLanguage.DefaultCode, neutralPath));
            builder.Append("</url>").Append('\n');
        }
    }

    private static void AppendAlternate(StringBuilder builder, string hreflang, string href) =>
        builder.Append("<xhtml:link rel=\"alternate\" hreflang=\"").Append(hreflang).Append("\" href=\"").Append(href).Append("\"/>");

    private static string Absolute(HttpContext httpContext, string code, string neutralPath) =>
        WebUtility.HtmlEncode(SiteUrl.BuildAbsolute(httpContext, SiteLanguage.UrlFor(code, neutralPath)));
}
