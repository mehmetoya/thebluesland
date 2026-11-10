using System.Net;
using System.Text.Json;
using Shouldly;
using TheBluesland.UnitTests.WeeklyNewsletter.Clustering;
using TheBluesland.UnitTests.WeeklyNewsletter.Drafting;
using TheBluesland.WeeklyNewsletter.Delivery;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Delivery.DeliveryTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Delivery;

/// <summary>kit-smoke: create a never-sent draft, read it back, delete it, report the account's non-secret facts.</summary>
public sealed class KitSmokeCommandTests : IDisposable
{
    private const string Draft = """{"broadcast":{"id":700,"description":"thebluesland-weekly-smoke:20261110T120000Z","status":"draft","send_at":null,"subscriber_filter":[{"all":[{"type":"tag","ids":[111]}],"any":null,"none":null}]}}""";
    private const string Account = """{"user":{"email":"mehmet@example.com"},"account":{"name":"TheBluesland","plan_type":"free","primary_email_address":"mehmet@example.com"}}""";

    private readonly string _tool = Path.Combine(Path.GetTempPath(), $"smoke-{Guid.NewGuid():N}");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public KitSmokeCommandTests()
    {
        Directory.CreateDirectory(_tool);
        File.WriteAllText(Path.Combine(_tool, "delivery.yml"), ValidYaml);
    }

    public void Dispose() => Directory.Delete(_tool, recursive: true);

    [Fact]
    public async Task A_working_kit_creates_an_unsendable_draft_reads_it_back_and_deletes_it()
    {
        var handler = Kit();

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());

        handler.Requests.Select(item => $"{item.Request.Method} {item.Request.RequestUri!.AbsolutePath}")
            .ShouldBe(["POST /v4/broadcasts", "GET /v4/broadcasts/700", "DELETE /v4/broadcasts/700", "GET /v4/account"]);
        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        var root = body.RootElement;
        root.GetProperty("description").GetString().ShouldBe("thebluesland-weekly-smoke:20261110T120000Z");
        root.GetProperty("send_at").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("published_at").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("public").GetBoolean().ShouldBeFalse();
        root.GetProperty("subscriber_filter")[0].GetProperty("all")[0].GetProperty("ids")[0].GetInt64().ShouldBe(111);
        var output = _output.ToString();
        output.ShouldContain("| create | ✅ ok |");
        output.ShouldContain("| read | ✅ ok |");
        output.ShouldContain("| delete | ✅ ok |");
        output.ShouldContain("plan free");
        output.ShouldContain("default sender m***@example.com");
        output.ShouldNotContain("mehmet@example.com");
        output.ShouldNotContain("test-key");
    }

    [Fact]
    public async Task A_broadcast_that_reads_back_scheduled_fails_the_run_but_is_still_deleted()
    {
        var handler = Kit(readJson: Draft.Replace("\"draft\"", "\"scheduled\"", StringComparison.Ordinal));

        (await RunAsync(handler)).ShouldBe(1);

        handler.Requests.ShouldContain(item => item.Request.Method == HttpMethod.Delete);
        _output.ToString().ShouldContain("| read | ❌ failed | unexpected: status 'scheduled' (expected draft) |");
    }

    [Fact]
    public async Task A_wrong_tag_filter_fails_the_run()
    {
        var handler = Kit(readJson: Draft.Replace("[111]", "[222]", StringComparison.Ordinal));

        (await RunAsync(handler)).ShouldBe(1);

        _output.ToString().ShouldContain("tag filter 222 (expected 111)");
    }

    [Fact]
    public async Task A_refused_create_fails_without_a_delete()
    {
        var handler = new QueueHandler(request => request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("""{"errors":["Forbidden"]}""") }
            : Json(HttpStatusCode.OK, Account));

        (await RunAsync(handler)).ShouldBe(1);

        handler.Requests.ShouldNotContain(item => item.Request.Method == HttpMethod.Delete);
        _output.ToString().ShouldContain("| create | ❌ failed |");
        _output.ToString().ShouldContain("403");
    }

    [Fact]
    public async Task An_unavailable_account_endpoint_does_not_fail_the_smoke_test()
    {
        var handler = Kit(account: new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("""{"errors":["Forbidden"]}""") });

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());

        _output.ToString().ShouldContain("| account | ℹ️ | unavailable (");
    }

    [Fact]
    public async Task No_key_stops_before_calling_kit()
    {
        var handler = Kit();

        (await RunAsync(handler, apiKey: null)).ShouldBe(1);

        handler.Requests.ShouldBeEmpty();
        _error.ToString().ShouldContain("KIT_API_KEY is not set.");
    }

    private static QueueHandler Kit(string readJson = Draft, HttpResponseMessage? account = null) =>
        new(request => request.Method.Method switch
        {
            "POST" => Json(HttpStatusCode.Created, """{"broadcast":{"id":700,"status":"draft"}}"""),
            "DELETE" => new HttpResponseMessage(HttpStatusCode.NoContent),
            _ when request.RequestUri!.AbsolutePath == "/v4/account" => account ?? Json(HttpStatusCode.OK, Account),
            _ => Json(HttpStatusCode.OK, readJson),
        });

    private Task<int> RunAsync(QueueHandler handler, string? apiKey = "test-key") =>
        KitSmokeCommand.RunAsync(_tool, apiKey, new HttpClient(handler), new FixedTimeProvider(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero)), _output, _error, CancellationToken.None, TimeSpan.Zero);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json) };
}
