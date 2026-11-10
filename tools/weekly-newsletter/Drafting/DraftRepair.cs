using System.Text.Json;

namespace TheBluesland.WeeklyNewsletter.Drafting;

public enum RepairStatus
{
    /// <summary>No fixable warning; no extra request was made.</summary>
    NotNeeded,

    /// <summary>The repaired answer parsed and carries fewer warnings; it replaced the original.</summary>
    Accepted,

    /// <summary>The repaired answer parsed but carries as many or more warnings; the original stays.</summary>
    Rejected,

    /// <summary>The repair request or its answer failed; the original stays.</summary>
    Failed,
}

/// <summary>What the self-repair pass did for one language, for the PR body and the job summary.</summary>
/// <param name="Before">Warnings of the original draft.</param>
/// <param name="After">Warnings of the repaired answer (equals <paramref name="Before"/> when none was made or it failed).</param>
/// <param name="Failure">Why the repair call failed (one line), when <see cref="RepairStatus.Failed"/>.</param>
public sealed record RepairReport(RepairStatus Status, int Before, int After, string? Failure = null);

/// <summary>
/// The self-repair pass (issue-drafting spec, Design section 3a): when the validated draft carries
/// warnings the model can fix by rewriting text, ONE more request hands the model its own answer, the
/// fact card and a plain list of those warnings. The repaired answer replaces the original only if it
/// parses and has strictly fewer warnings. A failed repair never fails the run.
/// </summary>
public static class DraftRepair
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>
    /// True for warnings a text rewrite can remove: why-it-matters that repeats the body, stock phrases,
    /// sample-issue copies, stale tense or time wording, and a subject or preview that is too long, lacks the tag or repeats.
    /// Missing backups, TR/EN parity and omissions are not repaired here.
    /// </summary>
    public static bool IsFixable(DraftWarning warning)
    {
        var message = warning.Message;
        return message.StartsWith("why it matters repeats", StringComparison.Ordinal)
            || message.StartsWith("generic phrase", StringComparison.Ordinal)
            || message.Contains("copies the voice guide's sample issue", StringComparison.Ordinal)
            || message.StartsWith("time wording", StringComparison.Ordinal)
            || message.Contains("but the text uses future tense", StringComparison.Ordinal)
            || message.Contains("before the send date; write it as already", StringComparison.Ordinal)
            || (warning.Where is "subject" or "preview"
                && (message.Contains($"(limit {DraftLanguage.MaxSubject})", StringComparison.Ordinal)
                    || message.Contains($"(limit {DraftLanguage.MaxPreview})", StringComparison.Ordinal)
                    || message.Contains("does not end with", StringComparison.Ordinal)
                    || message.Contains("repeats the subject", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Makes at most one request through <paramref name="generate"/> (prompt in, JSON text out). Cancellation
    /// by the caller propagates; every other failure is reported as <see cref="RepairStatus.Failed"/>.
    /// </summary>
    public static async Task<(DraftResponse Text, IReadOnlyList<DraftWarning> Warnings, RepairReport Report)> RunAsync(
        Func<string, CancellationToken, Task<string>> generate,
        DraftResponse current,
        IReadOnlyList<DraftWarning> warnings,
        FactCard card,
        DraftLanguage language,
        DateOnly sendDate,
        string sampleText,
        CancellationToken cancellationToken)
    {
        var fixable = warnings.Where(IsFixable).ToList();
        if (fixable.Count == 0)
        {
            return (current, warnings, new RepairReport(RepairStatus.NotNeeded, warnings.Count, warnings.Count));
        }

        try
        {
            var prompt = DraftPromptBuilder.BuildRepair(language, card, sendDate, ToJson(current, card), fixable);
            var repaired = DraftResponse.Parse(await generate(prompt, cancellationToken), card);
            var (clean, repairedWarnings) = DraftValidator.Validate(repaired, card, language, sendDate, sampleText);
            return repairedWarnings.Count < warnings.Count
                ? (clean, repairedWarnings, new RepairReport(RepairStatus.Accepted, warnings.Count, repairedWarnings.Count))
                : (current, warnings, new RepairReport(RepairStatus.Rejected, warnings.Count, repairedWarnings.Count));
        }
        catch (Exception ex) when ((ex is HttpRequestException or DraftFormatException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return (current, warnings, new RepairReport(RepairStatus.Failed, warnings.Count, warnings.Count, OneLine(ex.Message)));
        }
    }

    /// <summary>The answer in the model's own schema; backups the model skipped (card text filled in by the code) are left out.</summary>
    private static string ToJson(DraftResponse draft, FactCard card) =>
        JsonSerializer.Serialize(
            new
            {
                intro = draft.Intro,
                outro = draft.Outro,
                subject = draft.Subject,
                preview = draft.Preview,
                stories = draft.Stories
                    .Where(story => story.WhyItMatters != DraftResponse.MissingBackupNote && card.Stories.Any(known => known.Key == story.Key))
                    .Select(story => new { key = story.Key, headline = story.Headline, body = story.Body, whyItMatters = story.WhyItMatters }),
            },
            Json);

    private static string OneLine(string message)
    {
        var flat = string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > 200 ? flat[..199] + "…" : flat;
    }
}
