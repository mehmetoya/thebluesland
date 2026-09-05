using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TheBluesland.SpotifyFetcher.CuratorNote;

/// <summary>
/// ADR-0005 madde 3/6 (amended 2026-09-05 to Google's Gemini API - see the ADR's own dated note):
/// the tool's only outbound call besides the read-only Neon query - no Spotify credential, no
/// Spotify request. The API key is read by the caller (Program.cs) from the
/// <c>GEMINI_API_KEY</c> environment variable, scoped only to <c>suggest-curator-note.yml</c>, and
/// sent as a header (never a query-string parameter) so it can never end up in a logged request
/// URL.
/// </summary>
public sealed class GeminiClient : IAiClient
{
    private const string ApiKeyHeaderName = "x-goog-api-key";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiClient(HttpClient httpClient, string apiKey, string model)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new GenerateContentRequest(
                [new RequestContent([new RequestPart(prompt)])])),
        };
        request.Headers.Add(ApiKeyHeaderName, _apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(cancellationToken: cancellationToken);
        var text = payload?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        return text is { Length: > 0 }
            ? text
            : throw new InvalidOperationException("Gemini response did not include any text content.");
    }

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
