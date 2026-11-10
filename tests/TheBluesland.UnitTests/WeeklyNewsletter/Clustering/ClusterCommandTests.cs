using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using TheBluesland.Data.Entities;
using TheBluesland.WeeklyNewsletter.Clustering;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Clustering;

/// <summary>story-clustering spec, Design sections 1 and 4: the "cluster" command against real Postgres.</summary>
public sealed class ClusterCommandTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = NewsletterDbContext.Create(_postgres.GetConnectionString());
        await context.Database.MigrateAsync();
        context.NewsArticles.AddRange(
            Row("stereogum", "Oasis Won’t Attend Their Rock & Roll Hall Of Fame Induction", published: Now.AddHours(-30)),
            Row("rolling-stone", "Oasis Won’t Be Attending Rock and Roll of Fame Induction Ceremony", published: Now.AddHours(-28)),
            Row("consequence", "Oasis to Skip Rock and Roll Hall of Fame Induction Ceremony", published: null, fetched: Now.AddHours(-27)),
            Row("pitchfork", "Exactly Seven Days Old Is Inside The Window", published: Now.AddDays(-7)),
            Row("nme", "Older Than The Window", published: Now.AddDays(-7).AddSeconds(-1)),
            Row("billboard", "Published Exactly At The End Is Outside", published: Now));
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task RunAsync_default_window_is_the_seven_days_before_now_on_published_else_fetched()
    {
        var exitCode = await RunAsync(_postgres.GetConnectionString());

        exitCode.ShouldBe(0, _error.ToString());
        var report = _output.ToString();
        report.ShouldContain("## Story clusters · 2026-11-03 10:00 → 2026-11-10 10:00 UTC");
        report.ShouldContain("4 articles → 2 stories · 1 covered by 2+ sources");
        report.ShouldContain("| Stereogum · Rolling Stone · Consequence | 3 |");
        report.ShouldContain("Exactly Seven Days Old Is Inside The Window");
        report.ShouldNotContain("Older Than The Window");
        report.ShouldNotContain("Published Exactly At The End");
    }

    [Fact]
    public async Task RunAsync_explicit_window_is_honoured()
    {
        var exitCode = await RunAsync(_postgres.GetConnectionString(), from: Now.AddHours(-29), to: Now.AddHours(-27).AddSeconds(1));

        exitCode.ShouldBe(0, _error.ToString());
        _output.ToString().ShouldContain("2 articles → 1 stories · 1 covered by 2+ sources");
    }

    [Fact]
    public async Task RunAsync_without_connection_string_fails_before_reading()
    {
        (await RunAsync(null)).ShouldBe(1);
        _error.ToString().ShouldContain("NEON_NEWSLETTER_CONNECTION_STRING is not set");
        _output.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_reversed_window_fails()
    {
        (await RunAsync(_postgres.GetConnectionString(), from: Now, to: Now.AddDays(-1))).ShouldBe(1);
        _error.ToString().ShouldContain("must be before");
    }

    [Fact]
    public async Task RunAsync_database_error_prints_type_and_message_only()
    {
        var unreachable = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2";

        (await RunAsync(unreachable)).ShouldBe(1);
        _error.ToString().ShouldStartWith("Database error: ");
        _error.ToString().ShouldNotContain("   at ");
    }

    private Task<int> RunAsync(string? connectionString, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        ClusterCommand.RunAsync(
            RepoFiles.Find("tools/weekly-newsletter/clustering.yml"),
            RepoFiles.Find("tools/weekly-newsletter/feeds.yml"),
            connectionString,
            from,
            to,
            new FixedTimeProvider(Now),
            _output,
            _error,
            CancellationToken.None);

    private static int _sequence;

    private static NewsArticle Row(string source, string title, DateTimeOffset? published, DateTimeOffset? fetched = null)
    {
        var id = Interlocked.Increment(ref _sequence);
        return new NewsArticle
        {
            Source = source,
            Title = title,
            Url = $"https://example.com/{id}",
            NormalizedUrl = $"https://example.com/{id}",
            PublishedAt = published,
            FetchedAt = fetched ?? Now.AddDays(-10),
        };
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
