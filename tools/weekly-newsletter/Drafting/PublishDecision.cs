using System.Text.Json;
using TheBluesland.WeeklyNewsletter.Reporting;

namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>
/// Whether the draft workflow may enable auto-merge on the week's PR (issue-drafting spec, Design section 3b,
/// issue-review spec). Both languages must pass the final checker and carry no blocking validator problem;
/// a draft written by the lighter fallback model never auto-merges. Anything else leaves the PR open for
/// Mehmet with the label <c>needs-human</c>; no merge means no send that week (ADR-0006 madde 2).
/// </summary>
/// <param name="Reasons">Why not (Turkish, one line each, model text escaped); empty when <paramref name="AutoMerge"/>.</param>
public sealed record PublishDecision(bool AutoMerge, IReadOnlyList<string> Reasons)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static PublishDecision Decide(IReadOnlyList<LanguageDraft> drafts, FactCard card)
    {
        var reasons = new List<string>();
        foreach (var draft in drafts)
        {
            var code = draft.Language.Code.ToUpperInvariant();
            reasons.AddRange(Problems(draft, card).Select(problem => $"{code}: {problem}"));
            if (draft.FellBackFrom is not null)
            {
                reasons.Add($"{code}: daha hafif yedek modelle (`{draft.Model}`) yazıldı; otomatik yayın yok.");
            }
        }

        return new PublishDecision(reasons.Count == 0, reasons);
    }

    /// <summary>The checker and validator problems of one language that stop publishing; empty when it may go out.</summary>
    public static IReadOnlyList<string> Problems(LanguageDraft draft, FactCard card)
    {
        var problems = new List<string>();
        switch (draft.Check)
        {
            case null:
                problems.Add("son kontrol çalışmadı.");
                break;
            case { Failure: { } failure }:
                problems.Add($"son kontrolcü yanıt veremedi, geçmedi sayıldı ({MarkdownText.Cell(failure)}).");
                break;
            case { Passed: false } check:
                problems.Add($"son kontrol geçmedi: {string.Join(" ", check.Problems.Select(problem => MarkdownText.Cell(problem)))}");
                break;
        }

        foreach (var warning in BlockingWarnings(draft, card))
        {
            problems.Add($"engelleyen doğrulama uyarısı: {MarkdownText.Cell(warning.Where)} · {MarkdownText.Cell(warning.Message)}");
        }

        return problems;
    }

    /// <summary>Blocking warnings in the published text; backups are never published, so theirs do not count.</summary>
    public static IEnumerable<DraftWarning> BlockingWarnings(LanguageDraft draft, FactCard card)
    {
        var backups = card.Backups.Select(story => story.Key).ToHashSet(StringComparer.Ordinal);
        return draft.Warnings.Where(warning => IsBlocking(warning) && !backups.Contains(warning.Where));
    }

    /// <summary>
    /// Warnings that point at content which may be wrong or copied, as opposed to style: a number the card does not
    /// state, time wording or tense the card contradicts, a copied summary or sample text. Proper names are left to the
    /// checker (the name heuristic also flags ordinary capitalized words); style warnings never block.
    /// </summary>
    public static bool IsBlocking(DraftWarning warning)
    {
        var message = warning.Message;
        return (message.Contains(": number “", StringComparison.Ordinal) && message.EndsWith("is not in the fact card.", StringComparison.Ordinal))
            || message.StartsWith("time wording", StringComparison.Ordinal)
            || message.Contains("but the text uses future tense", StringComparison.Ordinal)
            || message.Contains("write it as already announced/happened", StringComparison.Ordinal)
            || message.Contains(": copies a summary", StringComparison.Ordinal)
            || message.Contains("copies the voice guide's sample issue", StringComparison.Ordinal);
    }

    /// <summary>The file the workflow reads: <c>{"autoMerge": bool, "reasons": [..]}</c>.</summary>
    public string ToJson() => JsonSerializer.Serialize(new { autoMerge = AutoMerge, reasons = Reasons }, Json);

    /// <summary>The PR-body paragraph (Turkish) that tells Mehmet whether the draft publishes itself.</summary>
    public string ToMarkdown() => AutoMerge
        ? "**Otomatik yayın: evet.** Her iki dil son kontrolden geçti; CI yeşil olunca PR kendiliğinden merge edilir (token tanımlıysa).\n\n"
        : $"**Otomatik yayın: hayır — `needs-human`.** Bu PR merge edilmezse bu hafta hiçbir dil gitmez.\n\n{string.Concat(Reasons.Select(reason => $"- {reason}\n"))}\n";
}
