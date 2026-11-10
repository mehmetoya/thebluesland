using System.Net;
using Shouldly;
using TheBluesland.WeeklyNewsletter.Drafting;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

/// <summary>issue-drafting spec, Design section 2; ADR-0006 madde 5 (key handling).</summary>
public sealed class GeminiClientTests
{
    [Fact]
    public async Task GenerateJsonAsync_sends_the_key_as_a_header_and_asks_for_json()
    {
        var handler = new QueueHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(GeminiEnvelope("{\"ok\":true}")) });
        var client = new GeminiClient(new HttpClient(handler), "secret-key", "test-model", TimeSpan.Zero);

        var text = await client.GenerateJsonAsync("prompt", new { type = "OBJECT" }, CancellationToken.None);

        text.ShouldBe("{\"ok\":true}");
        var (request, body) = handler.Requests.ShouldHaveSingleItem();
        request.Headers.GetValues("x-goog-api-key").ShouldBe(["secret-key"]);
        request.RequestUri!.ToString().ShouldBe("https://generativelanguage.googleapis.com/v1beta/models/test-model:generateContent");
        request.RequestUri.ToString().ShouldNotContain("secret-key");
        body.ShouldContain("\"responseMimeType\":\"application/json\"");
        body.ShouldContain("\"text\":\"prompt\"");
    }

    [Fact]
    public async Task GenerateJsonAsync_retries_transient_failures()
    {
        var handler = new QueueHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(GeminiEnvelope("{}")) });

        var text = await new GeminiClient(new HttpClient(handler), "k", "m", TimeSpan.Zero).GenerateJsonAsync("p", new { }, CancellationToken.None);

        text.ShouldBe("{}");
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GenerateJsonAsync_does_not_retry_a_client_error()
    {
        var handler = new QueueHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));

        var error = await Should.ThrowAsync<HttpRequestException>(() => new GeminiClient(new HttpClient(handler), "k", "m", TimeSpan.Zero).GenerateJsonAsync("p", new { }, CancellationToken.None));

        error.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        handler.Requests.Count.ShouldBe(1);
    }
}
