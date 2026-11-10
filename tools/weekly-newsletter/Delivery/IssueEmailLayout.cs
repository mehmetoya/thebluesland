using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TheBluesland.WeeklyNewsletter.Review;

namespace TheBluesland.WeeklyNewsletter.Delivery;

/// <summary>
/// The branded e-mail body (design pass DD-3/UX-2/A11Y-7/ED-13): one 600px column, inline styles only
/// (no &lt;style&gt;, no web fonts, no tables), system sans-serif, dark text on white so it still reads
/// when Kit wraps it in its own (white) template. Links use brand blue #004aad; the guitar orange
/// (#ff904b) only decorates, because it fails contrast on white. Works on the already neutralised
/// Markdig tree, so raw HTML stays escaped and only https links survive.
/// </summary>
internal static class IssueEmailLayout
{
    public const string SiteOrigin = "https://thebluesland.com";
    public const int MaxSourcesPerStory = 2;

    private const string TitlePrefix = "TheBluesland Weekly · ";
    private const string Ink = "#0d1f3c";
    private const string Muted = "#45597a";
    private const string Rule = "#e8eff9";
    private const string Brand = "#004aad";
    private const string Label = "#a8501c";
    private const string Orange = "#ff904b";
    private const string Soft = "#f5f8fd";

