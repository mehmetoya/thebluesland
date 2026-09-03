namespace TheBluesland.Web.Content;

/// <summary>
/// Read-only access to editorial playlist content for the render surface and for
/// <see cref="TheBluesland.Web.HealthChecks.PlaylistContentHealthCheck"/>. Content is small
/// (a handful of Markdown files) and re-read on demand rather than cached, matching
/// <c>tools/spotify-playlist-fetcher</c>'s own approach.
/// </summary>
public sealed class PlaylistContentRepository
{
    public const string ContentDirectoryConfigKey = "PlaylistContent:Directory";

    private readonly string _contentDirectory;
    private readonly PlaylistContentReader _reader;

    public PlaylistContentRepository(IConfiguration configuration, PlaylistContentReader reader)
    {
        _contentDirectory = configuration[ContentDirectoryConfigKey]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "content", "playlists");
        _reader = reader;
    }

    public Task<IReadOnlyList<PlaylistContent>> LoadAllAsync(CancellationToken cancellationToken) =>
        _reader.ReadAllAsync(_contentDirectory, cancellationToken);

    public async Task<PlaylistContent?> FindBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var playlists = await LoadAllAsync(cancellationToken);
        return playlists.FirstOrDefault(playlist => string.Equals(playlist.Slug, slug, StringComparison.Ordinal));
    }
}
