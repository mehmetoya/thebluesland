namespace TheBluesland.WeeklyNewsletter.Reporting;

/// <summary>
/// Makes feed-supplied text safe for GitHub job-summary Markdown: it can neither break a table nor
/// be swallowed by GitHub's HTML sanitizer, and it stays one line of bounded length.
/// </summary>
public static class MarkdownText
{
    public const int MaxCellLength = 200;

    /// <summary>Keeps arbitrary text (titles, feed names, exception messages) inside one table cell.</summary>
    public static string Cell(string? text)
    {
        var flat = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length > MaxCellLength)
        {
            flat = flat[..(MaxCellLength - 1)] + "…";
        }

        return flat
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    /// <summary>
    /// A Markdown link to an http(s) URL, or the escaped text alone when the URL is anything else.
    /// Parentheses and spaces are percent-encoded so the URL cannot end the link early.
    /// </summary>
    public static string Link(string? text, string? url)
    {
        var label = Cell(text);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return label;
        }

        var target = uri.AbsoluteUri
            .Replace("(", "%28", StringComparison.Ordinal)
            .Replace(")", "%29", StringComparison.Ordinal)
            .Replace(" ", "%20", StringComparison.Ordinal);
        return $"[{label}]({target})";
    }
}
