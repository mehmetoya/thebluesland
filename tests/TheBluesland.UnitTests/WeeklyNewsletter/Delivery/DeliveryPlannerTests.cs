using Shouldly;
using TheBluesland.WeeklyNewsletter.Delivery;
using TheBluesland.WeeklyNewsletter.Drafting;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Delivery.DeliveryTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Delivery;

/// <summary>issue-delivery spec, Design section 3: main is the source of truth for Kit.</summary>
public sealed class DeliveryPlannerTests
{
    private static readonly TimeSpan Lead = TimeSpan.FromMinutes(2);

    /// <summary>Sunday 2026-11-15 12:00 Istanbul: Mehmet just merged W47.</summary>
    private static readonly DateTimeOffset SundayNoon = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Send_times_are_monday_08_00_and_15_00_istanbul()
    {
        Issue(DraftLanguage.Turkish).SendAt.ShouldBe(new DateTimeOffset(2026, 11, 16, 5, 0, 0, TimeSpan.Zero));
        Issue(DraftLanguage.English).SendAt.ShouldBe(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_fresh_merge_schedules_both_languages()
    {
        var actions = DeliveryPlanner.Plan([Issue(DraftLanguage.Turkish), Issue(DraftLanguage.English)], [], SundayNoon, Lead);

        actions.Select(action => (action.Kind, action.Marker)).ShouldBe(
            [(DeliveryActionKind.Create, "thebluesland-weekly:2026-W47:en"), (DeliveryActionKind.Create, "thebluesland-weekly:2026-W47:tr")]);
    }

    [Fact]
    public void Running_again_changes_nothing()
    {
        var tr = Issue(DraftLanguage.Turkish);

        DeliveryPlanner.Plan([tr], [Broadcast(tr)], SundayNoon, Lead).ShouldHaveSingleItem().Kind.ShouldBe(DeliveryActionKind.Unchanged);
    }

    [Fact]
    public void A_fix_in_main_before_sending_updates_the_broadcast()
    {
        var tr = Issue(DraftLanguage.Turkish, html: "<p>fixed</p>");

        var action = DeliveryPlanner.Plan([tr], [Broadcast(tr, id: 42, html: "<p>typo</p>")], SundayNoon, Lead).ShouldHaveSingleItem();

        action.Kind.ShouldBe(DeliveryActionKind.Update);
        action.BroadcastId.ShouldBe(42);
        action.EmailTemplateId.ShouldBe(5);
    }

    [Theory]
    [InlineData("sending")]
    [InlineData("completed")]
    [InlineData("aborted")]
    public void A_broadcast_that_is_sending_sent_or_stopped_in_kit_is_never_touched(string status)
    {
        var tr = Issue(DraftLanguage.Turkish, html: "<p>edited after sending</p>");

        DeliveryPlanner.Plan([tr], [Broadcast(tr, status: status, html: "<p>sent</p>")], SundayNoon, Lead).ShouldHaveSingleItem().Kind.ShouldBe(DeliveryActionKind.Locked);
    }

    [Fact]
    public void A_scheduled_broadcast_inside_the_cut_off_is_not_touched()
    {
        var tr = Issue(DraftLanguage.Turkish, html: "<p>late fix</p>");
        var oneMinuteBefore = tr.SendAt - TimeSpan.FromMinutes(1);

        DeliveryPlanner.Plan([tr], [Broadcast(tr, html: "<p>old</p>")], oneMinuteBefore, Lead).ShouldHaveSingleItem().Kind.ShouldBe(DeliveryActionKind.Locked);
    }

    [Fact]
    public void A_merge_exactly_at_the_cut_off_still_goes_out_and_one_second_later_does_not()
    {
        var tr = Issue(DraftLanguage.Turkish);

        DeliveryPlanner.Plan([tr], [], tr.SendAt - Lead, Lead).ShouldHaveSingleItem().Kind.ShouldBe(DeliveryActionKind.Create);
        DeliveryPlanner.Plan([tr], [], tr.SendAt - Lead + TimeSpan.FromSeconds(1), Lead).ShouldHaveSingleItem().Kind.ShouldBe(DeliveryActionKind.Late);
    }

    [Fact]
    public void A_late_turkish_merge_still_schedules_english()
    {
        // Monday 09:00 Istanbul: TR (08:00) is gone for this week, EN (15:00) is not.
        var mondayMorning = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);

        var actions = DeliveryPlanner.Plan([Issue(DraftLanguage.Turkish), Issue(DraftLanguage.English)], [], mondayMorning, Lead);

        actions.Select(action => action.Kind).ShouldBe([DeliveryActionKind.Create, DeliveryActionKind.Late]);
    }

    [Fact]
    public void A_reverted_issue_cancels_its_scheduled_broadcast_but_never_a_sent_one()
    {
        var tr = Issue(DraftLanguage.Turkish);
        var oldEn = Issue(DraftLanguage.English, id: "2026-W46", sendDate: new DateOnly(2026, 11, 9));

        var actions = DeliveryPlanner.Plan([], [Broadcast(tr, id: 7), Broadcast(oldEn, id: 8, status: "completed")], SundayNoon, Lead);

        var cancel = actions.ShouldHaveSingleItem();
        cancel.Kind.ShouldBe(DeliveryActionKind.Cancel);
        cancel.BroadcastId.ShouldBe(7);
    }

    [Fact]
    public void Two_unsent_broadcasts_with_one_marker_lose_the_newer_one_and_the_older_carries_on()
    {
        var tr = Issue(DraftLanguage.Turkish);

        var actions = DeliveryPlanner.Plan([tr], [Broadcast(tr, id: 2), Broadcast(tr, id: 1)], SundayNoon, Lead);

        actions.Select(action => (action.Kind, action.BroadcastId)).ShouldBe([(DeliveryActionKind.DeleteDuplicate, (long?)2), (DeliveryActionKind.Unchanged, (long?)1)]);
    }

    [Fact]
    public void Duplicates_of_a_reverted_issue_are_removed_and_the_survivor_is_cancelled()
    {
        var tr = Issue(DraftLanguage.Turkish);

        var actions = DeliveryPlanner.Plan([], [Broadcast(tr, id: 1), Broadcast(tr, id: 2)], SundayNoon, Lead);

        actions.Select(action => (action.Kind, action.BroadcastId)).ShouldBe([(DeliveryActionKind.DeleteDuplicate, (long?)2), (DeliveryActionKind.Cancel, (long?)1)]);
    }

    [Theory]
    [InlineData("sending")]
    [InlineData("completed")]
    [InlineData("aborted")]
    public void Duplicates_of_which_one_is_already_sent_or_stopped_change_nothing_and_stay_a_conflict(string status)
    {
        var tr = Issue(DraftLanguage.Turkish);

        var actions = DeliveryPlanner.Plan([tr, Issue(DraftLanguage.English)], [Broadcast(tr, id: 1, status: status), Broadcast(tr, id: 2)], SundayNoon, Lead);

        actions.ShouldHaveSingleItem().Kind.ShouldBe(DeliveryActionKind.Conflict);
    }

    [Fact]
    public void Duplicates_inside_the_cut_off_stay_a_conflict()
    {
        var tr = Issue(DraftLanguage.Turkish);

        DeliveryPlanner.Plan([tr], [Broadcast(tr, id: 1), Broadcast(tr, id: 2)], tr.SendAt - TimeSpan.FromMinutes(1), Lead)
            .ShouldHaveSingleItem().Kind.ShouldBe(DeliveryActionKind.Conflict);
    }

    [Fact]
    public void A_smoke_test_broadcast_is_not_one_of_ours_and_is_never_cancelled()
    {
        // kit-smoke creates "thebluesland-weekly-smoke:<timestamp>"; it shares a prefix with the real
        // marker but not the colon, so the planner must neither plan for nor cancel it.
        var smoke = new KitBroadcast(4, "thebluesland-weekly-smoke:2026-11-15T09:00:00Z", "Smoke", "Smoke", "<p>x</p>", null, "draft", null, 111);

        DeliveryPlanner.Plan([], [smoke], SundayNoon, Lead).ShouldBeEmpty();
        DeliveryPlanner.Plan([Issue(DraftLanguage.Turkish)], [smoke], SundayNoon, Lead).ShouldHaveSingleItem().Kind.ShouldBe(DeliveryActionKind.Create);
    }

    [Fact]
    public void Broadcasts_without_our_marker_are_ignored_and_old_issues_stay_out_of_the_report()
    {
        var old = Issue(DraftLanguage.Turkish, id: "2026-W40", sendDate: new DateOnly(2026, 9, 28));
        var foreign = new KitBroadcast(3, "Mehmet's own announcement", "Hi", "Hi", "<p>hi</p>", SundayNoon.AddDays(1), "scheduled", null, null);

        DeliveryPlanner.Plan([old], [foreign], SundayNoon, Lead).ShouldBeEmpty();
    }

    [Fact]
    public void Planning_is_deterministic()
    {
        PlannedIssue[] issues = [Issue(DraftLanguage.English), Issue(DraftLanguage.Turkish)];

        DeliveryPlanner.Plan(issues, [], SundayNoon, Lead).ShouldBe(DeliveryPlanner.Plan(issues.Reverse().ToList(), [], SundayNoon, Lead));
    }
}
