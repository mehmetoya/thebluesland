using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TheBluesland.WeeklyNewsletter.Review;

namespace TheBluesland.WeeklyNewsletter.Delivery;

/// <summary>What Kit sends: subject, inbox preview line and the HTML body.</summary>
public sealed record IssueEmail(string Subject, string PreviewText, string Html);

/// <summary>
/// Turns a merged issue into e-mail HTML (issue-delivery spec, Design section 4). Raw HTML in the
/// Markdown is escaped, and only https links survive; any other link or image becomes its text.
/// Kit wraps the result in its template and adds the unsubscribe footer.
/// </summary>
public static class IssueEmailRenderer
{
    public const int MaxPreviewLength = 150;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    public static IssueEmail Render(WeeklyIssue issue)
    {
        var document = Markdown.Parse(issue.Body, Pipeline);
        foreach (var link in document.Descendants<LinkInline>().Where(link => link.IsImage || !IsHttps(link.Url)).ToList())
        {
            link.ReplaceBy(new LiteralInline(PlainText(link)), copyChildren: false);
        }

        foreach (var link in document.Descendants<AutolinkInline>().Where(link => link.IsEmail || !IsHttps(link.Url)).ToList())
        {
            link.ReplaceBy(new LiteralInline(link.Url), copyChildren: false);
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();

        var html = $"<h1>{Escape(issue.Title)}</h1>\n{writer}";
        return new IssueEmail(issue.Subject ?? issue.Title, issue.Preview ?? Preview(issue.Body), html);
    }

    /// <summary>The intro's first sentence, as plain text, for the inbox preview line.</summary>
    private static string Preview(string body)
    {
        var intro = body.Split("\n## ", 2)[0];
        var text = string.Join(' ', Markdown.ToPlainText(intro, Pipeline).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var end = text.IndexOfAny(['.', '?', '…']);
        var sentence = end > 0 ? text[..(end + 1)] : text;
        return sentence.Length <= MaxPreviewLength ? sentence : sentence[..(MaxPreviewLength - 1)] + "…";
    }

    /// <summary>Only the characters HTML gives meaning; "·" and Turkish letters stay readable.</summary>
    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string PlainText(ContainerInline container) =>
        string.Concat(container.Descendants<LiteralInline>().Select(literal => literal.Content.ToString()));

    private static bool IsHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
