using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TheBluesland.SpotifyFetcher.Spotify;

/// <summary>
/// Exchanges the long-lived Spotify refresh token for a short-lived access token via the
/// Authorization Code + PKCE refresh-token grant. The interactive first-token step of that flow
/// happens once, out-of-band, by Mehmet (spec section 18.4); this tool only ever performs the
/// refresh-token exchange, never the interactive authorization step.
/// </summary>
public sealed class SpotifyAuthClient
{
    private const string TokenEndpoint = "https://accounts.spotify.com/api/token";

    private readonly HttpClient _httpClient;

    public SpotifyAuthClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GetAccessTokenAsync(string clientId, string refreshToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
            }),
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
        return payload?.AccessToken is { Length: > 0 } accessToken
            ? accessToken
            : throw new InvalidOperationException("Spotify token response did not include an access token.");
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }
}
