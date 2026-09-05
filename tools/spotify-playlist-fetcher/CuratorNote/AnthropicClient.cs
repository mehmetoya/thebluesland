using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TheBluesland.SpotifyFetcher.CuratorNote;

/// <summary>
/// ADR-0005 madde 3/6: the tool's only outbound call besides the read-only Neon query - no
/// Spotify credential, no Spotify request. The API key is read by the caller (Program.cs) from
/// the <c>ANTHROPIC_API_KEY</c> environment variable, scoped only to
/// <c>suggest-curator-note.yml</c>, and never logged here.
/// </summary>
public sealed class AnthropicClient : IAnthropicClient
{
    private const string MessagesEndpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxTokens = 1024;

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public AnthropicClient(HttpClient httpClient, string apiKey, string model)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesEndpoint)
        {
            Content = JsonContent.Create(new MessagesRequest(
                _model,
                MaxTokens,
                [new MessagesRequestMessage("user", prompt)])),
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<MessagesResponse>(cancellationToken: cancellationToken);
        var text = payload?.Content?.FirstOrDefault(block => block.Type == "text")?.Text;

        return text is { Length: > 0 }
            ? text
            : throw new InvalidOperationException("Anthropic response did not include any text content.");
    }

    private sealed record MessagesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("messages")] MessagesRequestMessage[] Messages);

    private sealed record MessagesRequestMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class MessagesResponse
    {
        [JsonPropertyName("content")]
        public List<MessagesResponseContentBlock>? Content { get; set; }
    }

    private sealed class MessagesResponseContentBlock
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
