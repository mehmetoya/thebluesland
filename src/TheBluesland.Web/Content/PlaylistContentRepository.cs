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

    /// <summary>
    /// US-010 AC5/FR-020: resolves an old slug (one a playlist's <c>previousSlugs</c> front-matter
    /// array still lists) to the playlist now owning it, so the detail page can issue a permanent
    /// redirect to its current slug. Only consulted once <see cref="FindBySlugAsync"/> has already
    /// failed to find a current-slug match - a slug's own current owner always wins over any other
    /// playlist that happens to list it as a former slug.
    /// </summary>
    public async Task<PlaylistContent?> FindByPreviousSlugAsync(string slug, CancellationToken cancellationToken)
    {
        var playlists = await LoadAllAsync(cancellationToken);
        return playlists.FirstOrDefault(playlist => playlist.PreviousSlugs.Contains(slug, StringComparer.Ordinal));
    }

    /// <summary>
    /// Playlists the home page catalogue (US-008) may show; drafts are never listed. Sorted per
    /// US-009 AC1/FR-001 (<see cref="PlaylistCatalogueSort"/>) - the home page renders this order
    /// directly, before any filter is applied.
    /// </summary>
    public async Task<IReadOnlyList<PlaylistContent>> FindAllPublishedAsync(CancellationToken cancellationToken)
    {
        var playlists = await LoadAllAsync(cancellationToken);
        var published = playlists.Where(playlist => playlist.IsPublished).ToList();
        return PlaylistCatalogueSort.Apply(published);
    }
}
