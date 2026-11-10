using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>
/// Gemini <c>generateContent</c> with a JSON response schema (issue-drafting spec, Design section 2).
/// Same pattern as tools/spotify-playlist-fetcher's client, written separately because this tool may
/// not reference the Spotify tool (spec Assumptions 3). The key goes in a header, never the URL, so
/// it cannot end up in a logged request line. Transient 429/5xx answers are retried after a pause.
/// </summary>
public sealed class GeminiClient(HttpClient httpClient, string apiKey, string model, TimeSpan? retryDelay = null)
{
    public const string ApiKeyVariable = "GEMINI_API_KEY";
    public const string ModelVariable = "GEMINI_MODEL";

    // Same free-tier default as suggest-curator-note.yml (verified there against the live API).
    public const string DefaultModel = "gemini-3.8-flash";

    private const int MaxAttempts = 3;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);

    public async Task<string> GenerateJsonAsync(string prompt, object responseSchema, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
            {
                Content = JsonContent.Create(new
                {
                    contents = new[] { new { parts = new[] { new { text = prompt } } } },
                    generationConfig = new { temperature = 0.4, responseMimeType = "application/json", responseSchema },
                }),
            };
            request.Headers.Add("x-goog-api-key", apiKey);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                // Connection reset or DNS hiccup: worth another try, like a 5xx.
                await Task.Delay(_retryDelay, cancellationToken);
                continue;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    Response? payload;
                    try
                    {
                        payload = await response.Content.ReadFromJsonAsync<Response>(cancellationToken);
                    }
                    catch (JsonException ex)
                    {
                        throw new DraftFormatException($"Gemini's answer was not the expected JSON envelope: {ex.Message}");
                    }

                    return payload?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text is { Length: > 0 } text
                        ? text
                        : throw new DraftFormatException("Gemini's answer contained no text.");
                }

                if (!IsTransient(response.StatusCode) || attempt >= MaxAttempts)
                {
                    throw new HttpRequestException(
                        $"Gemini request failed with {(int)response.StatusCode} {response.StatusCode} (attempt {attempt} of {MaxAttempts}).",
                        null,
                        response.StatusCode);
                }

                await Task.Delay(RetryDelay(response), cancellationToken);
            }
        }
    }

    /// <summary>A model id may only be a path segment like "gemini-3.8-flash".</summary>
    public static bool IsValidModel(string model) =>
        model.Length is > 0 and <= 64 && model.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-');

    /// <summary>Gemini's own Retry-After when it sends one (free-tier 429s), capped at a minute.</summary>
    private TimeSpan RetryDelay(HttpResponseMessage response)
    {
        var suggested = response.Headers.RetryAfter?.Delta;
        return suggested is { } delta && delta > _retryDelay ? (delta < MaxRetryDelay ? delta : MaxRetryDelay) : _retryDelay;
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private sealed record Response([property: JsonPropertyName("candidates")] List<Candidate>? Candidates);

    private sealed record Candidate([property: JsonPropertyName("content")] Content? Content);

    private sealed record Content([property: JsonPropertyName("parts")] List<Part>? Parts);

    private sealed record Part([property: JsonPropertyName("text")] string? Text);
}
