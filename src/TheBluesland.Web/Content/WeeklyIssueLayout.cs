using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace TheBluesland.Web.Content;

/// <summary>
/// Turns a Weekly issue body (Markdown) into the structured HTML /weekly shows (design pass, DD-2):
/// the intro becomes the lede, every <c>## headline</c> opens a <c>section.weekly-story</c>, and the
/// "why it matters", "sources", outro and AI-note paragraphs get their own classes. It works on the
/// Markdig AST (raw HTML stays disabled) and the result still goes through the curator-note URL
/// sanitiser, so the guarantees of <see cref="CuratorNoteRenderer"/> hold. Pure function.
/// </summary>
public static partial class WeeklyIssueLayout
{
    private const int WordsPerMinute = 200;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    private static readonly string[] WhyLabels = ["Neden önemli", "Why it matters"];
    private static readonly string[] SourcesLabels = ["Kaynaklar:", "Sources:"];

    public sealed record Result(string Html, string Lede, IReadOnlyList<string> Headlines, int ReadMinutes);

    public static Result Build(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        var blocks = document.ToList();
        foreach (var block in blocks)
        {
            document.Remove(block);
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);

        var words = 0;
        var headlines = new List<string>();
        var lede = string.Empty;
        var index = 0;

        // Intro: everything before the first "##" heading.
        var firstLede = true;
        while (index < blocks.Count && !IsStoryHeading(blocks[index]))
        {
            var block = blocks[index++];
            if (block is ParagraphBlock && firstLede)
            {
                block.GetAttributes().AddClass("weekly-lede");
                lede = FirstSentence(PlainText(block));
                firstLede = false;
            }

            words += CountWords(block);
            renderer.Render(block);
        }

        // Stories: heading plus its paragraphs, up to and including the sources paragraph.
        while (index < blocks.Count && IsStoryHeading(blocks[index]))
        {
            var heading = blocks[index++];
            headlines.Add(PlainText(heading));
            words += CountWords(heading);
            writer.Write("<section class=\"weekly-story\">\n");
            renderer.Render(heading);

            while (index < blocks.Count && !IsStoryHeading(blocks[index]))
            {
                var block = blocks[index++];
                if (IsSources(block))
                {
                    block.GetAttributes().AddClass("weekly-sources");
                    renderer.Render(block);
                    break;
                }

                if (IsWhy(block))
                {
                    block.GetAttributes().AddClass("weekly-why");
                }

                words += CountWords(block);
                renderer.Render(block);
            }

            writer.Write("</section>\n");
        }

        // Closing: outro paragraphs, then the AI note (a paragraph that is one emphasis, last).
        for (; index < blocks.Count; index++)
        {
            var block = blocks[index];
            var isAiNote = index == blocks.Count - 1 && IsOnlyEmphasis(block);
            block.GetAttributes().AddClass(isAiNote ? "weekly-ai-note" : "weekly-outro");
            if (!isAiNote)
            {
                words += CountWords(block);
            }

            renderer.Render(block);
        }

        writer.Flush();
        var minutes = Math.Max(1, (int)Math.Round(words / (double)WordsPerMinute, MidpointRounding.AwayFromZero));
        return new Result(CuratorNoteRenderer.StripDangerousUrlSchemes(writer.ToString()), lede, headlines, minutes);
    }

    private static bool IsStoryHeading(Block block) => block is HeadingBlock { Level: 2 };

    private static bool IsSources(Block block) =>
        block is ParagraphBlock && SourcesLabels.Any(label => PlainText(block).StartsWith(label, StringComparison.Ordinal));

    private static bool IsWhy(Block block) =>
        block is ParagraphBlock { Inline.FirstChild: EmphasisInline emphasis }
        && WhyLabels.Any(label => PlainText(emphasis).StartsWith(label, StringComparison.OrdinalIgnoreCase));

    private static bool IsOnlyEmphasis(Block block) =>
        block is ParagraphBlock { Inline: { FirstChild: EmphasisInline first } inline } && inline.LastChild == first;

    private static string PlainText(Block block) =>
        block is LeafBlock { Inline: { } inline } ? PlainText(inline) : string.Empty;

    private static string PlainText(ContainerInline container) =>
        string.Concat(container.Descendants().Select(node => node switch
        {
            LiteralInline literal => literal.Content.ToString(),
            CodeInline code => code.Content,
            LineBreakInline => " ",
            _ => string.Empty,
        }));

    private static int CountWords(Block block) =>
        PlainText(block).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string FirstSentence(string text)
    {
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var end = SentenceEnd().Match(collapsed);
        return end.Success ? collapsed[..(end.Index + 1)] : collapsed;
    }

    [GeneratedRegex(@"[.!?…](?=\s|$)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SentenceEnd();
}
