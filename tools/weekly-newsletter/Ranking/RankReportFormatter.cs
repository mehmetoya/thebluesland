using System.Globalization;
using System.Text;
using TheBluesland.WeeklyNewsletter.Reporting;

namespace TheBluesland.WeeklyNewsletter.Ranking;

/// <summary>
/// Renders the week's pick as job-summary Markdown (story-ranking spec, Design section 5): picks and
/// backups with a "why" column (sources, type bonus, score), the genre-slot decision, and the
/// stories passed over by diversity rules, folded away.
/// </summary>
public static class RankReportFormatter
{
    public static string ToMarkdown(
        RankingResult result,
        int storyCount,
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyDictionary<string, string> sourceNames)
    {
        var markdown = new StringBuilder();
        markdown.Append(CultureInfo.InvariantCulture, $"## Weekly pick · {from.UtcDateTime:yyyy-MM-dd HH:mm} → {to.UtcDateTime:yyyy-MM-dd HH:mm} UTC\n\n");

        if (result.Picks.Count == 0)
        {
            markdown.Append("No stories to pick in this window.\n");
            return markdown.ToString();
        }

        markdown.Append(CultureInfo.InvariantCulture,
            $"{storyCount} stories · {result.Picks.Count} picked + {result.Backups.Count} backups · genre slot: {MarkdownText.Cell(result.GenreSlot)}\n\n");
        markdown.Append("| # | Story | Sources | Why |\n");
        markdown.Append("|---|---|---|---|\n");
        for (var index = 0; index < result.Picks.Count; index++)
        {
            Row(markdown, (index + 1).ToString(CultureInfo.InvariantCulture), result.Picks[index], sourceNames);
        }

        for (var index = 0; index < result.Backups.Count; index++)
        {
            Row(markdown, $"B{index + 1}", result.Backups[index], sourceNames);
        }

        if (result.Skipped.Count > 0)
        {
            markdown.Append(CultureInfo.InvariantCulture, $"\n<details><summary>Passed over ({result.Skipped.Count})</summary>\n\n");
            foreach (var skipped in result.Skipped)
            {
                markdown.Append(CultureInfo.InvariantCulture,
                    $"- {MarkdownText.Link(skipped.Story.Story.Articles[0].Title, skipped.Story.Story.Articles[0].Url)} — {MarkdownText.Cell(skipped.Reason)}\n");
            }

            markdown.Append("\n</details>\n");
        }

        return markdown.ToString();
    }

    /// <summary>"3 sources · obituary +1.5 = 4.50" - the reason a story is where it is.</summary>
    public static string Why(RankedStory story)
    {
        var sources = story.Story.SourceCount == 1 ? "1 source" : $"{story.Story.SourceCount} sources";
        var bonus = story.Bonus > 0 ? string.Create(CultureInfo.InvariantCulture, $" · {story.Type} +{story.Bonus:0.##}") : $" · {story.Type}";
        return string.Create(CultureInfo.InvariantCulture, $"{sources}{bonus} = {story.Score:0.00}");
    }

    private static void Row(StringBuilder markdown, string label, RankedStory story, IReadOnlyDictionary<string, string> sourceNames)
    {
        var lead = story.Story.Articles[0];
        var sources = string.Join(" · ", story.Story.Sources.Select(source => MarkdownText.Cell(sourceNames.GetValueOrDefault(source, source))));
        markdown.Append(CultureInfo.InvariantCulture, $"| {label} | {MarkdownText.Link(lead.Title, lead.Url)} | {sources} | {Why(story)} |\n");
    }
}
