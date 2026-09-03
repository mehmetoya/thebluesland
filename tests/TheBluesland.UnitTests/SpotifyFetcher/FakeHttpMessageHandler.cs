namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// Test double standing in for the real network so SpotifyFetcher tests never make an outbound
/// HTTP call (US-003 acceptance criterion: tested against a mocked Spotify response, never the
/// live API). Each test supplies a responder function keyed on the outgoing request.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(_responder(request));
}
