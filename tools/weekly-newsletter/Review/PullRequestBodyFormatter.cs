using System.Globalization;
using System.Text;
using TheBluesland.WeeklyNewsletter.Drafting;
using TheBluesland.WeeklyNewsletter.Reporting;

namespace TheBluesland.WeeklyNewsletter.Review;

/// <summary>
/// The bot PR's description (issue-review spec, Design section 6): how to approve, what to check,
/// why each story was picked, which sources every paragraph rests on (ADR-0006 madde 3) and the
/// backups ready to paste. Written in Turkish for Mehmet; model text is escaped or fenced so it
/// cannot change the page.
/// </summary>
public static class PullRequestBodyFormatter
{
    /// <summary>GitHub refuses PR bodies over 65,536 characters; leave room for the run link footer.</summary>
    public const int MaxLength = 60_000;

    public static string ToMarkdown(IssueWeek week, FactCard card, IReadOnlyList<LanguageDraft> drafts, string rankReport)
    {
        var head = new StringBuilder();
        head.Append(CultureInfo.InvariantCulture, $"## TheBluesland Weekly · {week.Id}\n\n");
        head.Append(CultureInfo.InvariantCulture,
            $"Gönderim **{week.SendDate:yyyy-MM-dd}** pazartesi · TR 08:00, EN 15:00 (Europe/Istanbul) · pencere {week.From.UtcDateTime:yyyy-MM-dd HH:mm} → {week.To.UtcDateTime:yyyy-MM-dd HH:mm} UTC\n\n");
        head.Append("**Merge = onay.** Metni bu PR'da düzelt → **Ready for review** → CI yeşil → merge. ");
        head.Append("TR sayısı 08:00'e, EN sayısı 15:00'e kadar merge edilmezse o dil bu hafta gitmez.\n\n");
        head.Append("Bir başlığı değiştirirsen front matter'daki `headline` değerini de aynı yap; CI ikisini karşılaştırır.\n\n");

        head.Append("### Neye bakmalı\n\n");
        foreach (var draft in drafts)
        {
            var count = draft.Warnings.Count;
            head.Append(CultureInfo.InvariantCulture,
                $"#### {draft.Language.Code.ToUpperInvariant()} · {(count == 0 ? "✅ uyarı yok" : $"⚠️ {count} uyarı")} · `{draft.Model}`");
            head.Append(draft.FellBackFrom is { } busy ? $" (`{busy}` yanıt vermedi)\n\n" : "\n\n");
            head.Append(DraftReportFormatter.WarningsTable(card, draft.Warnings));
        }

        head.Append("### Seçim\n\n");
        head.Append(WithoutFirstLine(rankReport).Trim()).Append("\n\n");

        head.Append("### Paragrafların kaynakları\n\n");
        var lead = drafts[0];
        foreach (var story in card.Picks)
        {
            var sources = story.Links.Select(link =>
            {
                var titles = story.Articles.Where(article => article.SourceName == link.Name).Select(article => $"“{MarkdownText.Cell(article.Title)}”");
                return $"{MarkdownText.Link(link.Name, link.Url)}: {string.Join(", ", titles)}";
            });
            head.Append(CultureInfo.InvariantCulture,
                $"{story.Position}. **{MarkdownText.Cell(lead.Text.For(story.Key).Headline)}** — {string.Join(" · ", sources)}\n");
        }

        head.Append('\n');

        var backups = Backups(card, drafts);
        return head.Length + backups.Length <= MaxLength
            ? head.Append(backups).ToString()
            : head.Append("### Yedekler\n\nYedeklerin metni bu açıklamaya sığmadı; aşağıdaki çalışmanın `weekly-draft` artifact'inde, `review/` klasöründe.\n").ToString();
    }

    private static string Backups(FactCard card, IReadOnlyList<LanguageDraft> drafts)
    {
        var markdown = new StringBuilder("### Yedekler — yapıştırmaya hazır\n\n");
        markdown.Append("Bir haberi değiştirmek için her dil dosyasında o haberin `## ` bölümünü aşağıdaki bölümle, ");
        markdown.Append("front matter'daki `- key:` girdisini de aşağıdaki girdiyle değiştir.\n\n");
        foreach (var story in card.Backups)
        {
            markdown.Append(CultureInfo.InvariantCulture,
                $"<details><summary>Y{story.Position} · {MarkdownText.Cell(story.Articles[0].Title)} — {MarkdownText.Cell(story.Why)}</summary>\n\n");
            foreach (var draft in drafts)
            {
                var text = draft.Text.For(story.Key);
                var code = draft.Language.Code.ToUpperInvariant();
                markdown.Append(CultureInfo.InvariantCulture, $"**{code} bölüm**\n\n");
                markdown.Append(Fence("markdown", IssueMarkdownWriter.Section(draft.Language, text, story, heading: text.Headline)));
                markdown.Append(CultureInfo.InvariantCulture, $"**{code} front matter**\n\n");
                markdown.Append(Fence("yaml", IssueMarkdownWriter.FrontMatterEntry(story, text.Headline)));
            }

            markdown.Append("</details>\n\n");
        }

        return markdown.ToString();
    }

    /// <summary>A tilde fence longer than any tilde run inside, so the content cannot close it.</summary>
    private static string Fence(string language, string content)
    {
        var longest = 0;
        var run = 0;
        foreach (var character in content)
        {
            run = character == '~' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        var fence = new string('~', Math.Max(3, longest + 1));
        return $"{fence}{language}\n{content.TrimEnd('\n')}\n{fence}\n\n";
    }

    private static string WithoutFirstLine(string markdown)
    {
        var end = markdown.IndexOf('\n', StringComparison.Ordinal);
        return end < 0 ? string.Empty : markdown[(end + 1)..];
    }
}
