using System.Net;
using System.Xml;
using Shouldly;
using TheBluesland.UnitTests.SpotifyFetcher;
using TheBluesland.WeeklyNewsletter.Ingest;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter;

/// <summary>
/// news-ingest spec, Design section 4 and Testing Strategy: real feed responses captured on
/// 2026-11-10 (Fixtures/newsletter-feeds/&lt;id&gt;.xml) plus synthetic Atom/edge-case files. No test
/// makes a network call - FetchAsync goes through FakeHttpMessageHandler.
/// </summary>
public sealed class RssFeedClientTests
{
    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "newsletter-feeds");

    [Theory]
    [InlineData("billboard")]
    [InlineData("pitchfork")]
    [InlineData("rolling-stone")]
    [InlineData("nme")]
    [InlineData("stereogum")]
    [InlineData("consequence")]
    [InlineData("music-business-worldwide")]
    public void Parse_real_fixture_returns_every_item_with_title_absolute_url_and_utc_date(string feedId)
    {
        var items = ParseFixture(feedId);

        items.Count.ShouldBe(4);
        items.ShouldAllBe(item => item.Source == feedId);
        items.ShouldAllBe(item => !string.IsNullOrWhiteSpace(item.Title));
        items.ShouldAllBe(item => item.Url.IsAbsoluteUri && item.Url.Scheme == Uri.UriSchemeHttps);
        items.ShouldAllBe(item => item.PublishedAt != null && item.PublishedAt.Value.Offset == TimeSpan.Zero);
    }

    [Theory]
    [InlineData("billboard")]
    [InlineData("pitchfork")]
    [InlineData("rolling-stone")]
    [InlineData("nme")]
    [InlineData("stereogum")]
    [InlineData("consequence")]
    [InlineData("music-business-worldwide")]
    public void Parse_real_fixture_summaries_are_plain_text_without_wordpress_boilerplate(string feedId)
    {
        var summaries = ParseFixture(feedId).Select(item => item.Summary).OfType<string>().ToList();

        summaries.ShouldAllBe(summary => !summary.Contains('<') && !summary.Contains("&#") && !summary.Contains("&amp;"));
        summaries.ShouldAllBe(summary => !summary.Contains("appeared first on", StringComparison.OrdinalIgnoreCase));
        summaries.ShouldAllBe(summary => summary == summary.Trim() && !summary.Contains("  "));
    }

    [Fact]
    public void Parse_decodes_html_entities_in_titles()
    {
        var titles = ParseFixture("stereogum").Select(item => item.Title).ToList();

        titles.ShouldContain("Oasis Won’t Attend Their Rock & Roll Hall Of Fame Induction");
    }

    [Fact]
    public void Parse_summary_made_only_of_wordpress_boilerplate_becomes_null()
    {
        var items = ParseFixture("stereogum");

        items.ShouldAllBe(item => item.Summary == null);
    }

    [Fact]
    public void Parse_keeps_real_summary_text_before_the_wordpress_suffix()
    {
        var summary = ParseFixture("nme")[0].Summary;

        summary.ShouldNotBeNull();
        summary.ShouldEndWith("how to watch the spectacular show");
    }

    [Fact]
    public void Parse_drops_trailing_source_link_text()
    {
        var summary = ParseFixture("music-business-worldwide")[0].Summary;

        summary.ShouldBe("Billboard announced the change on Monday (November 9), and YouTube confirmed its return later the same day.");
    }

    [Fact]
    public void Parse_keeps_feed_categories_trimmed_and_distinct()
    {
        var categories = ParseFixture("billboard")[0].Categories;

        categories.ShouldBe(["Music", "Music News", "Dance", "genre dance", "streaming"]);
    }

    [Fact]
    public void Parse_atom_feed_reads_published_date_link_summary_and_category()
    {
        var items = ParseFile("synthetic-atom.xml", "atom-source");

        items.Count.ShouldBe(2);
        items[0].Title.ShouldBe("Atom & Friends Announce Tour");
        items[0].Url.ShouldBe(new Uri("https://atom.example.com/news/atom-and-friends-tour"));
        items[0].PublishedAt.ShouldBe(new DateTimeOffset(2026, 11, 9, 7, 30, 0, TimeSpan.Zero));
        items[0].Summary.ShouldBe("The band confirmed twelve dates.");
        items[0].Categories.ShouldBe(["Tour Dates"]);
    }

    [Fact]
    public void Parse_atom_entry_without_published_falls_back_to_updated()
    {
        var items = ParseFile("synthetic-atom.xml", "atom-source");

        items[1].PublishedAt.ShouldBe(new DateTimeOffset(2026, 11, 8, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Parse_skips_items_without_title_or_absolute_link_and_keeps_the_rest()
    {
        var titles = ParseFile("synthetic-edge-cases.xml", "edge").Select(item => item.Title).ToList();

        titles.ShouldBe(["Good Item", "Item Without Date", "Item With Garbage Date", "Item With GMT Date"]);
    }

    [Fact]
    public void Parse_missing_or_unparseable_date_becomes_null_without_dropping_the_item()
    {
        var items = ParseFile("synthetic-edge-cases.xml", "edge");

        items.Single(item => item.Title == "Item Without Date").PublishedAt.ShouldBeNull();
        items.Single(item => item.Title == "Item With Garbage Date").PublishedAt.ShouldBeNull();
    }

    [Fact]
    public void Parse_reads_gmt_dates_as_utc()
    {
        var item = ParseFile("synthetic-edge-cases.xml", "edge").Single(item => item.Title == "Item With GMT Date");

        item.PublishedAt.ShouldBe(new DateTimeOffset(2026, 11, 8, 7, 5, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Parse_truncates_long_summaries_to_the_limit()
    {
        var longText = string.Join(' ', Enumerable.Repeat("word", 1000));
        var xml = $"""
            <rss version="2.0"><channel><title>t</title><link>https://x.example.com</link><description>d</description>
            <item><title>Long</title><link>https://x.example.com/long</link><description>{longText}</description></item>
            </channel></rss>
            """;

        var summary = RssFeedClient.Parse("long", ToStream(xml)).Single().Summary;

        summary.ShouldNotBeNull();
        summary.Length.ShouldBeLessThanOrEqualTo(RssFeedClient.MaxSummaryLength);
        summary.ShouldEndWith("…");
    }

    [Theory]
    [InlineData("<rss version=\"2.0\"><channel><item><title>unclosed")]
    [InlineData("<rss><channel><title>no version attribute</title></channel></rss>")]
    [InlineData("<html><body>Cloudflare challenge page</body></html>")]
    [InlineData("")]
    public void Parse_unreadable_feed_throws_xml_exception(string body)
    {
        Should.Throw<XmlException>(() => RssFeedClient.Parse("broken", ToStream(body)));
    }

    [Fact]
    public void Parse_does_not_resolve_external_entities()
    {
        var xml = """
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <rss version="2.0"><channel><title>t</title><link>https://x.example.com</link><description>d</description>
            <item><title>Entity &xxe;</title><link>https://x.example.com/a</link></item>
            </channel></rss>
            """;

        var items = SafeParse(xml);

        items.ShouldAllBe(item => !item.Title.Contains("root:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_sends_identifying_user_agent_and_parses_the_response()
    {
        HttpRequestMessage? sentRequest = null;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(request =>
        {
            sentRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(File.OpenRead(Path.Combine(FixturesDirectory, "pitchfork.xml"))),
            };
        }));
        var client = new RssFeedClient(httpClient);

        var items = await client.FetchAsync(Feed("pitchfork"), CancellationToken.None);

        items.Count.ShouldBe(4);
        sentRequest.ShouldNotBeNull();
        sentRequest.RequestUri.ShouldBe(Feed("pitchfork").Url);
        sentRequest.Headers.UserAgent.ToString().ShouldStartWith("TheBlueslandWeekly/");
    }

    [Fact]
    public async Task FetchAsync_non_success_status_throws_http_request_exception()
    {
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var client = new RssFeedClient(httpClient);

        await Should.ThrowAsync<HttpRequestException>(() => client.FetchAsync(Feed("nme"), CancellationToken.None));
    }

    [Fact]
    public async Task FetchAsync_feed_slower_than_the_timeout_is_cancelled_with_timeout_exception()
    {
        using var httpClient = new HttpClient(new HangingHttpMessageHandler());
        var client = new RssFeedClient(httpClient, TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<TimeoutException>(() => client.FetchAsync(Feed("consequence"), CancellationToken.None));
    }

    [Fact]
    public async Task FetchAsync_caller_cancellation_is_not_reported_as_a_timeout()
    {
        using var httpClient = new HttpClient(new HangingHttpMessageHandler());
        var client = new RssFeedClient(httpClient, TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() => client.FetchAsync(Feed("consequence"), cts.Token));
    }

    [Fact]
    public async Task FetchAsync_response_larger_than_the_limit_is_rejected_as_unreadable()
    {
        var oversized = new byte[RssFeedClient.MaxResponseBytes + 1];
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(oversized),
        }));

        await Should.ThrowAsync<XmlException>(() => new RssFeedClient(httpClient).FetchAsync(Feed("huge"), CancellationToken.None));
    }

    [Fact]
    public void Parse_removes_control_characters_and_lone_surrogates_that_postgres_jsonb_rejects()
    {
        // XML forbids &#0; outright, so hostile feeds carry it HTML-encoded inside the XML text
        // (&amp;#0;) - it only becomes a real NUL after our own HtmlDecode, which is the case to cover.
        var item = ParseItem("Null&amp;#0;Byte Title", "Bell&amp;#7; and &amp;#xD800;lone surrogate");

        item.Title.ShouldBe("Null Byte Title");
        item.Summary.ShouldNotBeNull();
        item.Summary.ShouldStartWith("Bell and");
        (item.Title + item.Summary).ShouldAllBe(c => !char.IsControl(c) && !char.IsSurrogate(c));
    }

    [Fact]
    public void Parse_truncation_never_splits_a_surrogate_pair()
    {
        var text = new string('a', RssFeedClient.MaxSummaryLength - 2) + string.Concat(Enumerable.Repeat("🎸", 50));

        var summary = ParseItem("Title", text).Summary!;

        summary.ShouldEndWith("…");
        summary.ShouldAllBe(c => !char.IsSurrogate(c) || summary.Contains("🎸"));
        char.IsHighSurrogate(summary[^2]).ShouldBeFalse();
    }

    [Fact]
    public void Parse_skips_an_item_whose_link_is_longer_than_the_limit()
    {
        var longLink = "https://x.example.com/" + new string('a', RssFeedClient.MaxUrlLength);

        var items = RssFeedClient.Parse("long-url", ToStream(Rss($"<item><title>Long link</title><link>{longLink}</link></item>")));

        items.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_does_not_use_an_enclosure_as_the_article_link()
    {
        var items = RssFeedClient.Parse("enclosure", ToStream(Rss(
            "<item><title>Podcast</title><enclosure url=\"https://cdn.example.com/episode.mp3\" length=\"1\" type=\"audio/mpeg\"/></item>")));

        items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Mon, 09 Nov 2026 10:00:00 -0500", 15)]
    [InlineData("Fri, 09 Nov 2026 10:00:00 +0000", 10)]
    [InlineData("09 Nov 2026 10:00:00 +0200", 8)]
    public void Parse_reads_offsets_and_tolerates_a_wrong_day_name(string pubDate, int expectedUtcHour)
    {
        var items = RssFeedClient.Parse("dates", ToStream(Rss(
            $"<item><title>t</title><link>https://x.example.com/a</link><pubDate>{pubDate}</pubDate></item>")));

        items.Single().PublishedAt.ShouldBe(new DateTimeOffset(2026, 11, 9, expectedUtcHour, 0, 0, TimeSpan.Zero));
    }

    private static IReadOnlyList<FeedItem> ParseFixture(string feedId) => ParseFile($"{feedId}.xml", feedId);

    private static IReadOnlyList<FeedItem> ParseFile(string fileName, string source)
    {
        using var stream = File.OpenRead(Path.Combine(FixturesDirectory, fileName));
        return RssFeedClient.Parse(source, stream);
    }

    private static IReadOnlyList<FeedItem> SafeParse(string xml)
    {
        try
        {
            return RssFeedClient.Parse("xxe", ToStream(xml));
        }
        catch (XmlException)
        {
            // Rejecting the DTD outright is also a safe outcome.
            return [];
        }
    }

    private static FeedItem ParseItem(string title, string description) =>
        RssFeedClient.Parse("clean", ToStream(Rss(
            $"<item><title>{title}</title><link>https://x.example.com/a</link><description>{description}</description></item>"))).Single();

    private static string Rss(string items) =>
        $"<rss version=\"2.0\"><channel><title>t</title><link>https://x.example.com</link><description>d</description>{items}</channel></rss>";

    private static MemoryStream ToStream(string xml) => new(System.Text.Encoding.UTF8.GetBytes(xml));

    private static FeedConfig Feed(string id) => new(id, id, new Uri($"https://{id}.example.com/feed"), Enabled: true);

    private sealed class HangingHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
