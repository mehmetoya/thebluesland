using System.Net;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using TheBluesland.WeeklyNewsletter.Ingest;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter;

/// <summary>
/// news-ingest spec, Design section 4 + Success Criteria, against a real Postgres (Testcontainers):
/// idempotent re-runs, utm variants collapsing to one row, an existing row never being rewritten,
/// and one broken feed (timeout / bad XML / HTTP 500) never stopping the others. Feeds are served
/// by an in-memory handler keyed on host - no network.
/// </summary>
public sealed class NewsIngestServiceTests : IAsyncLifetime
{
    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "newsletter-feeds");

    private static readonly string[] RealFeedIds =
        ["billboard", "pitchfork", "rolling-stone", "nme", "stereogum", "consequence", "music-business-worldwide"];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = NewsletterDbContext.Create(_postgres.GetConnectionString());
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task RunAsync_stores_every_item_from_every_real_fixture()
    {
        var service = CreateService(RealFeedIds.ToDictionary(id => Host(id), id => FixtureResponse(id)));

        var report = await service.RunAsync(RealFeedIds.Select(Feed).ToList(), CancellationToken.None);

        report.Feeds.ShouldAllBe(feed => feed.Status == FeedRunStatus.Succeeded && feed.Fetched == 4 && feed.Added == 4);
        (await CountArticlesAsync()).ShouldBe(28);
    }

    [Fact]
    public async Task RunAsync_second_run_over_the_same_feeds_adds_nothing()
    {
        var service = CreateService(RealFeedIds.ToDictionary(id => Host(id), id => FixtureResponse(id)));
        var feeds = RealFeedIds.Select(Feed).ToList();
        await service.RunAsync(feeds, CancellationToken.None);

        var second = await service.RunAsync(feeds, CancellationToken.None);

        second.Feeds.ShouldAllBe(feed => feed.Status == FeedRunStatus.Succeeded && feed.Fetched == 4 && feed.Added == 0);
        (await CountArticlesAsync()).ShouldBe(28);
    }

    [Fact]
    public async Task RunAsync_never_rewrites_an_existing_row()
    {
        var service = CreateService(new() { [Host("a")] = () => Rss(("Original title", "https://news.example.com/story")) });
        await service.RunAsync([Feed("a")], CancellationToken.None);
        var firstFetchedAt = _clock.GetUtcNow();

        _clock.Advance(TimeSpan.FromHours(2));
        var later = CreateService(new() { [Host("a")] = () => Rss(("Edited title", "https://news.example.com/story")) });
        await later.RunAsync([Feed("a")], CancellationToken.None);

        await using var context = NewsletterDbContext.Create(_postgres.GetConnectionString());
        var row = await context.NewsArticles.AsNoTracking().SingleAsync();
        row.Title.ShouldBe("Original title");
        row.FetchedAt.ShouldBe(firstFetchedAt);
    }

    [Fact]
    public async Task RunAsync_collapses_tracking_variants_of_one_story_across_feeds_into_one_row()
    {
        var service = CreateService(new()
        {
            [Host("a")] = () => Rss(("Story", "https://www.news.example.com/story/?utm_source=rss&utm_medium=feed")),
            [Host("b")] = () => Rss(("Story", "http://news.example.com/story")),
        });

        await service.RunAsync([Feed("a"), Feed("b")], CancellationToken.None);

        await using var context = NewsletterDbContext.Create(_postgres.GetConnectionString());
        var rows = await context.NewsArticles.AsNoTracking().ToListAsync();
        rows.Count.ShouldBe(1);
        rows[0].NormalizedUrl.ShouldBe("https://news.example.com/story");
        rows[0].Url.ShouldBe("https://www.news.example.com/story/?utm_source=rss&utm_medium=feed");
    }

    [Fact]
    public async Task RunAsync_one_failing_feed_of_each_kind_does_not_stop_the_others()
    {
        var service = CreateService(
            new()
            {
                [Host("nme")] = FixtureResponse("nme"),
                [Host("broken-xml")] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>challenge</html>") },
                [Host("server-error")] = () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
                [Host("pitchfork")] = FixtureResponse("pitchfork"),
            },
            hangingHosts: [Host("slow")]);

        var report = await service.RunAsync(
            [Feed("nme"), Feed("broken-xml"), Feed("server-error"), Feed("slow"), Feed("pitchfork")],
            CancellationToken.None);

        report.Feeds.Select(feed => (feed.FeedId, feed.Status)).ShouldBe(
        [
            ("nme", FeedRunStatus.Succeeded),
            ("broken-xml", FeedRunStatus.Failed),
            ("server-error", FeedRunStatus.Failed),
            ("slow", FeedRunStatus.Failed),
            ("pitchfork", FeedRunStatus.Succeeded),
        ]);
        report.Feeds.Where(feed => feed.Status == FeedRunStatus.Failed).ShouldAllBe(feed => !string.IsNullOrWhiteSpace(feed.Error));
        report.AnySucceeded.ShouldBeTrue();
        (await CountArticlesAsync()).ShouldBe(8);
    }

    [Fact]
    public async Task RunAsync_all_feeds_failing_is_reported_without_throwing()
    {
        var service = CreateService(new() { [Host("x")] = () => new HttpResponseMessage(HttpStatusCode.BadGateway) });

        var report = await service.RunAsync([Feed("x")], CancellationToken.None);

        report.AnySucceeded.ShouldBeFalse();
        report.Feeds.Single().Error!.ShouldContain("502");
    }

    [Fact]
    public async Task RunAsync_stores_fetched_at_from_the_clock_and_published_at_in_utc()
    {
        var service = CreateService(new() { [Host("pitchfork")] = FixtureResponse("pitchfork") });

        await service.RunAsync([Feed("pitchfork")], CancellationToken.None);

        await using var context = NewsletterDbContext.Create(_postgres.GetConnectionString());
        var rows = await context.NewsArticles.AsNoTracking().ToListAsync();
        rows.ShouldAllBe(row => row.FetchedAt == _clock.GetUtcNow() && row.Source == "pitchfork");
        rows.ShouldAllBe(row => row.PublishedAt != null && row.PublishedAt.Value.Offset == TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_round_trips_categories_and_null_summaries()
    {
        var service = CreateService(new()
        {
            [Host("billboard")] = FixtureResponse("billboard"),
            [Host("stereogum")] = FixtureResponse("stereogum"),
        });

        await service.RunAsync([Feed("billboard"), Feed("stereogum")], CancellationToken.None);

        await using var context = NewsletterDbContext.Create(_postgres.GetConnectionString());
        var billboard = await context.NewsArticles.AsNoTracking()
            .Where(row => row.Source == "billboard")
            .OrderBy(row => row.Id)
            .FirstAsync();
        billboard.Categories.ShouldBe(["Music", "Music News", "Dance", "genre dance", "streaming"]);
        billboard.Summary.ShouldNotBeNullOrWhiteSpace();
        (await context.NewsArticles.Where(row => row.Source == "stereogum").AllAsync(row => row.Summary == null)).ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_caller_cancellation_is_not_swallowed_as_a_feed_failure()
    {
        var service = CreateService([], hangingHosts: [Host("slow")], feedTimeout: TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(() => service.RunAsync([Feed("slow")], cts.Token));
    }

    private NewsIngestService CreateService(
        Dictionary<string, Func<HttpResponseMessage>> responses,
        string[]? hangingHosts = null,
        TimeSpan? feedTimeout = null)
    {
        var httpClient = new HttpClient(new RoutingHandler(responses, hangingHosts ?? []));
        var feedClient = new RssFeedClient(httpClient, feedTimeout ?? TimeSpan.FromMilliseconds(300));
        return new NewsIngestService(feedClient, () => NewsletterDbContext.Create(_postgres.GetConnectionString()), _clock);
    }

    private async Task<int> CountArticlesAsync()
    {
        await using var context = NewsletterDbContext.Create(_postgres.GetConnectionString());
        return await context.NewsArticles.CountAsync();
    }

    private static string Host(string feedId) => $"{feedId}.feeds.test";

    private static FeedConfig Feed(string feedId) => new(feedId, feedId, new Uri($"https://{Host(feedId)}/rss"), Enabled: true);

    private static Func<HttpResponseMessage> FixtureResponse(string feedId) => () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StreamContent(File.OpenRead(Path.Combine(FixturesDirectory, $"{feedId}.xml"))),
    };

    private static HttpResponseMessage Rss(params (string Title, string Link)[] items)
    {
        var body = string.Concat(items.Select(item =>
            $"<item><title>{item.Title}</title><link>{WebUtility.HtmlEncode(item.Link)}</link><pubDate>Mon, 09 Nov 2026 10:00:00 +0000</pubDate></item>"));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"<rss version=\"2.0\"><channel><title>t</title><link>https://x.test</link><description>d</description>{body}</channel></rss>"),
        };
    }

    private sealed class RoutingHandler(Dictionary<string, Func<HttpResponseMessage>> responses, string[] hangingHosts) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri!.Host;
            if (hangingHosts.Contains(host))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return responses.TryGetValue(host, out var respond) ? respond() : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
