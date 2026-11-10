using System.Data.Common;
using Shouldly;
using TheBluesland.WeeklyNewsletter.Reporting;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter;

/// <summary>EF Core wraps a failed connection in InvalidOperationException; both shapes must be caught.</summary>
public sealed class DatabaseErrorsTests
{
    private sealed class FakeDbException(string message) : DbException(message);

    [Fact]
    public void Find_returns_a_plain_DbException()
    {
        var error = new FakeDbException("boom");

        DatabaseErrors.Find(error).ShouldBeSameAs(error);
        DatabaseErrors.Describe(error).ShouldBe("FakeDbException: boom");
    }

    [Fact]
    public void Find_unwraps_EFs_transient_failure_wrapper()
    {
        var inner = new FakeDbException("Failed to connect");

        DatabaseErrors.Find(new InvalidOperationException("An exception has been raised that is likely due to a transient failure.", inner)).ShouldBeSameAs(inner);
    }

    [Fact]
    public void Find_ignores_unrelated_exceptions()
    {
        DatabaseErrors.Find(new InvalidOperationException("other")).ShouldBeNull();
        DatabaseErrors.Find(new TimeoutException()).ShouldBeNull();
    }
}
