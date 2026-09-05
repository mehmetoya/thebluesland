using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TheBluesland.SpotifyFetcher.CuratorNote;

/// <summary>
/// ADR-0005 madde 3/6 (amended 2026-09-05 to Google's Gemini API - see the ADR's own dated note):
/// the tool's only outbound call besides the read-only Neon query - no Spotify credential, no
/// Spotify request. The API key is read by the caller (Program.cs) from the
/// <c>GEMINI_API_KEY</c> environment variable, scoped only to <c>suggest-curator-note.yml</c>, and
/// sent as a header (never a query-string parameter) so it can never end up in a logged request
/// URL. Retries transient 5xx/429 responses a few times with a short delay - a live
/// suggest-curator-note.yml run on 2026-09-05 hit a genuine (non-code) 503 from Gemini being
/// momentarily overloaded, which otherwise surfaces as an unhandled exception instead of the
/// clean, actionable message <see cref="CuratorNoteSuggestionService"/>'s caller expects.
/// </summary>
public sealed class GeminiClient : IAiClient
{
    private const string ApiKeyHeaderName = "x-goog-api-key";
    private const int MaxAttempts = 3;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly TimeSpan _retryDelay;

    public GeminiClient(HttpClient httpClient, string apiKey, string model, TimeSpan? retryDelay = null)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            using var response = await SendGenerateRequestAsync(prompt, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await ExtractTextAsync(response, cancellationToken);
            }

            if (!IsTransientFailure(response.StatusCode) || attempt >= MaxAttempts)
            {
                throw new InvalidOperationException(
                    $"Gemini API request failed with {(int)response.StatusCode} {response.StatusCode} " +
                    $"(attempt {attempt} of {MaxAttempts}).");
            }

            await Task.Delay(_retryDelay, cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> SendGenerateRequestAsync(string prompt, CancellationToken cancellationToken)
    {
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new GenerateContentRequest(
                [new RequestContent([new RequestPart(prompt)])])),
        };
        request.Headers.Add(ApiKeyHeaderName, _apiKey);

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task<string> ExtractTextAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(cancellationToken: cancellationToken);
        var text = payload?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        return text is { Length: > 0 }
            ? text
            : throw new InvalidOperationException("Gemini response did not include any text content.");
    }

    private static bool IsTransientFailure(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private sealed record GenerateContentRequest(
        [property: JsonPropertyName("contents")] RequestContent[] Contents);

    private sealed record RequestContent(
        [property: JsonPropertyName("parts")] RequestPart[] Parts);

    private sealed record RequestPart(
        [property: JsonPropertyName("text")] string Text);

    private sealed class GenerateContentResponse
    {
        [JsonPropertyName("candidates")]
        public List<ResponseCandidate>? Candidates { get; set; }
    }

    private sealed class ResponseCandidate
    {
        [JsonPropertyName("content")]
        public ResponseContent? Content { get; set; }
    }

    private sealed class ResponseContent
    {
        [JsonPropertyName("parts")]
        public List<ResponsePart>? Parts { get; set; }
    }

    private sealed class ResponsePart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
