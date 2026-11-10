using System.Globalization;
using System.Text;
using TheBluesland.WeeklyNewsletter.Reporting;

namespace TheBluesland.WeeklyNewsletter.Delivery;

/// <summary>The job summary of a delivery run: what Kit will send, when, and what was left out.</summary>
public static class DeliveryReportFormatter
{
    public static string ToMarkdown(IReadOnlyList<DeliveryAction> actions, DateTimeOffset now, TimeZoneInfo zone, bool dryRun, string? failure = null)
    {
        var markdown = new StringBuilder();

        // A late merge is a decision, so the run stays green; the banner keeps it from going unnoticed.
        // Only the issue id and the two-letter language code are printed, never issue text.
        foreach (var late in actions.Where(action => action.Kind == DeliveryActionKind.Late))
        {
            var parts = late.Marker[DeliveryPlanner.MarkerPrefix.Length..].Split(':');
            markdown.Append(CultureInfo.InvariantCulture, $"**⚠️ LATE — {MarkdownText.Cell(parts[^1])} will not be sent this week** ({MarkdownText.Cell(parts[0])} merged after the cut-off)\n\n");
        }

        markdown.Append(CultureInfo.InvariantCulture, $"## Weekly delivery · {now.UtcDateTime:yyyy-MM-dd HH:mm} UTC{(dryRun ? " · dry run (nothing sent to Kit)" : string.Empty)}\n\n");
        if (actions.Count == 0)
        {
            markdown.Append("No issue to schedule: content/weekly has nothing due in the coming week.\n");
        }
        else
        {
            markdown.Append("| Issue | Sends at | What happens | Kit broadcast |\n|---|---|---|---|\n");
            foreach (var action in actions)
            {
                var sendsAt = action.SendAt is { } at ? TimeZoneInfo.ConvertTime(at, zone).ToString("ddd yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "—";
                markdown.Append(CultureInfo.InvariantCulture,
                    $"| {MarkdownText.Cell(action.Marker[DeliveryPlanner.MarkerPrefix.Length..])} | {sendsAt} | {Describe(action.Kind, dryRun)} | {action.BroadcastId?.ToString(CultureInfo.InvariantCulture) ?? "—"} |\n");
            }
        }

        if (failure is not null)
        {
            markdown.Append(CultureInfo.InvariantCulture, $"\n**Stopped:** {MarkdownText.Cell(failure)}\n");
        }

        return markdown.ToString();
    }

    private static string Describe(DeliveryActionKind kind, bool dryRun) => (kind, dryRun) switch
    {
        (DeliveryActionKind.Create, false) => "✅ scheduled",
        (DeliveryActionKind.Create, true) => "would schedule",
        (DeliveryActionKind.Update, false) => "✅ updated (main changed)",
        (DeliveryActionKind.Update, true) => "would update (main changed)",
        (DeliveryActionKind.Unchanged, _) => "scheduled, unchanged",
        (DeliveryActionKind.Locked, _) => "sending, sent or stopped in Kit; left alone",
        (DeliveryActionKind.Late, _) => "⚠️ merged after the cut-off; not sent this week",
        (DeliveryActionKind.Cancel, false) => "🗑️ cancelled (file left main)",
        (DeliveryActionKind.Cancel, true) => "would cancel (file left main)",
        (DeliveryActionKind.DeleteDuplicate, false) => "🗑️ duplicate removed (the older broadcast stays)",
        (DeliveryActionKind.DeleteDuplicate, true) => "would remove this duplicate (the older broadcast stays)",
        (DeliveryActionKind.Conflict, _) => "❌ two Kit broadcasts carry this marker and one is already sending or sent; nothing was changed",
        _ => kind.ToString(),
    };
}
