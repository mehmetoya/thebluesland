using Microsoft.EntityFrameworkCore;
using TheBluesland.Data;

namespace TheBluesland.SpotifyFetcher.CuratorNote;

/// <summary>
/// US-016/ADR-0005: reads exactly one <c>spotify_playlist_cache</c> row via the read-only
/// connection, then asks the AI provider for a draft curator note built only from that row's four
/// permitted fields (<see cref="CuratorNotePromptBuilder"/>). Fails fast on a missing or
/// unavailable row - <see cref="IAiClient"/> is never called in either case, so no
/// empty/incomplete data ever reaches the AI (US-016 AC3).
/// </summary>
public sealed class CuratorNoteSuggestionService
{
    private readonly TheBlueslandDbContext _dbContext;
    private readonly IAiClient _aiClient;

    public CuratorNoteSuggestionService(TheBlueslandDbContext dbContext, IAiClient aiClient)
    {
        _dbContext = dbContext;
        _aiClient = aiClient;
    }

    public async Task<string> SuggestAsync(string spotifyPlaylistId, CancellationToken cancellationToken)
    {
        var entry = await _dbContext.SpotifyPlaylistCache
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.SpotifyPlaylistId == spotifyPlaylistId, cancellationToken);

        if (entry is null)
        {
            throw new InvalidOperationException(
                $"No spotify_playlist_cache row found for '{spotifyPlaylistId}'. Run the monthly " +
                "sync workflow (or trigger it manually) for this playlist first.");
        }

        if (!entry.IsAvailable)
        {
            throw new InvalidOperationException(
                $"'{spotifyPlaylistId}' is marked unavailable by the last sync (Spotify could not " +
                "find it) - no curator-note suggestion can be generated from stale or missing data.");
        }

        var prompt = CuratorNotePromptBuilder.Build(entry);
        return await _aiClient.GenerateAsync(prompt, cancellationToken);
    }
}
