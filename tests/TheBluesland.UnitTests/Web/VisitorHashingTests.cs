using Shouldly;
using TheBluesland.Web.Analytics;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// docs/specs/visitor-and-playlist-click-analytics.md Testing Strategy / Design section 3: the
/// actual privacy property being relied on - the same visitor hashes differently on two different
/// days, so "approximate daily unique visitors" is derivable without ever storing anything that
/// could correlate one visitor across days - is asserted directly here, not just "it's some string".
/// </summary>
public sealed class VisitorHashingTests
{
    private static readonly DateOnly SampleDate = new(2026, 9, 11);

    [Fact]
    public void Same_inputs_always_produce_the_same_hash()
    {
        var first = VisitorHashing.Compute("pepper", SampleDate, "203.0.113.5", "Mozilla/5.0");
        var second = VisitorHashing.Compute("pepper", SampleDate, "203.0.113.5", "Mozilla/5.0");

        first.ShouldBe(second);
    }

    [Fact]
    public void Changing_the_pepper_changes_the_hash()
    {
        var original = VisitorHashing.Compute("pepper-one", SampleDate, "203.0.113.5", "Mozilla/5.0");
        var changed = VisitorHashing.Compute("pepper-two", SampleDate, "203.0.113.5", "Mozilla/5.0");

        changed.ShouldNotBe(original);
    }

    [Fact]
    public void Changing_the_remote_ip_changes_the_hash()
    {
        var original = VisitorHashing.Compute("pepper", SampleDate, "203.0.113.5", "Mozilla/5.0");
        var changed = VisitorHashing.Compute("pepper", SampleDate, "198.51.100.9", "Mozilla/5.0");

        changed.ShouldNotBe(original);
    }

    [Fact]
    public void Changing_the_user_agent_changes_the_hash()
    {
        var original = VisitorHashing.Compute("pepper", SampleDate, "203.0.113.5", "Mozilla/5.0");
        var changed = VisitorHashing.Compute("pepper", SampleDate, "203.0.113.5", "Chrome/120.0");

        changed.ShouldNotBe(original);
    }

    /// <summary>
    /// The actual privacy property: the same visitor (same ip+userAgent) is unrecognizable as the
    /// same visitor across two different UTC calendar dates from the stored hash alone.
    /// </summary>
    [Fact]
    public void Same_ip_and_user_agent_on_two_different_dates_produce_different_hashes()
    {
        var day1 = VisitorHashing.Compute("pepper", new DateOnly(2026, 9, 11), "203.0.113.5", "Mozilla/5.0");
        var day2 = VisitorHashing.Compute("pepper", new DateOnly(2026, 9, 12), "203.0.113.5", "Mozilla/5.0");

        day1.ShouldNotBe(day2);
    }
}
