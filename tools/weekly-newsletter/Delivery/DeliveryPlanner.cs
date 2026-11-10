using TheBluesland.WeeklyNewsletter.Drafting;

namespace TheBluesland.WeeklyNewsletter.Delivery;

/// <summary>A merged issue ready for Kit: its e-mail, list and send instant.</summary>
public sealed record PlannedIssue(string IssueId, DraftLanguage Language, DateTimeOffset SendAt, IssueEmail Email, long TagId)
{
    public string Marker => DeliveryPlanner.Marker(IssueId, Language.Code);
}

public enum DeliveryActionKind
{
    /// <summary>No broadcast yet and the send instant is far enough ahead: create and schedule it.</summary>
    Create,

    /// <summary>A scheduled broadcast whose text, list or time differs from main: update it.</summary>
    Update,

    /// <summary>Kit already holds exactly what main says.</summary>
    Unchanged,

    /// <summary>Sending, sent, aborted in Kit, or too close to its send instant to touch.</summary>
    Locked,

    /// <summary>Merged after the cut-off: this language does not go out this week (map rule).</summary>
    Late,

    /// <summary>A scheduled broadcast whose issue file left main (revert): delete it.</summary>
    Cancel,

    /// <summary>Two broadcasts carry the same marker and a human must look (one is already sending or sent): nothing is changed.</summary>
    Conflict,

    /// <summary>Two unsent broadcasts carry the same marker: the newer one is deleted, the older one carries on.</summary>
    DeleteDuplicate,
}

public sealed record DeliveryAction(DeliveryActionKind Kind, string Marker, DateTimeOffset? SendAt, long? BroadcastId, PlannedIssue? Issue, long? EmailTemplateId = null);

/// <summary>
/// The issue-delivery spec's section 3 table as a pure function: main's issue files against Kit's
/// marked broadcasts at one instant. Deterministic, no I/O. Anything older than a week is left out
/// so past issues do not fill every report.
/// </summary>
public static class DeliveryPlanner
{
    public const string MarkerPrefix = "thebluesland-weekly:";
    private static readonly TimeSpan ReportWindow = TimeSpan.FromDays(7);
    private static readonly HashSet<string> Mutable = new(["draft", "scheduled"], StringComparer.Ordinal);

    public static string Marker(string issueId, string languageCode) => $"{MarkerPrefix}{issueId}:{languageCode}";

    public static IReadOnlyList<DeliveryAction> Plan(
        IReadOnlyList<PlannedIssue> issues,
        IReadOnlyList<KitBroadcast> broadcasts,
        DateTimeOffset now,
        TimeSpan minimumLead)
    {
        var marked = broadcasts
            .Where(broadcast => broadcast.Description?.StartsWith(MarkerPrefix, StringComparison.Ordinal) == true)
            .GroupBy(broadcast => broadcast.Description!.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(broadcast => broadcast.Id).ToList(), StringComparer.Ordinal);

        var cutOff = now + minimumLead;
        bool CanChange(KitBroadcast broadcast) =>
            broadcast.Status is { } status && Mutable.Contains(status) && (broadcast.SendAt is null || broadcast.SendAt > cutOff);

        // Duplicates (a retried POST that Kit had already accepted) are only safe to remove while every
        // copy is still unsent; if one is sending or sent, nothing is touched and a human must look.
        var conflicts = marked.Where(pair => pair.Value.Count > 1 && !pair.Value.All(CanChange))
            .Select(pair => new DeliveryAction(DeliveryActionKind.Conflict, pair.Key, null, null, null)).ToList();
        if (conflicts.Count > 0)
        {
            return conflicts.OrderBy(action => action.Marker, StringComparer.Ordinal).ToList();
        }

        var actions = new List<DeliveryAction>();
        foreach (var (marker, copies) in marked.Where(pair => pair.Value.Count > 1))
        {
            // The oldest copy stays (lowest id); the newer ones go.
            foreach (var duplicate in copies.Skip(1))
            {
                actions.Add(new DeliveryAction(DeliveryActionKind.DeleteDuplicate, marker, duplicate.SendAt, duplicate.Id, null));
            }
        }

        foreach (var issue in issues)
        {
            if (!marked.TryGetValue(issue.Marker, out var found))
            {
                if (issue.SendAt >= cutOff)
                {
                    actions.Add(new DeliveryAction(DeliveryActionKind.Create, issue.Marker, issue.SendAt, null, issue));
                }
                else if (issue.SendAt > now - ReportWindow)
                {
                    actions.Add(new DeliveryAction(DeliveryActionKind.Late, issue.Marker, issue.SendAt, null, issue));
                }

                continue;
            }

            var broadcast = found[0];
            if (!CanChange(broadcast))
            {
                if (issue.SendAt > now - ReportWindow)
                {
                    actions.Add(new DeliveryAction(DeliveryActionKind.Locked, issue.Marker, broadcast.SendAt ?? issue.SendAt, broadcast.Id, issue));
                }

                continue;
            }

            if (issue.SendAt < cutOff)
            {
                // Kit holds it unsent but main's time is already past the cut-off (e.g. the send
                // time was moved earlier): leave it alone rather than send at an unplanned moment.
                actions.Add(new DeliveryAction(DeliveryActionKind.Locked, issue.Marker, broadcast.SendAt, broadcast.Id, issue));
                continue;
            }

            var same = broadcast.Subject == issue.Email.Subject
                && broadcast.PreviewText == issue.Email.PreviewText
                && broadcast.Content == issue.Email.Html
                && broadcast.SendAt == issue.SendAt
                && broadcast.TagId == issue.TagId
                && broadcast.Status == "scheduled";
            actions.Add(new DeliveryAction(same ? DeliveryActionKind.Unchanged : DeliveryActionKind.Update, issue.Marker, issue.SendAt, broadcast.Id, issue, broadcast.EmailTemplateId));
        }

        var wanted = issues.Select(issue => issue.Marker).ToHashSet(StringComparer.Ordinal);
        foreach (var (marker, found) in marked.Where(pair => !wanted.Contains(pair.Key)))
        {
            if (CanChange(found[0]))
            {
                actions.Add(new DeliveryAction(DeliveryActionKind.Cancel, marker, found[0].SendAt, found[0].Id, null));
            }
        }

        return actions.OrderBy(action => action.Marker, StringComparer.Ordinal).ToList();
    }
}
