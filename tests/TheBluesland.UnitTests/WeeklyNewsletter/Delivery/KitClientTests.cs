using System.Net;
using System.Text.Json;
using Shouldly;
using TheBluesland.UnitTests.WeeklyNewsletter.Drafting;
using TheBluesland.WeeklyNewsletter.Delivery;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Delivery;

/// <summary>Kit API v4 broadcasts as documented on developers.kit.com (read 2026-11-10).</summary>
public sealed class KitClientTests
{
    private static readonly KitBroadcastRequest Request = new(
        "thebluesland-weekly:2026-W47:tr",
        new IssueEmail("Subject", "Preview.", "<p>x</p>"),
        new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.FromHours(3)),
        111,
        Public: false);

    [Fact]
    public async Task Create_sends_the_key_in_a_header_and_schedules_for_one_tag()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.Created, """{"broadcast":{"id":77,"status":"scheduled"}}"""));

        var id = await Client(handler).CreateAsync(Request, CancellationToken.None);

        id.ShouldBe(77);
        var (request, body) = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.ToString().ShouldBe("https://api.kit.com/v4/broadcasts");
        request.RequestUri.ToString().ShouldNotContain("secret");
        request.Headers.GetValues("X-Kit-Api-Key").ShouldBe(["secret"]);
        request.Headers.Authorization.ShouldBeNull();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        root.GetProperty("send_at").GetString().ShouldBe("2026-11-16T08:00:00+03:00");
        root.GetProperty("published_at").GetString().ShouldBe("2026-11-16T08:00:00+03:00");
        root.GetProperty("description").GetString().ShouldBe("thebluesland-weekly:2026-W47:tr");
        root.GetProperty("public").GetBoolean().ShouldBeFalse();
        root.GetProperty("subject").GetString().ShouldBe("Subject");
        root.GetProperty("preview_text").GetString().ShouldBe("Preview.");
        root.GetProperty("content").GetString().ShouldBe("<p>x</p>");
        var filter = root.GetProperty("subscriber_filter")[0];
        filter.GetProperty("all")[0].GetProperty("type").GetString().ShouldBe("tag");
        filter.GetProperty("all")[0].GetProperty("ids")[0].GetInt64().ShouldBe(111);
        filter.GetProperty("any").ValueKind.ShouldBe(JsonValueKind.Null);
        root.TryGetProperty("email_template_id", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Update_keeps_the_broadcasts_template_and_delete_targets_the_id()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.OK, """{"broadcast":{"id":77}}"""), _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = Client(handler);

        await client.UpdateAsync(77, Request, emailTemplateId: 5, CancellationToken.None);
        await client.DeleteAsync(77, CancellationToken.None);

        handler.Requests[0].Request.Method.ShouldBe(HttpMethod.Put);
        handler.Requests[0].Request.RequestUri!.ToString().ShouldBe("https://api.kit.com/v4/broadcasts/77");
        JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("email_template_id").GetInt64().ShouldBe(5);
        handler.Requests[1].Request.Method.ShouldBe(HttpMethod.Delete);
        handler.Requests[1].Request.RequestUri!.ToString().ShouldBe("https://api.kit.com/v4/broadcasts/77");
    }

    [Fact]
    public async Task List_follows_the_cursor_and_reads_status_template_and_tag()
    {
        var handler = new QueueHandler(
            _ => Json(HttpStatusCode.OK, """
                {"broadcasts":[{"id":1,"description":"thebluesland-weekly:2026-W47:tr","subject":"S","preview_text":"P","content":"<p>x</p>",
                  "send_at":"2026-11-16T05:00:00Z","status":"scheduled","email_template":{"id":5,"name":"Classic"},
                  "subscriber_filter":[{"all":[{"type":"tag","ids":[111]}],"any":null,"none":null}]}],
                 "pagination":{"has_previous_page":false,"has_next_page":true,"start_cursor":"a","end_cursor":"b c","per_page":1}}
                """),
            _ => Json(HttpStatusCode.OK, """{"broadcasts":[{"id":2,"status":"completed"}],"pagination":{"has_previous_page":true,"has_next_page":false,"start_cursor":"c","end_cursor":"d","per_page":1}}"""));

        var broadcasts = await Client(handler).ListAsync(CancellationToken.None);

        broadcasts.Select(broadcast => broadcast.Id).ShouldBe([1L, 2L]);
        broadcasts[0].ShouldBe(new KitBroadcast(1, "thebluesland-weekly:2026-W47:tr", "S", "P", "<p>x</p>", new DateTimeOffset(2026, 11, 16, 5, 0, 0, TimeSpan.Zero), "scheduled", 5, 111));
        handler.Requests[1].Request.RequestUri!.Query.ShouldBe("?per_page=1000&after=b%20c");
    }

    [Fact]
    public async Task Rate_limits_and_server_errors_are_retried()
    {
        var handler = new QueueHandler(
            _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("Retry later") },
            _ => new HttpResponseMessage(HttpStatusCode.BadGateway),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Client(handler).DeleteAsync(1, CancellationToken.None);

        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_refusal_carries_kits_errors_but_never_the_key()
    {
        var handler = new QueueHandler(_ => Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Broadcast has already been sent."]}"""));

        var error = await Should.ThrowAsync<KitException>(() => Client(handler).DeleteAsync(1, CancellationToken.None));

        error.Message.ShouldContain("422");
        error.Message.ShouldContain("Broadcast has already been sent.");
        error.Message.ShouldNotContain("secret");
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_create_that_timed_out_after_kit_made_the_broadcast_is_found_not_posted_twice()
    {
        var handler = new QueueHandler(
            _ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."),
            _ => Json(HttpStatusCode.OK, """{"broadcasts":[{"id":5,"description":"other"},{"id":88,"description":"thebluesland-weekly:2026-W47:tr","status":"scheduled"}],"pagination":{"has_next_page":false}}"""));

        var id = await Client(handler).CreateAsync(Request, CancellationToken.None);

        id.ShouldBe(88);
        handler.Requests.Select(item => item.Request.Method).ShouldBe([HttpMethod.Post, HttpMethod.Get]);
    }

    [Fact]
    public async Task A_create_that_got_a_server_error_after_kit_made_the_broadcast_is_found_not_posted_twice()
    {
        var handler = new QueueHandler(
            _ => new HttpResponseMessage(HttpStatusCode.GatewayTimeout),
            _ => Json(HttpStatusCode.OK, """{"broadcasts":[{"id":88,"description":"thebluesland-weekly:2026-W47:tr"}],"pagination":{"has_next_page":false}}"""));

        (await Client(handler).CreateAsync(Request, CancellationToken.None)).ShouldBe(88);

        handler.Requests.Count(item => item.Request.Method == HttpMethod.Post).ShouldBe(1);
    }

    [Fact]
    public async Task A_create_that_failed_without_making_anything_is_posted_again()
    {
        var handler = new QueueHandler(
            _ => new HttpResponseMessage(HttpStatusCode.BadGateway),
            _ => Json(HttpStatusCode.OK, """{"broadcasts":[{"id":5,"description":"thebluesland-weekly:2026-W47:en"}],"pagination":{"has_next_page":false}}"""),
            _ => Json(HttpStatusCode.Created, """{"broadcast":{"id":99}}"""));

        (await Client(handler).CreateAsync(Request, CancellationToken.None)).ShouldBe(99);

        handler.Requests.Select(item => item.Request.Method).ShouldBe([HttpMethod.Post, HttpMethod.Get, HttpMethod.Post]);
    }

    [Fact]
    public async Task A_create_that_keeps_failing_gives_up_after_four_posts_and_never_leaks_the_key()
    {
        var handler = new QueueHandler(request => request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json(HttpStatusCode.OK, """{"broadcasts":[],"pagination":{"has_next_page":false}}"""));

        var error = await Should.ThrowAsync<KitException>(() => Client(handler).CreateAsync(Request, CancellationToken.None));

        error.Status.ShouldBe(HttpStatusCode.InternalServerError);
        error.Message.ShouldNotContain("secret");
        handler.Requests.Count(item => item.Request.Method == HttpMethod.Post).ShouldBe(4);
    }

    [Fact]
    public async Task A_rate_limited_create_is_retried_without_listing_because_kit_did_nothing()
    {
        var handler = new QueueHandler(
            _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            _ => Json(HttpStatusCode.Created, """{"broadcast":{"id":99}}"""));

        (await Client(handler).CreateAsync(Request, CancellationToken.None)).ShouldBe(99);

        handler.Requests.Select(item => item.Request.Method).ShouldBe([HttpMethod.Post, HttpMethod.Post]);
    }

    private static KitClient Client(QueueHandler handler) => new(new HttpClient(handler), "secret", TimeSpan.Zero);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
