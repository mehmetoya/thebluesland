using System.Globalization;
using System.Text;
using TheBluesland.WeeklyNewsletter.Reporting;

namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <param name="FellBackFrom">The default model that was unavailable, when <paramref name="Model"/> is the fallback.</param>
/// <param name="Repair">What the self-repair pass did; null when it was not part of the run.</param>
public sealed record LanguageDraft(
    DraftLanguage Language,
    string IssueMarkdown,
    string BackupsMarkdown,
    DraftResponse Text,
    IReadOnlyList<DraftWarning> Warnings,
    string Model,
    string? FellBackFrom = null,
    RepairReport? Repair = null);

/// <summary>
/// The job summary for a draft run: which issue, the warnings Mehmet should check (story by
/// story), then each language's issue and backups folded away as plain Markdown to read.
/// </summary>
public static class DraftReportFormatter
{
    /// <summary>Marks a draft written by the fallback model; the workflow also puts it in the PR title.</summary>
    public const string LowModelMarker = "[DÜŞÜK MODEL]";

    /// <summary>True when any language was written by the fallback model, even after the default's extra try.</summary>
    public static bool UsedFallback(IReadOnlyList<LanguageDraft> drafts) => drafts.Any(draft => draft.FellBackFrom is not null);

    /// <summary>The low-model notice (Turkish, for Mehmet), or empty when the default model wrote every language.</summary>
    public static string LowModelNotice(IReadOnlyList<LanguageDraft> drafts)
    {
        if (!UsedFallback(drafts))
        {
            return string.Empty;
        }

        var languages = string.Join(", ", drafts.Where(draft => draft.FellBackFrom is not null).Select(draft => $"{draft.Language.Code.ToUpperInvariant()} (`{draft.Model}`)"));
        return $"> ⚠️ **{LowModelMarker}** Varsayılan model iki denemede de yanıt vermedi; şu diller daha hafif modelle yazıldı: {languages}. Metni normalden dikkatli oku ya da taslağı varsayılan modelle yeniden üret.\n\n";
    }

    public static string ToMarkdown(IssueWeek week, FactCard card, IReadOnlyList<LanguageDraft> drafts)
    {
        var markdown = new StringBuilder();
        markdown.Append(CultureInfo.InvariantCulture,
            $"## Weekly draft · {week.Id} · send {week.SendDate:yyyy-MM-dd}\n\n");
        markdown.Append(CultureInfo.InvariantCulture,
            $"Window {week.From.UtcDateTime:yyyy-MM-dd HH:mm} → {week.To.UtcDateTime:yyyy-MM-dd HH:mm} UTC · {card.Picks.Count()} stories + {card.Backups.Count()} backups · {string.Join(" + ", drafts.Select(draft => draft.Language.Code.ToUpperInvariant()))}\n\n");

        markdown.Append(LowModelNotice(drafts));
        foreach (var draft in drafts)
        {
            var count = draft.Warnings.Count;
            markdown.Append(CultureInfo.InvariantCulture,
                $"###{draft.Language.Code.ToUpperInvariant()} · {(count == 0 ? "✅ no warnings" : $"⚠️ {count} to check")}\n\n");
            markdown.Append(CultureInfo.InvariantCulture, $"Written by `{draft.Model}`");
            markdown.Append(draft.FellBackFrom is { } busy
                ? string.Create(CultureInfo.InvariantCulture, $" because `{busy}` was unavailable (busy or out of free quota).\n\n")
                : ".\n\n");
            markdown.Append(RepairLine(draft.Repair, turkish: false));
            markdown.Append(WarningsTable(card, draft.Warnings));

            markdown.Append(CultureInfo.InvariantCulture, $"<details><summary>{draft.Language.Code.ToUpperInvariant()} issue</summary>\n\n");
            markdown.Append(StripFrontMatter(draft.IssueMarkdown)).Append("\n</details>\n\n");
            markdown.Append(CultureInfo.InvariantCulture, $"<details><summary>{draft.Language.Code.ToUpperInvariant()} backups</summary>\n\n");
            markdown.Append(StripFrontMatter(draft.BackupsMarkdown)).Append("\n</details>\n\n");
        }

        return markdown.ToString();
    }

    /// <summary>
    /// The self-repair outcome as one line ending in a blank line (issue-drafting spec, Design section 3a),
    /// "Otomatik düzeltme: 7 uyarı → 3 uyarı" for Mehmet's PR body (<paramref name="turkish"/>) or its English twin for the job summary; empty when there is no report.
    /// </summary>
    public static string RepairLine(RepairReport? repair, bool turkish)
    {
        if (repair is null)
        {
            return string.Empty;
        }

        var (label, unit, none, kept, failed) = turkish
            ? ("Otomatik düzeltme", "uyarı", "gerekmedi", "ilk taslak korundu", "düzeltme isteği başarısız")
            : ("Automatic repair", "warnings", "not needed", "original draft kept", "repair request failed");
        var change = string.Create(CultureInfo.InvariantCulture, $"{repair.Before} {unit} → {repair.After} {unit}");
        var text = repair.Status switch
        {
            RepairStatus.NotNeeded => none,
            RepairStatus.Accepted => change,
            RepairStatus.Rejected => $"{change}; {kept}",
            _ => $"{failed} ({MarkdownText.Cell(repair.Failure)}); {kept}",
        };
        return $"{label}: {text}\n\n";
    }

    /// <summary>The warnings as a "where / what to check" table, the story named by its lead headline; empty when there are none.</summary>
    public static string WarningsTable(FactCard card, IReadOnlyList<DraftWarning> warnings)
    {
        if (warnings.Count == 0)
        {
            return string.Empty;
        }

        var headline = card.Stories.ToDictionary(story => story.Key, story => story.Articles[0].Title);
        var markdown = new StringBuilder("| Where | What to check |\n|---|---|\n");
        foreach (var warning in warnings)
        {
            var where = headline.TryGetValue(warning.Where, out var title) ? title : warning.Where;
            markdown.Append(CultureInfo.InvariantCulture, $"| {MarkdownText.Cell(where)} | {MarkdownText.Cell(warning.Message)} |\n");
        }

        return markdown.Append('\n').ToString();
    }

    private static string StripFrontMatter(string markdown)
    {
        var end = markdown.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        return end < 0 ? markdown : markdown[(end + 5)..].TrimStart('\n');
    }
}
