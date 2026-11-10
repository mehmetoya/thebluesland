using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using TheBluesland.Data.Entities;
using TheBluesland.UnitTests.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Ranking;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Ranking;

/// <summary>story-ranking spec, Design section 5: the "rank" command against real Postgres and the repo's settings.</summary>
public sealed class RankCommandTests : IAsyncLifetime
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
        var id = 0;
        NewsArticle Row(string source, string title, double hoursAgo) => new()
        {
            Source = source,
            Title = title,
            Url = $"https://example.com/{++id}",
            NormalizedUrl = $"https://example.com/{id}",
            PublishedAt = Now.AddHours(-hoursAgo),
            FetchedAt = Now.AddHours(-hoursAgo),
        };
        context.NewsArticles.AddRange(
            Row("pitchfork", "Richard Lloyd, Television Guitarist, Dies at 75", 70),
            Row("stereogum", "Television’s Richard Lloyd Dead At 75", 69),
            Row("consequence", "Nick Cave Launches 2027 Tour", 20),
            Row("stereogum", "The 5 Best Songs Of The Week", 30));
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task RunAsync_prints_the_pick_with_reasons()
    {
        var exitCode = await RankCommand.RunAsync(
            Path.GetDirectoryName(RepoFiles.Find("tools/weekly-newsletter/ranking.yml"))!,
            _postgres.GetConnectionString(),
            from: null,
            to: null,
            new FixedTimeProvider(Now),
            _output,
            _error,
            CancellationToken.None);

        exitCode.ShouldBe(0, _error.ToString());
        var report = _output.ToString();
        report.ShouldContain("3 stories · 2 picked + 0 backups");
        report.ShouldContain("| Pitchfork · Stereogum | 2 sources · obituary +1.5 = 3.50 |");
        report.ShouldContain("| Consequence | 1 source · live +0.25 = 1.25 |");
        report.ShouldContain("roundup: not a news story");
    }

    [Fact]
    public async Task RunAsync_without_connection_string_fails_before_reading()
    {
        var exitCode = await RankCommand.RunAsync(
            Path.GetDirectoryName(RepoFiles.Find("tools/weekly-newsletter/ranking.yml"))!,
            null,
            null,
            null,
            new FixedTimeProvider(Now),
            _output,
            _error,
            CancellationToken.None);

        exitCode.ShouldBe(1);
        _error.ToString().ShouldContain("is not set");
    }
}
