using System.Text;
using TheBluesland.Web.Content;

namespace TheBluesland.Web.Seo;

public static class AiDiscoveryGenerator
{
    private const string SiteDescription = "Mehmet's personal collection of Spotify playlists, rooted in blues and spanning rock, folk, jazz, soul and electronic music. Each playlist has a short listening note.";
    private static readonly string[] AiBots = ["OAI-SearchBot", "ChatGPT-User", "GPTBot", "Claude-SearchBot", "Claude-User", "ClaudeBot", "PerplexityBot", "Google-Extended"];

    public static string BuildRobots(HttpContext context)
    {
        var text = new StringBuilder("User-agent: *\nAllow: /\n\n");
        foreach (var bot in AiBots)
        {
            text.Append("User-agent: ").Append(bot).Append("\nAllow: /\n\n");
        }
        return text.Append("Sitemap: ").Append(SiteUrl.BuildAbsolute(context, "/sitemap.xml")).Append('\n').ToString();
    }

    public static string BuildLlms(HttpContext context, IReadOnlyList<PlaylistContent> playlists)
    {
        var text = new StringBuilder("# TheBluesland\n\n> ").Append(SiteDescription)
            .Append("\n\nPlaylist pages contain editorial descriptions, not complete track listings. Listen through the Spotify links on each page.\n\n## Site\n\n")
            .Append("- [Catalogue](").Append(SiteUrl.BuildAbsolute(context, "/")).Append("): Browse published playlists.\n")
            .Append("- [Collections](").Append(SiteUrl.BuildAbsolute(context, "/collections")).Append("): Explore genres and listening occasions.\n")
            .Append("- [About](").Append(SiteUrl.BuildAbsolute(context, "/about")).Append("): About the collection.\n")
            .Append("\n## Playlists\n\n");
        foreach (var playlist in playlists.Where(item => item.IsPublished).OrderBy(item => item.Slug, StringComparer.Ordinal))
        {
            text.Append("- [").Append(EscapeText(playlist.Title)).Append("](")
                .Append(SiteUrl.BuildAbsolute(context, $"/playlists/{Uri.EscapeDataString(playlist.Slug)}"))
                .Append("): ").Append(EscapeText(playlist.Summary)).Append('\n');
        }
        return text.ToString();
    }

    private static string EscapeText(string value) => value.Replace("\\", "\\\\").Replace("[", "\\[")
        .Replace("]", "\\]").Replace("\r", " ").Replace("\n", " ");
}
