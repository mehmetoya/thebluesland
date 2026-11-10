using System.Globalization;
using System.Text;

namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>
/// Writes <c>content/weekly/&lt;issue&gt;.&lt;lang&gt;.md</c> and its backups side file (issue-drafting
/// spec, Design section 4). Front matter, headings, source links and the AI note come from code;
/// only the prose comes from the model. The file format is the contract issue-delivery and
/// weekly-archive read.
/// </summary>
public static class IssueMarkdownWriter
{
    public static string IssueFileName(IssueWeek week, DraftLanguage language) => $"{week.Id}.{language.Code}.md";

    public static string BackupsFileName(IssueWeek week, DraftLanguage language) => $"{week.Id}.backups.{language.Code}.md";

    public static string Title(IssueWeek week, DraftLanguage language) =>
        string.Format(CultureInfo.InvariantCulture, language.TitleFormat, week.Week);

    public static string Issue(IssueWeek week, DraftLanguage language, DraftResponse draft, FactCard card)
    {
        var picks = card.Picks.ToList();
        var markdown = new StringBuilder();
        markdown.Append("---\n");
        markdown.Append(CultureInfo.InvariantCulture, $"issue: {week.Id}\n");
        markdown.Append(CultureInfo.InvariantCulture, $"language: {language.Code}\n");
        markdown.Append(CultureInfo.InvariantCulture, $"title: {Quote(Title(week, language))}\n");
        markdown.Append(CultureInfo.InvariantCulture, $"sendDate: {week.SendDate:yyyy-MM-dd}\n");
        markdown.Append("window:\n");
        markdown.Append(CultureInfo.InvariantCulture, $"  from: {week.From.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\n");
        markdown.Append(CultureInfo.InvariantCulture, $"  to: {week.To.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\n");
        markdown.Append("aiAssisted: true\n");
        markdown.Append("stories:\n");
        foreach (var story in picks)
        {
            markdown.Append(CultureInfo.InvariantCulture, $"  - key: {story.Key}\n");
            markdown.Append(CultureInfo.InvariantCulture, $"    headline: {Quote(draft.For(story.Key).Headline)}\n");
            markdown.Append("    sources:\n");
            foreach (var link in story.Links)
            {
                markdown.Append(CultureInfo.InvariantCulture, $"      - name: {Quote(link.Name)}\n");
                markdown.Append(CultureInfo.InvariantCulture, $"        url: {Quote(link.Url)}\n");
            }
        }

        markdown.Append("---\n\n");
        markdown.Append(draft.Intro).Append("\n\n");
        foreach (var story in picks)
        {
            AppendStory(markdown, language, draft.For(story.Key), story, heading: draft.For(story.Key).Headline);
        }

        markdown.Append(draft.Outro).Append("\n\n");
        markdown.Append(CultureInfo.InvariantCulture, $"*{language.AiNote}*\n");
        return markdown.ToString();
    }

    public static string Backups(IssueWeek week, DraftLanguage language, DraftResponse draft, FactCard card)
    {
        var markdown = new StringBuilder();
        markdown.Append("---\n");
        markdown.Append(CultureInfo.InvariantCulture, $"backupsFor: {week.Id}\n");
        markdown.Append(CultureInfo.InvariantCulture, $"language: {language.Code}\n");
        markdown.Append("---\n\n");
        foreach (var story in card.Backups)
        {
            var text = draft.For(story.Key);
            AppendStory(markdown, language, text, story, heading: $"{language.BackupLabel} {story.Position} · {text.Headline}");
        }

        return markdown.ToString();
    }

    private static void AppendStory(StringBuilder markdown, DraftLanguage language, DraftStory text, FactStory story, string heading)
    {
        markdown.Append(CultureInfo.InvariantCulture, $"## {heading}\n\n");
        markdown.Append(text.Body).Append("\n\n");
        markdown.Append(CultureInfo.InvariantCulture, $"*{language.WhyLabel}* {text.WhyItMatters}\n\n");
        var links = string.Join(" · ", story.Links.Select(link => $"[{link.Name}]({LinkTarget(link.Url)})"));
        markdown.Append(CultureInfo.InvariantCulture, $"{language.SourcesLabel} {links}\n\n");
    }

    private static string LinkTarget(string url) =>
        url.Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal).Replace(" ", "%20", StringComparison.Ordinal);

    /// <summary>A YAML double-quoted scalar.</summary>
    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
