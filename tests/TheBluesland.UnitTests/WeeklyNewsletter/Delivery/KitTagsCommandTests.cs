using System.Net;
using System.Text.Json;
using Shouldly;
using TheBluesland.UnitTests.WeeklyNewsletter.Drafting;
using TheBluesland.WeeklyNewsletter.Delivery;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Delivery;

/// <summary>issue-delivery spec, T0: the delivery tags are created through Kit's v4 API, idempotently.</summary>
public sealed class KitTagsCommandTests
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    [Fact]
    public async Task Missing_tags_are_created_and_existing_ones_reused()
    {
        var handler = new QueueHandler(
            _ => Json(HttpStatusCode.OK, """{"tags":[{"id":11,"name":"weekly-tr"},{"id":12,"name":"other"}],"pagination":{"has_previous_page":false,"has_next_page":false,"start_cursor":"a","end_cursor":"b","per_page":1000}}"""),
            _ => Json(HttpStatusCode.Created, """{"tag":{"id":22,"name":"weekly-en"}}"""));

        (await RunAsync(handler)).ShouldBe(0, _error.ToString());

        handler.Requests.Select(request => $"{request.Request.Method} {request.Request.RequestUri}").ShouldBe(
            ["GET https://api.kit.com/v4/tags?per_page=1000", "POST https://api.kit.com/v4/tags"]);
        JsonDocument.Parse(handler.Requests[1].Body).RootElement.GetProperty("name").GetString().ShouldBe("weekly-en");
        handler.Requests.ShouldAllBe(request => request.Request.Headers.GetValues("X-Kit-Api-Key").Single() == "kit_test");
        _output.ToString().ShouldContain("| weekly-tr | 11 | already existed |");
        _output.ToString().ShouldContain("| weekly-en | 22 | created |");
    }

    [Fact]
    public async Task A_rejected_key_fails_with_kits_message()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.Unauthorized, """{"errors":["The access token is invalid"]}"""));

        (await RunAsync(handler)).ShouldBe(1);

        _error.ToString().ShouldContain("401");
        _error.ToString().ShouldContain("The access token is invalid");
        _error.ToString().ShouldNotContain("kit_test");
    }

    [Fact]
    public async Task No_key_or_a_bad_tag_name_stops_before_calling_kit()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK, "{}"));

        (await KitTagsCommand.RunAsync(KitTagsCommand.DefaultTags, null, new HttpClient(handler), _output, _error, CancellationToken.None)).ShouldBe(1);
        (await KitTagsCommand.RunAsync(["Weekly TR"], "kit_test", new HttpClient(handler), _output, _error, CancellationToken.None)).ShouldBe(1);

        handler.Requests.ShouldBeEmpty();
    }

    private Task<int> RunAsync(QueueHandler handler) =>
        KitTagsCommand.RunAsync(KitTagsCommand.DefaultTags, "kit_test", new HttpClient(handler), _output, _error, CancellationToken.None, TimeSpan.Zero);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json) };
}
