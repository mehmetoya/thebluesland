using System.Globalization;
using System.Text;
using TheBluesland.WeeklyNewsletter.Reporting;

namespace TheBluesland.WeeklyNewsletter.Clustering;

/// <summary>
/// Renders a week's stories as GitHub job-summary Markdown (story-clustering spec, Design section 4).
/// One headline line, then the multi-source stories as a table (most sources first), then each of
/// them with its member headlines so a wrong merge is visible at a glance, then the single-source
/// stories folded away.
/// </summary>
public static class ClusterReportFormatter
{
    public static string ToMarkdown(
        IReadOnlyList<Story> stories,
        DateTimeOffset from,
        DateTimeOffset to,
        ClusteringOptions options,
        IReadOnlyDictionary<string, string> sourceNames)
    {
        var articleCount = stories.Sum(story => story.Articles.Count);
        var covered = stories.Where(story => story.SourceCount > 1).ToList();
        var single = stories.Where(story => story.SourceCount == 1).ToList();

        var markdown = new StringBuilder();
        markdown.Append(CultureInfo.InvariantCulture, $"## Story clusters · {from.UtcDateTime:yyyy-MM-dd HH:mm} → {to.UtcDateTime:yyyy-MM-dd HH:mm} UTC\n\n");

        if (articleCount == 0)
        {
            markdown.Append("No articles in this window.\n");
            return markdown.ToString();
        }

        markdown.Append(CultureInfo.InvariantCulture,
            $"{articleCount} articles → {stories.Count} stories · {covered.Count} covered by 2+ sources · TF-IDF, threshold {options.SimilarityThreshold.ToString("0.00", CultureInfo.InvariantCulture)}\n\n");

        if (covered.Count > 0)
        {
            markdown.Append("| # | Story (earliest headline) | Sources | Articles |\n");
            markdown.Append("|---:|---|---|---:|\n");
            for (var index = 0; index < covered.Count; index++)
            {
                var story = covered[index];
                markdown.Append(CultureInfo.InvariantCulture,
                    $"| {index + 1} | {MarkdownText.Link(story.Articles[0].Title, story.Articles[0].Url)} | {SourceList(story, sourceNames)} | {story.Articles.Count} |\n");
            }

            markdown.Append("\n### Members\n\n");
            for (var index = 0; index < covered.Count; index++)
            {
                markdown.Append(CultureInfo.InvariantCulture, $"**{index + 1}.** {MarkdownText.Cell(covered[index].Articles[0].Title)}\n");
                foreach (var article in covered[index].Articles)
                {
                    markdown.Append(CultureInfo.InvariantCulture,
                        $"- {MarkdownText.Cell(Name(article.Source, sourceNames))} · {article.EffectiveAt.UtcDateTime:MM-dd HH:mm} · {MarkdownText.Link(article.Title, article.Url)}\n");
                }

                markdown.Append('\n');
            }
        }

        if (single.Count > 0)
        {
            markdown.Append(CultureInfo.InvariantCulture, $"<details><summary>Single-source stories ({single.Count})</summary>\n\n");
            foreach (var story in single)
            {
                var article = story.Articles[0];
                var more = story.Articles.Count > 1 ? $" (+{story.Articles.Count - 1} more from the same source)" : string.Empty;
                markdown.Append(CultureInfo.InvariantCulture,
                    $"- {MarkdownText.Cell(Name(article.Source, sourceNames))} · {MarkdownText.Link(article.Title, article.Url)}{more}\n");
            }

            markdown.Append("\n</details>\n");
        }

        return markdown.ToString();
    }

    private static string SourceList(Story story, IReadOnlyDictionary<string, string> sourceNames) =>
        string.Join(" · ", story.Sources.Select(source => MarkdownText.Cell(Name(source, sourceNames))));

    private static string Name(string source, IReadOnlyDictionary<string, string> sourceNames) =>
        sourceNames.GetValueOrDefault(source, source);
}
