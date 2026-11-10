using TheBluesland.WeeklyNewsletter.Delivery;
using TheBluesland.WeeklyNewsletter.Drafting;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Delivery;

internal static class DeliveryTestData
{
    public const string ValidYaml = """
        timeZone: Europe/Istanbul
        minimumLeadMinutes: 2
        kitPublic: false
        languages:
          tr:
            sendTime: "08:00"
            kitTagId: 111
          en:
            sendTime: "15:00"
            kitTagId: 222
        """;

    public static readonly DeliveryOptions Options = DeliveryOptionsLoader.Parse(ValidYaml).Options!;

    /// <summary>Monday 2026-11-16, the W47 send date.</summary>
    public static readonly DateOnly Monday = new(2026, 11, 16);

    public static PlannedIssue Issue(DraftLanguage language, string id = "2026-W47", DateOnly? sendDate = null, string html = "<p>x</p>") =>
        new(id, language, Options.SendAt(sendDate ?? Monday, language), new IssueEmail("Subject", "Preview.", html), Options.Languages[language.Code].KitTagId);

    public static KitBroadcast Broadcast(PlannedIssue issue, long id = 9, string status = "scheduled", string? html = null, DateTimeOffset? sendAt = null) =>
        new(id, issue.Marker, issue.Email.Subject, issue.Email.PreviewText, html ?? issue.Email.Html, sendAt ?? issue.SendAt, status, 5, issue.TagId);
}
