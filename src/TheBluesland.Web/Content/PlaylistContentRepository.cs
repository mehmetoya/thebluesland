using System.Collections.Frozen;

namespace TheBluesland.Web.Content;

/// <summary>
/// One catalogue snapshot per application instance. Editorial files are immutable within a
/// deployment; restart the host after editing them. Canceling one request never cancels the
/// shared load for other visitors. A failed load remains failed until the next deployment.
/// </summary>
public sealed class PlaylistContentRepository
{
    public const string ContentDirectoryConfigKey = "PlaylistContent:Directory";

    private readonly string _contentDirectory;
    private readonly PlaylistContentReader _reader;
    private readonly Lazy<Task<Catalogue>> _catalogue;
    private readonly Lazy<Task<PlaylistContentValidationResult>> _validation;

    public PlaylistContentRepository(IConfiguration configuration, PlaylistContentReader reader)
    {
        _contentDirectory = configuration[ContentDirectoryConfigKey]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "content", "playlists");
        _reader = reader;
        _catalogue = new Lazy<Task<Catalogue>>(LoadCatalogueAsync);
        _validation = new Lazy<Task<PlaylistContentValidationResult>>(() =>
            new PlaylistContentValidator().ValidateAllAsync(_contentDirectory, CancellationToken.None));
    }

    public async Task<IReadOnlyList<PlaylistContent>> LoadAllAsync(CancellationToken cancellationToken) =>
        (await _catalogue.Value.WaitAsync(cancellationToken)).All;

    public async Task<PlaylistContent?> FindBySlugAsync(string slug, CancellationToken cancellationToken) =>
        (await _catalogue.Value.WaitAsync(cancellationToken)).PublishedBySlug.GetValueOrDefault(slug);

    public async Task<PlaylistContent?> FindByPreviousSlugAsync(string slug, CancellationToken cancellationToken)
    {
        var catalogue = await _catalogue.Value.WaitAsync(cancellationToken);
        // A current draft slug must not redirect through another playlist's historical alias.
        return catalogue.CurrentSlugs.Contains(slug)
            ? null
            : catalogue.PublishedByPreviousSlug.GetValueOrDefault(slug);
    }

    public async Task<IReadOnlyList<PlaylistContent>> FindAllPublishedAsync(CancellationToken cancellationToken) =>
        (await _catalogue.Value.WaitAsync(cancellationToken)).Published;

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        var catalogue = await _catalogue.Value.WaitAsync(cancellationToken);
        var validation = await _validation.Value.WaitAsync(cancellationToken);
        return catalogue.Published.Count > 0 && validation.IsValid;
    }

    private async Task<Catalogue> LoadCatalogueAsync()
    {
        if (!Directory.Exists(_contentDirectory))
        {
            throw new DirectoryNotFoundException("Playlist content directory is missing.");
        }

        var all = await _reader.ReadAllAsync(_contentDirectory, CancellationToken.None);
        var published = PlaylistCatalogueSort.Apply(all.Where(playlist => playlist.IsPublished).ToList());
        return new Catalogue(
            all,
            published,
            all.Select(playlist => playlist.Slug).ToFrozenSet(StringComparer.Ordinal),
            published.GroupBy(playlist => playlist.Slug, StringComparer.Ordinal)
                .ToFrozenDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal),
            published.SelectMany(playlist => playlist.PreviousSlugs.Select(slug => (Slug: slug, Playlist: playlist)))
                .GroupBy(entry => entry.Slug, StringComparer.Ordinal)
                .ToFrozenDictionary(group => group.Key, group => group.First().Playlist, StringComparer.Ordinal));
    }

    private sealed record Catalogue(
        IReadOnlyList<PlaylistContent> All,
        IReadOnlyList<PlaylistContent> Published,
        FrozenSet<string> CurrentSlugs,
        FrozenDictionary<string, PlaylistContent> PublishedBySlug,
        FrozenDictionary<string, PlaylistContent> PublishedByPreviousSlug);
}
