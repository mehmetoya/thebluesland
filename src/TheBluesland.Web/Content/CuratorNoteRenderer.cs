using System.Net;
using System.Text.RegularExpressions;
using Markdig;

namespace TheBluesland.Web.Content;

/// <summary>
/// Renders a curator note's raw Markdown (<see cref="PlaylistContent.CuratorNote"/>) into sanitized
/// HTML for the playlist detail page (US-010 AC1, spec 12.2: Markdig is the specified Markdown
/// library). <see cref="MarkdownPipelineBuilder.DisableHtml"/> removes Markdig's HTML block/inline
/// parsers, so raw HTML written into a curator note's Markdown source is escaped/output as literal
/// text rather than parsed and passed through - the documented way to satisfy spec 9.3 ("Markdown
/// must be sanitised; raw HTML is disabled by default") and SEC-003.
///
/// DisableHtml alone does not sanitize a URL scheme used in ordinary Markdown link/image syntax
/// (e.g. <c>[text](javascript:...)</c>), which Markdig still renders as a normal <c>href</c>/
/// <c>src</c> attribute - found in review. <see cref="StripDangerousUrlSchemes"/> is a second,
/// narrow pass over Markdig's own output that neutralises the small set of URI schemes browsers
/// treat as executable (javascript/vbscript/data/file), rather than pulling in a general-purpose
/// HTML sanitizer library for this one gap.
///
/// Deliberately a pure function, independent of <see cref="PlaylistContentReader"/> (which keeps
/// <c>CuratorNote</c> as the raw Markdown string) - see that reader's and
/// <see cref="PlaylistContentValidator"/>'s own doc comments on staying decoupled from rendering,
/// so this stays directly unit-testable and reusable by any future route that also needs
/// curator-note rendering.
/// </summary>
public static class CuratorNoteRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .Build();

    private static readonly Regex UrlAttribute = new(
        """(?<attr>href|src)=["'](?<value>[^"']*)["']""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] DisallowedSchemes = ["javascript", "vbscript", "data", "file"];

    public static string ToSanitizedHtml(string curatorNoteMarkdown) =>
        StripDangerousUrlSchemes(Markdown.ToHtml(curatorNoteMarkdown, Pipeline));

    private static string StripDangerousUrlSchemes(string html) =>
        UrlAttribute.Replace(html, match =>
        {
            var scheme = ExtractScheme(WebUtility.HtmlDecode(match.Groups["value"].Value));
            return scheme is not null && DisallowedSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase)
                ? $"{match.Groups["attr"].Value}=\"#\""
                : match.Value;
        });

    // Browsers ignore control characters and whitespace inside a URL scheme when deciding whether
    // to treat it as "javascript:" (e.g. "java\tscript:alert(1)"), so those are stripped before
    // looking for the ':' separator rather than matching the raw substring literally.
    private static string? ExtractScheme(string url)
    {
        var normalized = new string(url.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());
        var separatorIndex = normalized.IndexOf(':');
        return separatorIndex > 0 ? normalized[..separatorIndex] : null;
    }
}
