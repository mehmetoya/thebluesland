using System.Globalization;
using System.Text;

namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>
/// Writes <c>content/weekly/&lt;issue&gt;.&lt;lang&gt;.md</c> and its backups side file (issue-drafting
/// spec, Design section 4). Backups and the PR body go to a <see cref="ReviewDirectory"/> next to the
/// issues: they are read in the pull request and never merged (issue-review spec, Design section 5). Front matter, headings, source links and the AI note come from code;
/// only the prose comes from the model. The file format is the contract issue-delivery and
/// weekly-archive read.
/// </summary>
public static class IssueMarkdownWriter
{
    public static string IssueFileName(IssueWeek week, DraftLanguage language) => $"{week.Id}.{language.Code}.md";

    public const string ReviewDirectory = "review";

    public static string BackupsFileName(IssueWeek week, DraftLanguage language) => Path.Combine(ReviewDirectory, $"{week.Id}.backups.{language.Code}.md");

    public static string PullRequestBodyFileName(IssueWeek week) => Path.Combine(ReviewDirectory, $"{week.Id}.pr-body.md");

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
        if (draft.Subject is { Length: > 0 } subject)
        {
            markdown.Append(CultureInfo.InvariantCulture, $"subject: {Quote(FitSubject(subject))}\n");
        }

        if (draft.Preview is { Length: > 0 } preview)
        {
            markdown.Append(CultureInfo.InvariantCulture, $"preview: {Quote(Fit(preview, DraftLanguage.MaxPreview))}\n");
        }

        markdown.Append(CultureInfo.InvariantCulture, $"sendDate: {week.SendDate:yyyy-MM-dd}\n");
        markdown.Append("window:\n");
        markdown.Append(CultureInfo.InvariantCulture, $"  from: {week.From.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\n");
        markdown.Append(CultureInfo.InvariantCulture, $"  to: {week.To.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\n");
        markdown.Append("aiAssisted: true\n");
        markdown.Append("stories:\n");
        foreach (var story in picks)
        {
            markdown.Append(FrontMatterEntry(story, draft.For(story.Key).Headline));
        }

        markdown.Append("---\n\n");
        markdown.Append(draft.Intro).Append("\n\n");
        foreach (var story in picks)
        {
            markdown.Append(Section(language, draft.For(story.Key), story, heading: draft.For(story.Key).Headline));
        }

        // No AI note under the issue (Mehmet, 2026-11-10; ADR-0006 madde 4): aiAssisted stays in the
        // front matter as the internal record, and the editor's review is the human control.
        markdown.Append(draft.Outro).Append('\n');
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
            markdown.Append(Section(language, text, story, heading: $"{language.BackupLabel} {story.Position} · {text.Headline}"));
        }

        return markdown.ToString();
    }

    /// <summary>One story's entry under the front matter's <c>stories:</c> list.</summary>
    public static string FrontMatterEntry(FactStory story, string headline)
    {
        var markdown = new StringBuilder();
        markdown.Append(CultureInfo.InvariantCulture, $"  - key: {story.Key}\n");
        markdown.Append(CultureInfo.InvariantCulture, $"    headline: {Quote(headline)}\n");
        markdown.Append("    sources:\n");
        foreach (var link in story.Links)
        {
            markdown.Append(CultureInfo.InvariantCulture, $"      - name: {Quote(link.Name)}\n");
            markdown.Append(CultureInfo.InvariantCulture, $"        url: {Quote(link.Url)}\n");
        }

        return markdown.ToString();
    }

    /// <summary>One story's section: heading, body, why it matters and the source links.</summary>
    public static string Section(DraftLanguage language, DraftStory text, FactStory story, string heading)
    {
        var markdown = new StringBuilder();
        markdown.Append(CultureInfo.InvariantCulture, $"## {heading}\n\n");
        markdown.Append(text.Body).Append("\n\n");
        markdown.Append(CultureInfo.InvariantCulture, $"*{language.WhyLabel}* {text.WhyItMatters}\n\n");
        var links = string.Join(" · ", story.Links.Select(link => $"[{link.Name}]({LinkTarget(link.Url)})"));
        markdown.Append(CultureInfo.InvariantCulture, $"{language.SourcesLabel} {links}\n\n");
        return markdown.ToString();
    }

    private static string LinkTarget(string url) =>
        url.Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal).Replace(" ", "%20", StringComparison.Ordinal);

    /// <summary>A YAML double-quoted scalar.</summary>
    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// validate-weekly rejects a subject over 60 or a preview over 90 characters, which would block
    /// the PR's merge; the model sometimes runs over (2026-11-10: a 94-character preview). Cut at a
    /// word boundary and add "…"; the draft report still warns, so the editor sees it.
    /// </summary>
    public static string Fit(string text, int max)
    {
        text = text.Trim();
        if (text.Length <= max)
        {
            return text;
        }

        var head = text[..(max - 1)];
        var space = head.LastIndexOf(' ');
        return (space > max / 2 ? head[..space] : head).TrimEnd(' ', ',', ';', ':', '-', '·') + "…";
    }

    /// <summary>
    /// Like <see cref="Fit"/>, but keeps the brand tag at the end of the subject. When the hooks run
    /// over, the second hook is dropped: the cut falls at the last hook separator (", ", "; ", " ve ",
    /// " and ") that fits, without "…". Only a subject with no separator is cut mid-phrase.
    /// </summary>
    public static string FitSubject(string subject)
    {
        subject = subject.Trim();
        if (subject.Length <= DraftLanguage.MaxSubject)
        {
            return subject;
        }

        var tagged = subject.EndsWith(DraftLanguage.SubjectTag, StringComparison.Ordinal);
        var hooks = tagged ? subject[..^DraftLanguage.SubjectTag.Length] : subject;
        var tag = tagged ? DraftLanguage.SubjectTag : string.Empty;
        var room = DraftLanguage.MaxSubject - tag.Length;
        for (var cut = Math.Min(room, hooks.Length - 1); cut >= room / 3; cut--)
        {
            if (HookSeparators.Any(separator => string.CompareOrdinal(hooks, cut, separator, 0, separator.Length) == 0))
            {
                return hooks[..cut].TrimEnd() + tag;
            }
        }

        return Fit(hooks, room) + tag;
    }

    private static readonly string[] HookSeparators = [", ", "; ", " ve ", " and "];
}
