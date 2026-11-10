using System.Globalization;

namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>
/// Which issue a draft is for (issue-drafting spec, Design section 4). An issue is named after the
/// ISO week of its Monday send date. Its window ends on the Sunday 10:00 (Europe/Istanbul) before
/// that Monday - when the weekly draft runs - and starts seven days earlier.
/// </summary>
public sealed record IssueWeek(int Year, int Week, DateOnly SendDate, DateTimeOffset From, DateTimeOffset To)
{
    public static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    private static readonly TimeSpan DraftTimeOfDay = TimeSpan.FromHours(10);

    public string Id => string.Create(CultureInfo.InvariantCulture, $"{Year}-W{Week:00}");

    /// <summary>The issue whose window ended at the most recent Sunday 10:00 Istanbul time at or before <paramref name="now"/>.</summary>
    public static IssueWeek ForDraftAt(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Istanbul);
        var sunday = DateOnly.FromDateTime(local.DateTime).AddDays(-(int)local.DayOfWeek);
        var end = AtIstanbul(sunday, DraftTimeOfDay);
        if (end > now)
        {
            end = AtIstanbul(sunday.AddDays(-7), DraftTimeOfDay);
        }

        return ForWindow(end - TimeSpan.FromDays(7), end);
    }

    /// <summary>An explicit window; the issue is sent on the first Monday after the window's last Istanbul day.</summary>
    public static IssueWeek ForWindow(DateTimeOffset from, DateTimeOffset to)
    {
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(to, Istanbul).DateTime);
        var daysToMonday = ((int)DayOfWeek.Monday - (int)lastDay.DayOfWeek + 7) % 7;
        var sendDate = lastDay.AddDays(daysToMonday == 0 ? 7 : daysToMonday);
        var sendDateTime = sendDate.ToDateTime(TimeOnly.MinValue);
        return new IssueWeek(ISOWeek.GetYear(sendDateTime), ISOWeek.GetWeekOfYear(sendDateTime), sendDate, from, to);
    }

    private static DateTimeOffset AtIstanbul(DateOnly date, TimeSpan timeOfDay)
    {
        var local = date.ToDateTime(TimeOnly.FromTimeSpan(timeOfDay));
        return new DateTimeOffset(local, Istanbul.GetUtcOffset(local)).ToUniversalTime();
    }
}
