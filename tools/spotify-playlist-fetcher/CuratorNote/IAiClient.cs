namespace TheBluesland.SpotifyFetcher.CuratorNote;

/// <summary>Abstraction over the AI provider's text-generation call so <see cref="CuratorNoteSuggestionService"/> is testable without a live API key or network call.</summary>
public interface IAiClient
{
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken);
}
