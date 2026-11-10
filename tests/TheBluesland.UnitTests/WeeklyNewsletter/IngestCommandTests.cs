using System.Net;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using TheBluesland.Data.Entities;
using TheBluesland.WeeklyNewsletter.Ingest;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter;

/// <summary>
/// news-ingest spec, Design section 4 (steps 5-6) and the exit-code rule: the full "ingest" command
/// - config, connection string, fetch/store, 8-week retention and the job-summary table - against
/// a real Postgres, with feeds served in memory.
/// </summary>
public sealed class IngestCommandTests : IAsyncLifetime
{
    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "newsletter-feeds");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly TimeProvider _clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 10, 8, 17, 0, TimeSpan.Zero));
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private string _feedsPath = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = NewsletterDbContext.Create(_postgres.GetConnectionString());
        await context.Database.MigrateAsync();

        _feedsPath = Path.Combine(Path.GetTempPath(), $"feeds-{Guid.NewGuid():N}.yml");
        await File.WriteAllTextAsync(_feedsPath, """
            feeds:
              - id: pitchfork
                name: Pitchfork
                url: https://pitchfork.feeds.test/rss
              - id: nme
                name: NME
                url: https://nme.feeds.test/rss
              - id: retired
                name: Retired Source
                url: https://retired.feeds.test/rss
                enabled: false
            """);
    }

    public async Task DisposeAsync()
    {
        File.Delete(_feedsPath);
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_success_returns_zero_and_prints_one_row_per_enabled_feed()
    {
        var exitCode = await RunAsync(Serve(("pitchfork", Fixture("pitchfork")), ("nme", Fixture("nme"))));

        exitCode.ShouldBe(0);
        var summary = _output.ToString();
        summary.ShouldContain("| Pitchfork | ✅ OK | 4 | 4 |");
        summary.ShouldContain("| NME | ✅ OK | 4 | 4 |");
        summary.ShouldNotContain("Retired Source");
        summary.ShouldContain("2/2 feeds OK");
        summary.ShouldContain("8 new");
    }

    [Fact]
    public async Task RunAsync_one_failed_feed_still_returns_zero_and_explains_the_failure()
    {
        var exitCode = await RunAsync(Serve(("pitchfork", Fixture("pitchfork")), ("nme", () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        exitCode.ShouldBe(0);
        _output.ToString().ShouldContain("| NME | ❌ Failed | – | – | HTTP 503 ServiceUnavailable |");
        _output.ToString().ShouldContain("1/2 feeds OK");
    }

    [Fact]
    public async Task RunAsync_all_feeds_failed_returns_one_but_still_prints_the_summary()
    {
        var exitCode = await RunAsync(Serve());

        exitCode.ShouldBe(1);
        _output.ToString().ShouldContain("0/2 feeds OK");
    }

    [Fact]
    public async Task RunAsync_without_connection_string_returns_one_before_fetching_anything()
    {
        var requests = 0;
        var handler = new CountingHandler(() => requests++);

        var exitCode = await IngestCommand.RunAsync(_feedsPath, connectionString: null, new HttpClient(handler), _clock, _output, _error, CancellationToken.None);

        exitCode.ShouldBe(1);
        requests.ShouldBe(0);
        _error.ToString().ShouldContain(IngestCommand.ConnectionStringVariable);
    }

    [Fact]
    public async Task RunAsync_invalid_feeds_file_returns_one_before_fetching_anything()
    {
        await File.WriteAllTextAsync(_feedsPath, "feeds: []");
        var requests = 0;

        var exitCode = await IngestCommand.RunAsync(_feedsPath, _postgres.GetConnectionString(), new HttpClient(new CountingHandler(() => requests++)), _clock, _output, _error, CancellationToken.None);

        exitCode.ShouldBe(1);
        requests.ShouldBe(0);
        _error.ToString().ShouldContain("no feeds");
    }

    [Fact]
    public async Task RunAsync_removes_articles_older_than_eight_weeks_and_keeps_newer_ones()
    {
        var now = _clock.GetUtcNow();
        await using (var context = NewsletterDbContext.Create(_postgres.GetConnectionString()))
        {
            context.NewsArticles.AddRange(
                Article("old-published", publishedAt: now.AddDays(-57), fetchedAt: now.AddDays(-1)),
                Article("old-fetched-no-date", publishedAt: null, fetchedAt: now.AddDays(-60)),
                Article("recent-published", publishedAt: now.AddDays(-55), fetchedAt: now.AddDays(-55)),
                Article("recent-no-date", publishedAt: null, fetchedAt: now.AddDays(-3)));
            await context.SaveChangesAsync();
        }

        var exitCode = await RunAsync(Serve(("pitchfork", Fixture("pitchfork")), ("nme", Fixture("nme"))));

        exitCode.ShouldBe(0);
        await using var verify = NewsletterDbContext.Create(_postgres.GetConnectionString());
        var remaining = await verify.NewsArticles.Where(a => a.Source == "seed").Select(a => a.NormalizedUrl).ToListAsync();
        remaining.ShouldBe(["https://seed.test/recent-published", "https://seed.test/recent-no-date"], ignoreOrder: true);
        _output.ToString().ShouldContain("2 removed");
    }

    private Task<int> RunAsync(HttpMessageHandler handler) =>
        IngestCommand.RunAsync(_feedsPath, _postgres.GetConnectionString(), new HttpClient(handler), _clock, _output, _error, CancellationToken.None);

    private static NewsArticle Article(string slug, DateTimeOffset? publishedAt, DateTimeOffset fetchedAt) => new()
    {
        Source = "seed",
        Title = slug,
        Url = $"https://seed.test/{slug}",
        NormalizedUrl = $"https://seed.test/{slug}",
        PublishedAt = publishedAt,
        FetchedAt = fetchedAt,
    };

    private static Func<HttpResponseMessage> Fixture(string feedId) => () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StreamContent(File.OpenRead(Path.Combine(FixturesDirectory, $"{feedId}.xml"))),
    };

    private static ServingHandler Serve(params (string FeedId, Func<HttpResponseMessage> Respond)[] feeds) =>
        new(feeds.ToDictionary(feed => $"{feed.FeedId}.feeds.test", feed => feed.Respond));

    private sealed class ServingHandler(Dictionary<string, Func<HttpResponseMessage>> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses.TryGetValue(request.RequestUri!.Host, out var respond)
                ? respond()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class CountingHandler(Action onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onRequest();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
