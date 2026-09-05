using Microsoft.EntityFrameworkCore;
using TheBluesland.Data;
using TheBluesland.SpotifyFetcher.Content;
using TheBluesland.SpotifyFetcher.CuratorNote;
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

// Read-only discovery mode: lists every playlist on the account so candidates can be picked for
// editorial curation without ever asking Mehmet to paste a playlist ID (spec 11.2 still means the
// human picks the title/tags/curator note - this only shortens "which ID is that playlist?").
// Never touches spotify_playlist_cache or content/playlists.
if (args.Length > 0 && string.Equals(args[0], "list-playlists", StringComparison.Ordinal))
{
    return await ListMyPlaylistsAsync(cancellationToken);
}

// Read-only: prints every spotify_playlist_cache row (name/description/track count/artists) so
// already-synced data can be reviewed without a direct database connection or a new credential -
// uses the same NEON_SYNC_CONNECTION_STRING this workflow already holds. Never writes anything.
if (args.Length > 0 && string.Equals(args[0], "dump-cache", StringComparison.Ordinal))
{
    return await DumpCacheAsync(cancellationToken);
}

// US-016/ADR-0005: independent, manually-triggered AI curator-note draft suggestion. Runs only
// from suggest-curator-note.yml, never from the monthly sync-spotify.yml job. Reads the cache via
// the read-only role (NEON_READONLY_CONNECTION_STRING) - never the sync tool's write-scoped
// connection string - and never touches Spotify or content/playlists.
if (args.Length > 0 && string.Equals(args[0], "suggest-curator-note", StringComparison.Ordinal))
{
    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
    {
        Console.Error.WriteLine("Usage: suggest-curator-note <spotifyPlaylistId>");
        return 1;
    }

    return await SuggestCuratorNoteAsync(args[1], cancellationToken);
}

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

static async Task<int> ListMyPlaylistsAsync(CancellationToken cancellationToken)
{
    var clientId = RequireEnvironmentVariable("SPOTIFY_CLIENT_ID");
    var refreshToken = RequireEnvironmentVariable("SPOTIFY_REFRESH_TOKEN");

    using var httpClient = new HttpClient();
    var authClient = new SpotifyAuthClient(httpClient);
    var accessToken = await authClient.GetAccessTokenAsync(clientId, refreshToken, cancellationToken);

    var myPlaylistsClient = new SpotifyMyPlaylistsClient(httpClient);
    var playlists = await myPlaylistsClient.ListAsync(accessToken, cancellationToken);

    Console.WriteLine($"Found {playlists.Count} playlist(s) on this Spotify account:");
    Console.WriteLine();
    foreach (var playlist in playlists)
    {
        Console.WriteLine(
            $"- {playlist.Id} | \"{playlist.Name}\" | {playlist.TrackCount} tracks | " +
            $"public={playlist.IsPublic} | owner={playlist.OwnerDisplayName ?? "(unknown)"}");
        if (!string.IsNullOrWhiteSpace(playlist.Description))
        {
            Console.WriteLine($"  {playlist.Description}");
        }
    }

    return 0;
}

static async Task<int> DumpCacheAsync(CancellationToken cancellationToken)
{
    var connectionString = RequireEnvironmentVariable("NEON_SYNC_CONNECTION_STRING");

    var optionsBuilder = new DbContextOptionsBuilder<TheBlueslandDbContext>().UseNpgsql(connectionString);
    await using var dbContext = new TheBlueslandDbContext(optionsBuilder.Options);

    var entries = await dbContext.SpotifyPlaylistCache
        .AsNoTracking()
        .OrderBy(e => e.Name)
        .ToListAsync(cancellationToken);

    Console.WriteLine($"{entries.Count} row(s) in spotify_playlist_cache:");
    Console.WriteLine();
    foreach (var entry in entries)
    {
        Console.WriteLine(
            $"- {entry.SpotifyPlaylistId} | \"{entry.Name}\" | {entry.TrackCount} tracks | " +
            $"available={entry.IsAvailable} | artists={string.Join(", ", entry.Artists)}");
        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            Console.WriteLine($"  {entry.Description}");
        }
    }

    return 0;
}

static async Task<int> SuggestCuratorNoteAsync(string spotifyPlaylistId, CancellationToken cancellationToken)
{
    var apiKey = RequireEnvironmentVariable("GEMINI_API_KEY");
    var connectionString = RequireEnvironmentVariable("NEON_READONLY_CONNECTION_STRING");
    // Overridable per US-016's own suggest-curator-note.yml `model` input. Gemini rather than
    // Anthropic (ADR-0005's 2026-09-05 amendment): Mehmet requires genuinely zero marginal cost,
    // and Gemini has a real free tier (no billing account needed) unlike Anthropic's usage-based
    // API. Flash is on that free tier and gives noticeably better prose than Flash-Lite for a
    // short creative draft.
    var model = Environment.GetEnvironmentVariable("GEMINI_MODEL") is { Length: > 0 } configuredModel
        ? configuredModel
        : "gemini-2.5-flash";

    var optionsBuilder = new DbContextOptionsBuilder<TheBlueslandDbContext>().UseNpgsql(connectionString);
    await using var dbContext = new TheBlueslandDbContext(optionsBuilder.Options);

    using var httpClient = new HttpClient();
    var aiClient = new GeminiClient(httpClient, apiKey, model);
    var suggestionService = new CuratorNoteSuggestionService(dbContext, aiClient);

    string suggestion;
    try
    {
        suggestion = await suggestionService.SuggestAsync(spotifyPlaylistId, cancellationToken);
    }
    catch (InvalidOperationException ex)
    {
        // A known, actionable failure (no row / unavailable) - a clear one-line message rather
        // than an unhandled-exception stack trace, matching RequireEnvironmentVariable's style.
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    // ADR-0005 madde 5: the suggestion never touches the database or content/playlists - it is
    // written only to stdout (piped to $GITHUB_STEP_SUMMARY by the workflow) and this file (picked
    // up as a build artifact by the workflow's upload-artifact step).
    Console.WriteLine($"Curator note suggestion for '{spotifyPlaylistId}':");
    Console.WriteLine();
    Console.WriteLine(suggestion);

    await File.WriteAllTextAsync("curator-note-suggestion.md", suggestion, cancellationToken);

    return 0;
}
