using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TheBluesland.SpotifyFetcher.Spotify;

/// <summary>
/// Reads playlist-level facts from the Spotify Web API. Track-level data (title, id, duration,
/// ISRC) is read only transiently, one page at a time, while paginating the items endpoint solely
/// to compute the distinct artist list, and is discarded as soon as that page's artist names have
/// been extracted - see spec section 9.4 and 11.2. The <c>fields</c> query parameter narrows both
/// requests so Spotify itself never sends fields this tool has no use for.
///
/// Spotify's February 2026 Web API migration removed <c>GET /playlists/{id}/tracks</c> in favour
/// of <c>GET /playlists/{id}/items</c>, and the per-item track payload moved from the <c>track</c>
/// field (now present but always empty, <c>{}</c>) to <c>item</c>; the "Get Playlist" summary
/// endpoint's <c>tracks</c> container was renamed to <c>items</c> at the same time (its inner
/// shape is unchanged). This client reads the new <c>items</c>/<c>item</c> fields accordingly. The
/// new items endpoint additionally requires the <c>playlist-read-private</c> scope on the access
/// token, which must already be present on <c>SPOTIFY_REFRESH_TOKEN</c> from the interactive
/// authorization step (see <see cref="SpotifyAuthClient"/>).
/// </summary>
public sealed class SpotifyPlaylistClient
{
    private const string BaseUrl = "https://api.spotify.com/v1";

    private readonly HttpClient _httpClient;

    public SpotifyPlaylistClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SpotifyPlaylistFetchResult> FetchAsync(
        string spotifyPlaylistId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var summary = await GetPlaylistSummaryAsync(spotifyPlaylistId, accessToken, cancellationToken);
        if (summary is null)
        {
            return new SpotifyPlaylistFetchResult.NotFound();
        }

        var artists = await GetDistinctArtistNamesAsync(spotifyPlaylistId, accessToken, cancellationToken);
        return new SpotifyPlaylistFetchResult.Found(summary with { Artists = artists });
    }

    private async Task<SpotifyPlaylistSummary?> GetPlaylistSummaryAsync(
        string spotifyPlaylistId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/playlists/{Uri.EscapeDataString(spotifyPlaylistId)}" +
                  "?fields=name,description,images,items.total,snapshot_id";

        using var response = await SendAsync(HttpMethod.Get, url, accessToken, cancellationToken);
        if (IsPlaylistUnavailableStatus(response.StatusCode))
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        if (name is null)
        {
            throw new InvalidOperationException(
                $"Spotify playlist response for '{spotifyPlaylistId}' did not include a 'name'.");
        }

        var description = root.TryGetProperty("description", out var descriptionElement)
            && descriptionElement.ValueKind != JsonValueKind.Null
                ? descriptionElement.GetString()
                : null;

        string? coverImageUrl = null;
        if (root.TryGetProperty("images", out var imagesElement)
            && imagesElement.ValueKind == JsonValueKind.Array
            && imagesElement.GetArrayLength() > 0)
        {
            coverImageUrl = imagesElement[0].TryGetProperty("url", out var urlElement)
                ? urlElement.GetString()
                : null;
        }

        var trackCount = root.TryGetProperty("items", out var itemsTotalElement)
            && itemsTotalElement.TryGetProperty("total", out var totalElement)
                ? totalElement.GetInt32()
                : 0;

        var snapshotId = root.TryGetProperty("snapshot_id", out var snapshotElement)
            && snapshotElement.ValueKind != JsonValueKind.Null
                ? snapshotElement.GetString()
                : null;

        return new SpotifyPlaylistSummary
        {
            Name = name,
            Description = description,
            CoverImageUrl = coverImageUrl,
            TrackCount = trackCount,
            SnapshotId = snapshotId,
            Artists = [],
        };
    }

    private async Task<string[]> GetDistinctArtistNamesAsync(
        string spotifyPlaylistId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var artistNames = new SortedSet<string>(StringComparer.Ordinal);
        string? nextUrl = $"{BaseUrl}/playlists/{Uri.EscapeDataString(spotifyPlaylistId)}/items" +
                           "?fields=items(item(artists(name))),next&limit=100";

        while (nextUrl is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, nextUrl, accessToken, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var pageItem in itemsElement.EnumerateArray())
                {
                    CollectArtistNames(pageItem, artistNames);
                }
            }

            nextUrl = root.TryGetProperty("next", out var nextElement) && nextElement.ValueKind != JsonValueKind.Null
                ? nextElement.GetString()
                : null;
        }

        return [.. artistNames];
    }

    private static void CollectArtistNames(JsonElement pageItem, SortedSet<string> artistNames)
    {
        // Since the February 2026 API migration, "track" is present on every page item but is
        // always an empty, deprecated object ({}); the actual track/episode payload is under
        // "item" instead - reading "track" here would silently collect zero artists.
        if (!pageItem.TryGetProperty("item", out var itemDetailElement)
            || itemDetailElement.ValueKind != JsonValueKind.Object)
        {
            return; // Spotify returns a null item for removed/local items; nothing to attribute.
        }

        if (!itemDetailElement.TryGetProperty("artists", out var artistsElement)
            || artistsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var artist in artistsElement.EnumerateArray())
        {
            if (artist.TryGetProperty("name", out var artistNameElement)
                && artistNameElement.GetString() is { Length: > 0 } artistName)
            {
                artistNames.Add(artistName);
            }
        }
    }

    private static bool IsPlaylistUnavailableStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.NotFound;

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _httpClient.SendAsync(request, cancellationToken);
    }
}
