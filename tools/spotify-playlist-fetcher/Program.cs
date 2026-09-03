using Microsoft.EntityFrameworkCore;
using TheBluesland.Data;
using TheBluesland.SpotifyFetcher.Content;
using TheBluesland.SpotifyFetcher.Spotify;
using TheBluesland.SpotifyFetcher.Sync;

// US-003: monthly Spotify Web API sync tool (spec section 12.4, 18.4). Runs out-of-process, only
// from GitHub Actions (US-004) or Mehmet's own machine - never inside the production web app.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
var cancellationToken = cts.Token;

var contentDirectory = args.Length > 0
    ? args[0]
    : Path.Combine(Directory.GetCurrentDirectory(), "content", "playlists");

var frontMatterReader = new PlaylistFrontMatterReader();
var spotifyPlaylistIds = await frontMatterReader.ReadDistinctSpotifyPlaylistIdsAsync(contentDirectory, cancellationToken);

Console.WriteLine($"Found {spotifyPlaylistIds.Count} spotifyPlaylistId value(s) in '{contentDirectory}'.");

if (spotifyPlaylistIds.Count == 0)
{
    Console.WriteLine("Nothing to sync.");
    return 0;
}

var clientId = RequireEnvironmentVariable("SPOTIFY_CLIENT_ID");
var refreshToken = RequireEnvironmentVariable("SPOTIFY_REFRESH_TOKEN");
var connectionString = RequireEnvironmentVariable("NEON_SYNC_CONNECTION_STRING");

using var httpClient = new HttpClient();
var authClient = new SpotifyAuthClient(httpClient);
var playlistClient = new SpotifyPlaylistClient(httpClient);

var accessToken = await authClient.GetAccessTokenAsync(clientId, refreshToken, cancellationToken);

var optionsBuilder = new DbContextOptionsBuilder<TheBlueslandDbContext>().UseNpgsql(connectionString);
await using var dbContext = new TheBlueslandDbContext(optionsBuilder.Options);

var syncService = new PlaylistCacheSyncService(playlistClient, dbContext);
var summary = await syncService.SyncAsync(spotifyPlaylistIds, accessToken, cancellationToken);

Console.WriteLine(
    $"Sync complete: {summary.Created} created, {summary.Updated} updated, {summary.Unavailable} marked unavailable.");

return 0;

static string RequireEnvironmentVariable(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
