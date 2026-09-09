using System.Collections.Frozen;
using TheBluesland.Web.Cache;

namespace TheBluesland.Web.Content;

/// <summary>
/// One catalogue snapshot per application instance. Editorial files are immutable within a
/// deployment; restart the host after editing them. Canceling one request never cancels the
/// shared load for other visitors. A failed load remains failed until the next deployment.
/// </summary>
public sealed class PlaylistContentRepository
{
    public const string ContentDirectoryConfigKey = "PlaylistContent:Directory";

    private readonly PlaylistCacheSignalsCache? _cacheSignals;
    private readonly string _contentDirectory;
    private readonly PlaylistContentReader _reader;
    private readonly Lazy<Task<Catalogue>> _catalogue;
    private readonly Lazy<Task<PlaylistContentValidationResult>> _validation;

    public PlaylistContentRepository(
        IConfiguration configuration,
        PlaylistContentReader reader,
        PlaylistCacheSignalsCache? cacheSignals = null)
    {
        _contentDirectory = configuration[ContentDirectoryConfigKey]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "content", "playlists");
        _reader = reader;
        _cacheSignals = cacheSignals;
        _catalogue = new Lazy<Task<Catalogue>>(LoadCatalogueAsync);
        _validation = new Lazy<Task<PlaylistContentValidationResult>>(() =>
            new PlaylistContentValidator().ValidateAllAsync(_contentDirectory, CancellationToken.None));
    }

    public async Task<IReadOnlyList<PlaylistContent>> LoadAllAsync(CancellationToken cancellationToken) =>
        await ApplyErasAsync((await _catalogue.Value.WaitAsync(cancellationToken)).All, cancellationToken);

    public async Task<PlaylistContent?> FindBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var playlist = (await _catalogue.Value.WaitAsync(cancellationToken)).PublishedBySlug.GetValueOrDefault(slug);
        return await ApplyErasAsync(playlist, cancellationToken);
    }

    public async Task<PlaylistContent?> FindByPreviousSlugAsync(string slug, CancellationToken cancellationToken)
    {
        var catalogue = await _catalogue.Value.WaitAsync(cancellationToken);
        // A current draft slug must not redirect through another playlist's historical alias.
        var playlist = catalogue.CurrentSlugs.Contains(slug)
            ? null
            : catalogue.PublishedByPreviousSlug.GetValueOrDefault(slug);
        return await ApplyErasAsync(playlist, cancellationToken);
    }

    /// <summary>
    /// Featured-then-follower-count order (<see cref="PlaylistCatalogueSort"/>), computed fresh on
    /// every call from the same 5-minute cache signals read that also overlays computed eras
    /// (<see cref="PlaylistEraAssignment"/>) - one signal fetch feeds both, since neither can be
    /// baked into <see cref="LoadCatalogueAsync"/>'s once-per-process snapshot without going stale
    /// for the life of the process (docs/specs/catalogue-priority-and-follower-sort.md).
    /// </summary>
    public async Task<IReadOnlyList<PlaylistContent>> FindAllPublishedAsync(CancellationToken cancellationToken)
    {
        var published = (await _catalogue.Value.WaitAsync(cancellationToken)).Published;
        if (_cacheSignals is null)
        {
            return published;
        }

        var signals = await _cacheSignals.GetAsync(cancellationToken);
        var withEras = published.Select(playlist => PlaylistEraAssignment.Apply(playlist, signals)).ToArray();
        return PlaylistCatalogueSort.Apply(withEras, signals);
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        var catalogue = await _catalogue.Value.WaitAsync(cancellationToken);
        var validation = await _validation.Value.WaitAsync(cancellationToken);
        return catalogue.Published.Count > 0 && validation.IsValid;
    }

    private async Task<PlaylistContent?> ApplyErasAsync(PlaylistContent? playlist, CancellationToken cancellationToken)
    {
        if (_cacheSignals is null || playlist is null)
        {
            return playlist;
        }

        return PlaylistEraAssignment.Apply(playlist, await _cacheSignals.GetAsync(cancellationToken));
    }

    private async Task<IReadOnlyList<PlaylistContent>> ApplyErasAsync(
        IReadOnlyList<PlaylistContent> playlists,
        CancellationToken cancellationToken)
    {
        if (_cacheSignals is null)
        {
            return playlists;
        }

        var signals = await _cacheSignals.GetAsync(cancellationToken);
        return playlists.Select(playlist => PlaylistEraAssignment.Apply(playlist, signals)).ToArray();
    }

    private async Task<Catalogue> LoadCatalogueAsync()
    {
        if (!Directory.Exists(_contentDirectory))
        {
            throw new DirectoryNotFoundException("Playlist content directory is missing.");
        }

        var all = await _reader.ReadAllAsync(_contentDirectory, CancellationToken.None);
        // Not sorted here: featured/follower-count order depends on spotify_playlist_cache data
        // that can change more often than this once-per-process catalogue load - see
        // FindAllPublishedAsync, which applies PlaylistCatalogueSort fresh on every call instead.
        var published = all.Where(playlist => playlist.IsPublished).ToList();
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
