using System.Net;
using System.Text;
using Shouldly;
using TheBluesland.SpotifyFetcher.Spotify;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// US-003: the sync tool only ever performs the Authorization Code + PKCE refresh-token grant
/// (spec section 18.4), against a mocked token endpoint - never the live Spotify API in tests.
/// </summary>
public sealed class SpotifyAuthClientTests
{
    [Fact]
    public async Task GetAccessTokenAsync_returns_access_token_from_a_successful_refresh_response()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"mocked-access-token","token_type":"Bearer","expires_in":3600}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        using var httpClient = new HttpClient(handler);
        var authClient = new SpotifyAuthClient(httpClient);

        var accessToken = await authClient.GetAccessTokenAsync("client-id", "refresh-token", CancellationToken.None);

        accessToken.ShouldBe("mocked-access-token");
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.Method.ShouldBe(HttpMethod.Post);
        capturedRequest.RequestUri.ShouldBe(new Uri("https://accounts.spotify.com/api/token"));
    }

    [Fact]
    public async Task GetAccessTokenAsync_throws_when_spotify_rejects_the_refresh_token()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"invalid_grant"}""", Encoding.UTF8, "application/json"),
        });
        using var httpClient = new HttpClient(handler);
        var authClient = new SpotifyAuthClient(httpClient);

        await Should.ThrowAsync<HttpRequestException>(() =>
            authClient.GetAccessTokenAsync("client-id", "bad-refresh-token", CancellationToken.None));
    }
}
