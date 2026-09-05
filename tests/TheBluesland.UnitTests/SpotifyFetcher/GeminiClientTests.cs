using System.Net;
using System.Text;
using System.Text.Json;
using Shouldly;
using TheBluesland.SpotifyFetcher.CuratorNote;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// ADR-0005 (amended 2026-09-05 to Gemini): the API key must be sent as a header, never a
/// query-string parameter, so it can never end up in a logged request URL - and the response's
/// nested candidate/content/part text must be extracted correctly. Never a live API call.
/// </summary>
public sealed class GeminiClientTests
{
    private const string ApiKey = "test-api-key";
    private const string Model = "gemini-3.8-flash";

    [Fact]
    public async Task GenerateAsync_sends_the_api_key_as_a_header_and_the_prompt_as_the_request_body()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(request =>
        {
            capturedRequest = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(SuccessResponseJson("A drafted curator note."));
        }));
        var client = new GeminiClient(httpClient, ApiKey, Model);

        await client.GenerateAsync("Write a curator note.", CancellationToken.None);

        capturedRequest!.Headers.TryGetValues("x-goog-api-key", out var apiKeyHeader).ShouldBeTrue();
        apiKeyHeader!.ShouldContain(ApiKey);
        capturedRequest.RequestUri!.ToString().ShouldNotContain(ApiKey);
        capturedRequest.RequestUri.ToString().ShouldContain($"models/{Model}:generateContent");

        using var bodyDocument = JsonDocument.Parse(capturedBody!);
        bodyDocument.RootElement
            .GetProperty("contents")[0]
            .GetProperty("parts")[0]
            .GetProperty("text").GetString()
            .ShouldBe("Write a curator note.");
    }

    [Fact]
    public async Task GenerateAsync_returns_the_first_candidate_s_text()
    {
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ =>
            JsonResponse(SuccessResponseJson("A drafted curator note."))));
        var client = new GeminiClient(httpClient, ApiKey, Model);

        var result = await client.GenerateAsync("prompt", CancellationToken.None);

        result.ShouldBe("A drafted curator note.");
    }

    [Fact]
    public async Task GenerateAsync_throws_when_the_response_has_no_candidates()
    {
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ =>
            JsonResponse("""{"candidates":[]}""")));
        var client = new GeminiClient(httpClient, ApiKey, Model);

        await Should.ThrowAsync<InvalidOperationException>(
            () => client.GenerateAsync("prompt", CancellationToken.None));
    }

    /// <summary>
    /// A real suggest-curator-note.yml run on 2026-09-05 hit a genuine 503 from Gemini being
    /// momentarily overloaded - a transient, non-code failure that succeeded on a bare re-run.
    /// GeminiClient must absorb that itself rather than making Mehmet notice and re-trigger it.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_retries_a_transient_503_and_returns_the_eventual_success()
    {
        var attempts = 0;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ =>
        {
            attempts++;
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : JsonResponse(SuccessResponseJson("A drafted curator note."));
        }));
        var client = new GeminiClient(httpClient, ApiKey, Model, retryDelay: TimeSpan.Zero);

        var result = await client.GenerateAsync("prompt", CancellationToken.None);

        result.ShouldBe("A drafted curator note.");
        attempts.ShouldBe(2);
    }

    [Fact]
    public async Task GenerateAsync_throws_a_clear_message_after_exhausting_retries_on_a_persistent_503()
    {
        var attempts = 0;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }));
        var client = new GeminiClient(httpClient, ApiKey, Model, retryDelay: TimeSpan.Zero);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => client.GenerateAsync("prompt", CancellationToken.None));

        exception.Message.ShouldContain("503");
        attempts.ShouldBe(3);
    }

    /// <summary>
    /// The bug that motivated the retry logic above (a bad model name) must still fail on the
    /// first attempt - retrying a 404 would only delay a real, actionable error for no benefit.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_does_not_retry_a_non_transient_error_such_as_a_bad_model_name()
    {
        var attempts = 0;
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var client = new GeminiClient(httpClient, ApiKey, Model, retryDelay: TimeSpan.Zero);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => client.GenerateAsync("prompt", CancellationToken.None));

        exception.Message.ShouldContain("404");
        attempts.ShouldBe(1);
    }

    private static string SuccessResponseJson(string text) =>
        $$"""
        {
          "candidates": [
            {
              "content": {
                "role": "model",
                "parts": [ { "text": "{{text}}" } ]
              },
              "finishReason": "STOP"
            }
          ]
        }
        """;

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
