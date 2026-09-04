using System.Net;
using System.Text;
using Shouldly;
using TheBluesland.SpotifyFetcher.Spotify;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// The read-only "list my playlists" discovery mode (Program.cs's <c>list-playlists</c> verb) -
/// never the live API in tests, mirroring <see cref="SpotifyPlaylistClientTests"/>.
/// </summary>
public sealed class SpotifyMyPlaylistsClientTests
{
    private const string AccessToken = "mocked-access-token";

    [Fact]
    public async Task ListAsync_maps_every_field_and_collects_all_pages()
    {
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(BuildTwoPageResponder()));
        var client = new SpotifyMyPlaylistsClient(httpClient);

        var playlists = await client.ListAsync(AccessToken, CancellationToken.None);

        playlists.Count.ShouldBe(2);

        var first = playlists.Single(p => p.Id == "0iJt9LMebhOY0KSHSJw3cS");
        first.Name.ShouldBe("Masterpieces of Erkin the Father");
        first.Description.ShouldBe("Anadolu rock essentials.");
        first.IsPublic.ShouldBeTrue();
        first.TrackCount.ShouldBe(24);
        first.OwnerDisplayName.ShouldBe("Mehmet Oya");

        var second = playlists.Single(p => p.Id == "second-page-id");
        second.Name.ShouldBe("From the second page");
        second.IsPublic.ShouldBeFalse();
    }

    [Fact]
    public async Task ListAsync_tolerates_a_missing_description_and_owner()
    {
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => JsonResponse(
            """
            {
              "items": [
                { "id": "no-extras", "name": "Bare Playlist", "public": true, "tracks": { "total": 0 } }
              ],
              "next": null
            }
            """)));
        var client = new SpotifyMyPlaylistsClient(httpClient);

        var playlists = await client.ListAsync(AccessToken, CancellationToken.None);

        var playlist = playlists.ShouldHaveSingleItem();
        playlist.Description.ShouldBeNull();
        playlist.OwnerDisplayName.ShouldBeNull();
        playlist.TrackCount.ShouldBe(0);
    }

    [Fact]
    public async Task ListAsync_skips_a_null_playlist_entry()
    {
        // Spotify can return a null item here if a playlist was deleted between page requests.
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => JsonResponse(
            """{"items":[null,{"id":"still-here","name":"Still Here","public":true,"tracks":{"total":1}}],"next":null}""")));
        var client = new SpotifyMyPlaylistsClient(httpClient);

        var playlists = await client.ListAsync(AccessToken, CancellationToken.None);

        var playlist = playlists.ShouldHaveSingleItem();
        playlist.Id.ShouldBe("still-here");
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> BuildTwoPageResponder() => request =>
    {
        var query = request.RequestUri!.Query;

        if (query.Contains("offset=50"))
        {
            return JsonResponse(SecondPageJson);
        }

        return JsonResponse(FirstPageJson);
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private const string FirstPageJson =
        """
        {
          "items": [
            {
              "id": "0iJt9LMebhOY0KSHSJw3cS",
              "name": "Masterpieces of Erkin the Father",
              "description": "Anadolu rock essentials.",
              "public": true,
              "tracks": { "total": 24 },
              "owner": { "display_name": "Mehmet Oya" }
            }
          ],
          "next": "https://api.spotify.com/v1/me/playlists?offset=50&limit=50"
        }
        """;

    private const string SecondPageJson =
        """
        {
          "items": [
            {
              "id": "second-page-id",
              "name": "From the second page",
              "description": null,
              "public": false,
              "tracks": { "total": 5 },
              "owner": { "display_name": "Mehmet Oya" }
            }
          ],
          "next": null
        }
        """;
}
