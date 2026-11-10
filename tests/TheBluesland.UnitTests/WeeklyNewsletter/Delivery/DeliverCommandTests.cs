using System.Net;
using Shouldly;
using TheBluesland.UnitTests.WeeklyNewsletter.Clustering;
using TheBluesland.UnitTests.WeeklyNewsletter.Drafting;
using TheBluesland.WeeklyNewsletter.Delivery;
using TheBluesland.WeeklyNewsletter.Drafting;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Delivery.DeliveryTestData;
using static TheBluesland.UnitTests.WeeklyNewsletter.Review.ReviewTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Delivery;

/// <summary>issue-delivery spec, Design section 6: the "deliver" command end to end with a fake Kit.</summary>
public sealed class DeliverCommandTests : IDisposable
{
    /// <summary>Sunday 2026-11-08 12:00 Istanbul: W46 (send Monday 2026-11-09) was just merged.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deliver-{Guid.NewGuid():N}");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public DeliverCommandTests()
    {
        Directory.CreateDirectory(Tool);
        Directory.CreateDirectory(Content);
        File.WriteAllText(Path.Combine(Tool, "delivery.yml"), ValidYaml);
        foreach (var language in DraftLanguage.All)
        {
            File.WriteAllText(Path.Combine(Content, IssueName(language)), Draft(language).IssueMarkdown);
        }
    }

    private string Tool => Path.Combine(_root, "tool");

    private string Content => Path.Combine(_root, "weekly");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task A_merged_issue_is_scheduled_in_both_languages_and_reported()
    {
        var handler = new QueueHandler(EmptyList, Created(501), Created(502));

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());

        handler.Requests.Select(request => request.Request.Method.Method).ShouldBe(["GET", "POST", "POST"]);
        var en = System.Text.Json.JsonDocument.Parse(handler.Requests[1].Body).RootElement;
        var tr = System.Text.Json.JsonDocument.Parse(handler.Requests[2].Body).RootElement;
        en.GetProperty("description").GetString().ShouldBe("thebluesland-weekly:2026-W46:en");
        en.GetProperty("send_at").GetString().ShouldBe("2026-11-09T15:00:00+03:00");
        tr.GetProperty("send_at").GetString().ShouldBe("2026-11-09T08:00:00+03:00");
        tr.GetProperty("subscriber_filter")[0].GetProperty("all")[0].GetProperty("ids")[0].GetInt64().ShouldBe(111);
        tr.GetProperty("content").GetString()!.ShouldContain("<em>Bu sayı yapay zekâ yardımıyla hazırlandı");
        _output.ToString().ShouldContain("| 2026-W46:tr | Mon 2026-11-09 08:00 | ✅ scheduled | 502 |");
    }

    [Fact]
    public async Task A_dry_run_only_reads_kit()
    {
        var handler = new QueueHandler(EmptyList);

        (await RunAsync(handler, dryRun: true)).ShouldBe(0, _error.ToString());

        handler.Requests.ShouldHaveSingleItem().Request.Method.ShouldBe(HttpMethod.Get);
        _output.ToString().ShouldContain("dry run (nothing sent to Kit)");
        _output.ToString().ShouldContain("would schedule");
    }

    [Fact]
    public async Task An_invalid_issue_file_stops_everything_before_kit_is_called()
    {
        File.WriteAllText(Path.Combine(Content, "2026-W46.backups.tr.md"), "x");
        var handler = new QueueHandler(EmptyList);

        (await RunAsync(handler)).ShouldBe(1);

        handler.Requests.ShouldBeEmpty();
        _error.ToString().ShouldContain("not an issue file");
    }

    [Fact]
    public async Task Missing_tag_ids_or_key_stop_everything()
    {
        File.WriteAllText(Path.Combine(Tool, "delivery.yml"), ValidYaml.Replace("kitTagId: 111", "kitTagId: null", StringComparison.Ordinal));
        var handler = new QueueHandler(EmptyList);

        (await RunAsync(handler, apiKey: null)).ShouldBe(1);

        handler.Requests.ShouldBeEmpty();
        _error.ToString().ShouldContain("kitTagId is not set");
        _error.ToString().ShouldContain("KIT_API_KEY is not set.");
    }

    [Fact]
    public async Task Duplicate_broadcasts_change_nothing_and_fail()
    {
        const string twins = """
            {"broadcasts":[{"id":1,"description":"thebluesland-weekly:2026-W46:tr","status":"scheduled"},
                           {"id":2,"description":"thebluesland-weekly:2026-W46:tr","status":"scheduled"}],
             "pagination":{"has_previous_page":false,"has_next_page":false,"start_cursor":"a","end_cursor":"b","per_page":2}}
            """;
        var handler = new QueueHandler(_ => Json(twins));

        (await RunAsync(handler)).ShouldBe(1);

        handler.Requests.ShouldHaveSingleItem();
        _output.ToString().ShouldContain("two Kit broadcasts carry this marker");
    }

    [Fact]
    public async Task A_kit_refusal_fails_the_run_and_says_what_was_already_done()
    {
        var handler = new QueueHandler(EmptyList, Created(501), _ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("""{"errors":["Forbidden"]}""") });

        (await RunAsync(handler)).ShouldBe(1);

        _output.ToString().ShouldContain("| 2026-W46:en | Mon 2026-11-09 15:00 | ✅ scheduled | 501 |");
        _output.ToString().ShouldContain("**Stopped:** thebluesland-weekly:2026-W46:tr");
        _error.ToString().ShouldContain("403");
    }

    private Task<int> RunAsync(QueueHandler handler, bool dryRun = false, string? apiKey = "test-key") =>
        DeliverCommand.RunAsync(Tool, Content, apiKey, dryRun, new HttpClient(handler), new FixedTimeProvider(Now), _output, _error, CancellationToken.None, TimeSpan.Zero);

    private static HttpResponseMessage EmptyList(HttpRequestMessage request) =>
        Json("""{"broadcasts":[],"pagination":{"has_previous_page":false,"has_next_page":false,"start_cursor":null,"end_cursor":null,"per_page":1000}}""");

    private static Func<HttpRequestMessage, HttpResponseMessage> Created(long id) =>
        _ => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"broadcast\":{\"id\":" + id + ",\"status\":\"scheduled\"}}") };

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
}
