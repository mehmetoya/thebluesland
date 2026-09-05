namespace TheBluesland.SpotifyFetcher.CuratorNote;

/// <summary>Abstraction over the Anthropic Messages API so <see cref="CuratorNoteSuggestionService"/> is testable without a live API key or network call.</summary>
public interface IAnthropicClient
{
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken);
}
