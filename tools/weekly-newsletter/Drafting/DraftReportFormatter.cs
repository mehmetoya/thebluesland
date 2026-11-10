using System.Globalization;
using System.Text;
using TheBluesland.WeeklyNewsletter.Reporting;

namespace TheBluesland.WeeklyNewsletter.Drafting;

public sealed record LanguageDraft(DraftLanguage Language, string IssueMarkdown, string BackupsMarkdown, IReadOnlyList<DraftWarning> Warnings);

/// <summary>
/// The job summary for a draft run: which issue, the warnings Mehmet should check (story by
/// story), then each language's issue and backups folded away as plain Markdown to read.
/// </summary>
public static class DraftReportFormatter
{
    public static string ToMarkdown(IssueWeek week, FactCard card, IReadOnlyList<LanguageDraft> drafts)
    {
        var markdown = new StringBuilder();
        markdown.Append(CultureInfo.InvariantCulture,
            $"## Weekly draft · {week.Id} · send {week.SendDate:yyyy-MM-dd}\n\n");
        markdown.Append(CultureInfo.InvariantCulture,
            $"Window {week.From.UtcDateTime:yyyy-MM-dd HH:mm} → {week.To.UtcDateTime:yyyy-MM-dd HH:mm} UTC · {card.Picks.Count()} stories + {card.Backups.Count()} backups · {string.Join(" + ", drafts.Select(draft => draft.Language.Code.ToUpperInvariant()))}\n\n");

        var headline = card.Stories.ToDictionary(story => story.Key, story => story.Articles[0].Title);
        foreach (var draft in drafts)
        {
            var count = draft.Warnings.Count;
            markdown.Append(CultureInfo.InvariantCulture,
                $"### {draft.Language.Code.ToUpperInvariant()} · {(count == 0 ? "✅ no warnings" : $"⚠️ {count} to check")}\n\n");
            if (count > 0)
            {
                markdown.Append("| Where | What to check |\n|---|---|\n");
                foreach (var warning in draft.Warnings)
                {
                    var where = headline.TryGetValue(warning.Where, out var title) ? title : warning.Where;
                    markdown.Append(CultureInfo.InvariantCulture, $"| {MarkdownText.Cell(where)} | {MarkdownText.Cell(warning.Message)} |\n");
                }

                markdown.Append('\n');
            }

            markdown.Append(CultureInfo.InvariantCulture, $"<details><summary>{draft.Language.Code.ToUpperInvariant()} issue</summary>\n\n");
            markdown.Append(StripFrontMatter(draft.IssueMarkdown)).Append("\n</details>\n\n");
            markdown.Append(CultureInfo.InvariantCulture, $"<details><summary>{draft.Language.Code.ToUpperInvariant()} backups</summary>\n\n");
            markdown.Append(StripFrontMatter(draft.BackupsMarkdown)).Append("\n</details>\n\n");
        }

        return markdown.ToString();
    }

    private static string StripFrontMatter(string markdown)
    {
        var end = markdown.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        return end < 0 ? markdown : markdown[(end + 5)..].TrimStart('\n');
    }
}