    private const string LinkStyle = $"color:{Brand};text-decoration:underline;";
    private const string FontFamily = "Arial,Helvetica,sans-serif";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    private static readonly string[] TurkishMonths =
        ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];

    private static readonly string[] EnglishMonths =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    public static string Build(WeeklyIssue issue, MarkdownDocument document)
    {
        var turkish = issue.Language.Code == "tr";
        var blocks = document.ToList();
        foreach (var link in document.Descendants<LinkInline>().Where(link => !link.IsImage))
        {
            link.GetAttributes().AddProperty("style", LinkStyle);
        }

        var html = new StringBuilder();
        html.Append($"<div lang=\"{issue.Language.Code}\" dir=\"ltr\" style=\"max-width:600px;margin:0 auto;background:#ffffff;color:{Ink};font-family:{FontFamily};font-size:16px;line-height:1.6;\">\n");
        html.Append($"<div style=\"background:{Brand};padding:16px 24px;text-align:center;\"><a href=\"{SiteOrigin}/weekly\" style=\"text-decoration:none;\"><img src=\"{SiteOrigin}/images/logo-blue.png\" alt=\"TheBluesland Weekly\" width=\"160\" style=\"display:block;width:160px;max-width:100%;height:auto;margin:0 auto;border:0;\"></a></div>\n");
        html.Append("<div style=\"padding:0 24px 24px;\">\n");

        var number = IssueNumber(issue.Id);
        var webUrl = $"{SiteOrigin}/weekly/{issue.Id}/{issue.Language.Code}";
        html.Append($"<p style=\"margin:16px 0 0;font-size:13px;line-height:1.5;color:{Muted};\">{(turkish ? "Sayı" : "Issue")} {number} · {FormatDate(issue.SendDate, turkish)} · <a href=\"{webUrl}\" style=\"{LinkStyle}\">{(turkish ? "Web'de oku" : "Read on the web")}</a></p>\n");
        html.Append($"<h1 style=\"margin:8px 0 20px;font-size:28px;line-height:1.2;font-weight:bold;color:{Ink};\">{Escape(DisplayTitle(issue.Title))}</h1>\n");

        var index = 0;
        var firstIntro = true;
        while (index < blocks.Count && blocks[index] is not HeadingBlock { Level: 2 })
        {
            var block = blocks[index++];
            html.Append(Render(block, firstIntro && block is ParagraphBlock
                ? $"margin:0 0 24px;font-size:18px;line-height:1.55;color:{Ink};"
                : $"margin:0 0 16px;color:{Ink};"));
            firstIntro = false;
        }

        var story = 0;
        while (index < blocks.Count && blocks[index] is HeadingBlock { Level: 2 } heading)
        {
            index++;
            story++;
            html.Append($"<div style=\"margin-top:28px;padding-top:24px;border-top:1px solid {Rule};\">\n");
            html.Append($"<p style=\"margin:0 0 4px;font-size:13px;font-weight:bold;letter-spacing:0.08em;color:{Label};\">{story:00}</p>\n");
            html.Append(Render(heading, $"margin:0 0 12px;font-size:20px;line-height:1.3;font-weight:bold;color:{Ink};"));

            while (index < blocks.Count && blocks[index] is not HeadingBlock { Level: 2 })
            {
                var block = blocks[index++];
                if (IsSources(block, issue))
                {
                    KeepFirstLinks((ParagraphBlock)block);
                    html.Append(Render(block, $"margin:0;font-size:13px;line-height:1.5;color:{Muted};"));
                    break;
                }

                if (IsWhy(block, issue))
                {
                    StyleWhyLabel((ParagraphBlock)block);
                    html.Append(Render(block, $"margin:0 0 12px;padding:12px 16px;border-left:4px solid {Orange};background:{Soft};font-size:15px;line-height:1.55;color:{Ink};"));
                    continue;
                }

                html.Append(Render(block, $"margin:0 0 16px;color:{Ink};"));
            }

            html.Append("</div>\n");
        }

        var firstOutro = true;
        for (; index < blocks.Count; index++)
        {
            var block = blocks[index];
            if (index == blocks.Count - 1 && IsOnlyEmphasis(block))
            {
                html.Append(Render(block, $"margin:16px 0 0;font-size:12px;line-height:1.5;color:{Muted};"));
            }
            else
            {
                html.Append(Render(block, firstOutro
                    ? $"margin:28px 0 0;padding-top:24px;border-top:1px solid {Rule};color:{Ink};"
                    : $"margin:16px 0 0;color:{Ink};"));
                firstOutro = false;
            }
        }

        var otherCode = turkish ? "en" : "tr";
        html.Append($"<p style=\"margin:24px 0 0;padding-top:16px;border-top:1px solid {Rule};font-size:13px;line-height:1.5;color:{Muted};\">");
        html.Append(turkish
            ? $"<a href=\"{SiteOrigin}/weekly\" style=\"{LinkStyle}\">Arşiv</a> · <a href=\"{SiteOrigin}/weekly/{issue.Id}/{otherCode}\" lang=\"en\" style=\"{LinkStyle}\">Read in English</a>"
            : $"<a href=\"{SiteOrigin}/weekly\" style=\"{LinkStyle}\">Archive</a> · <a href=\"{SiteOrigin}/weekly/{issue.Id}/{otherCode}\" lang=\"tr\" style=\"{LinkStyle}\">Türkçe oku</a>");
        html.Append("</p>\n</div>\n</div>\n");
        return html.ToString();
    }

    private static string Render(Block block, string style)
    {
        block.GetAttributes().AddProperty("style", style);
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(block);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>The label inside the why-it-matters box (first emphasis) becomes a small caps tag.</summary>
    private static void StyleWhyLabel(ParagraphBlock paragraph)
    {
        if (paragraph.Inline?.FirstChild is EmphasisInline label)
        {
            label.GetAttributes().AddProperty("style", $"font-style:normal;font-weight:bold;font-size:12px;letter-spacing:0.06em;text-transform:uppercase;color:{Label};");
        }
    }

    /// <summary>E-mail shows at most <see cref="MaxSourcesPerStory"/> sources (the first is the primary one).</summary>
    private static void KeepFirstLinks(ParagraphBlock paragraph)
    {
        if (paragraph.Inline is null)
        {
            return;
        }

        var links = 0;
        foreach (var inline in paragraph.Inline.ToList())
        {
            if (inline is not LinkInline || ++links <= MaxSourcesPerStory)
            {
                continue;
            }

            if (inline.PreviousSibling is LiteralInline separator && separator.Content.ToString().Contains('·', StringComparison.Ordinal))
            {
                separator.Remove();
            }

            inline.Remove();
        }
    }

    private static bool IsSources(Block block, WeeklyIssue issue) =>
        block is ParagraphBlock paragraph && PlainText(paragraph).StartsWith(issue.Language.SourcesLabel, StringComparison.Ordinal);

    private static bool IsWhy(Block block, WeeklyIssue issue) =>
        block is ParagraphBlock { Inline.FirstChild: EmphasisInline emphasis }
        && PlainText(emphasis).StartsWith(issue.Language.WhyLabel.TrimEnd(':'), StringComparison.OrdinalIgnoreCase);

    private static bool IsOnlyEmphasis(Block block) =>
        block is ParagraphBlock { Inline: { FirstChild: EmphasisInline first } inline } && inline.LastChild == first;

    private static string PlainText(ParagraphBlock paragraph) =>
        paragraph.Inline is null ? string.Empty : PlainText(paragraph.Inline);

    private static string PlainText(ContainerInline container) =>
        string.Concat(container.Descendants<LiteralInline>().Select(literal => literal.Content.ToString()));

    private static string DisplayTitle(string title) =>
        title.StartsWith(TitlePrefix, StringComparison.Ordinal) && title.Length > TitlePrefix.Length ? title[TitlePrefix.Length..] : title;

    private static string IssueNumber(string id) =>
        id.Length >= 7 && int.TryParse(id.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var week)
            ? week.ToString(CultureInfo.InvariantCulture)
            : id;

    private static string FormatDate(DateOnly date, bool turkish) =>
        string.Create(CultureInfo.InvariantCulture, $"{date.Day} {(turkish ? TurkishMonths : EnglishMonths)[date.Month - 1]} {date.Year}");

    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
