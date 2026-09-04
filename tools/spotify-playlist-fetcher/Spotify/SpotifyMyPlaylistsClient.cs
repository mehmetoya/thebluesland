using System.Net.Http.Headers;
using System.Text.Json;

namespace TheBluesland.SpotifyFetcher.Spotify;

/// <summary>
/// Lists the authenticated Spotify account's own playlists (<c>GET /me/playlists</c>) - a
/// read-only discovery mode used only by Program.cs's <c>list-playlists</c> CLI verb, never by the
/// monthly sync path. It never writes to the database and is not invoked from the production web
/// app; it exists so candidate playlists can be identified for editorial curation (title/tags/
/// curator note remain a human decision, spec 11.2) without ever asking Mehmet to paste playlist
/// IDs or handing his Spotify credentials to anything outside the existing
/// <c>sync-spotify.yml</c> secret scope (SEC-001).
/// </summary>
public sealed class SpotifyMyPlaylistsClient
{
    private const string BaseUrl = "https://api.spotify.com/v1";

    private readonly HttpClient _httpClient;

    public SpotifyMyPlaylistsClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<SpotifyOwnedPlaylistSummary>> ListAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var results = new List<SpotifyOwnedPlaylistSummary>();
        // Requests both "tracks.total" and "items.total": the simplified playlist object historically
        // used "tracks", but the February 2026 migration that renamed the "Get Playlist" endpoint's
        // top-level tracks container to "items" (see SpotifyPlaylistClient's XML doc) may have touched
        // this shared object shape too. MapPlaylist below checks both, whichever Spotify sends back.
        string? nextUrl = $"{BaseUrl}/me/playlists" +
                           "?limit=50&fields=items(id,name,description,public,tracks.total,items.total,owner.display_name),next";

        while (nextUrl is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsElement.EnumerateArray())
                {
                    // A playlist can come back null here if it was deleted between page requests.
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        results.Add(MapPlaylist(item));
                    }
                }
            }

            nextUrl = root.TryGetProperty("next", out var nextElement) && nextElement.ValueKind != JsonValueKind.Null
                ? nextElement.GetString()
                : null;
        }

        return results;
    }

    private static SpotifyOwnedPlaylistSummary MapPlaylist(JsonElement item)
    {
        var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "";
        var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";

        var description = item.TryGetProperty("description", out var descriptionElement)
            && descriptionElement.ValueKind != JsonValueKind.Null
                ? descriptionElement.GetString()
                : null;

        var isPublic = item.TryGetProperty("public", out var publicElement)
            && publicElement.ValueKind == JsonValueKind.True;

        var trackCount = TryGetNestedTotal(item, "tracks") ?? TryGetNestedTotal(item, "items") ?? 0;

        var ownerDisplayName = item.TryGetProperty("owner", out var ownerElement)
            && ownerElement.TryGetProperty("display_name", out var displayNameElement)
            && displayNameElement.ValueKind != JsonValueKind.Null
                ? displayNameElement.GetString()
                : null;

        return new SpotifyOwnedPlaylistSummary(id, name, description, isPublic, trackCount, ownerDisplayName);
    }

    private static int? TryGetNestedTotal(JsonElement item, string containerPropertyName) =>
        item.TryGetProperty(containerPropertyName, out var container)
        && container.ValueKind == JsonValueKind.Object
        && container.TryGetProperty("total", out var totalElement)
        && totalElement.ValueKind == JsonValueKind.Number
            ? totalElement.GetInt32()
            : null;
}

/// <summary>
/// One row of <see cref="SpotifyMyPlaylistsClient.ListAsync"/>'s output - printed to the CLI/job
/// summary only (see Program.cs's <c>list-playlists</c> verb), never persisted to
/// <c>spotify_playlist_cache</c> or a content file.
/// </summary>
public sealed record SpotifyOwnedPlaylistSummary(
    string Id,
    string Name,
    string? Description,
    bool IsPublic,
    int TrackCount,
    string? OwnerDisplayName);
