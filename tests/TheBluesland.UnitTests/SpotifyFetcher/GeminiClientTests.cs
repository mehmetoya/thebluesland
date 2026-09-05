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
    private const string Model = "gemini-2.5-flash";

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
