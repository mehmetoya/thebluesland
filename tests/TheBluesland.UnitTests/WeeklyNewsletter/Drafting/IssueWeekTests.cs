using Shouldly;
using TheBluesland.WeeklyNewsletter.Drafting;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

/// <summary>issue-drafting spec, Design section 4: issue name, send date and window.</summary>
public sealed class IssueWeekTests
{
    [Fact]
    public void ForDraftAt_on_a_Tuesday_uses_the_window_that_ended_last_Sunday_10_00_Istanbul()
    {
        var week = IssueWeek.ForDraftAt(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero));

        week.To.ShouldBe(new DateTimeOffset(2026, 11, 8, 7, 0, 0, TimeSpan.Zero)); // 10:00 UTC+3
        week.From.ShouldBe(new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero));
        week.SendDate.ShouldBe(new DateOnly(2026, 11, 9));
        week.Id.ShouldBe("2026-W46");
    }

    [Fact]
    public void ForDraftAt_exactly_at_Sunday_10_00_Istanbul_uses_the_window_ending_now()
    {
        var sunday = new DateTimeOffset(2026, 11, 15, 7, 0, 0, TimeSpan.Zero);

        var week = IssueWeek.ForDraftAt(sunday);

        week.To.ShouldBe(sunday);
        week.SendDate.ShouldBe(new DateOnly(2026, 11, 16));
        week.Id.ShouldBe("2026-W47");
    }

    [Fact]
    public void Resolve_names_the_issue_the_sunday_schedule_and_its_safety_retry_draft()
    {
        // newsletter-draft.yml: cron 07:00 UTC and 10:00 UTC on Sunday 2026-11-15.
        IssueWeek.Resolve(null, null, new DateTimeOffset(2026, 11, 15, 7, 0, 30, TimeSpan.Zero)).Id.ShouldBe("2026-W47");
        IssueWeek.Resolve(null, null, new DateTimeOffset(2026, 11, 15, 10, 4, 0, TimeSpan.Zero)).Id.ShouldBe("2026-W47");
    }

    [Fact]
    public void Resolve_with_an_explicit_window_uses_it_and_defaults_to_seven_days()
    {
        var to = new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);

        var week = IssueWeek.Resolve(null, to, DateTimeOffset.UnixEpoch);

        week.Id.ShouldBe("2026-W47");
        week.From.ShouldBe(to.AddDays(-7));
        IssueWeek.Resolve(to.AddDays(-2), to, DateTimeOffset.UnixEpoch).From.ShouldBe(to.AddDays(-2));
    }

    [Fact]
    public void ForDraftAt_on_Sunday_morning_before_10_00_still_uses_the_previous_window()
    {
        IssueWeek.ForDraftAt(new DateTimeOffset(2026, 11, 15, 6, 59, 0, TimeSpan.Zero)).Id.ShouldBe("2026-W46");
    }

    [Theory]
    [InlineData("2026-12-27T07:00:00Z", "2026-12-28", "2026-W53")]
    [InlineData("2027-01-03T07:00:00Z", "2027-01-04", "2027-W01")]
    [InlineData("2026-11-10T10:00:00Z", "2026-11-16", "2026-W47")]
    [InlineData("2026-11-15T21:00:00Z", "2026-11-16", "2026-W47")] // Monday 00:00 Istanbul, exclusive

    public void ForWindow_sends_on_the_first_Monday_after_the_window(string to, string sendDate, string id)
    {
        var end = DateTimeOffset.Parse(to, System.Globalization.CultureInfo.InvariantCulture);

        var week = IssueWeek.ForWindow(end.AddDays(-7), end);

        week.SendDate.ShouldBe(DateOnly.Parse(sendDate, System.Globalization.CultureInfo.InvariantCulture));
        week.Id.ShouldBe(id);
    }
}
