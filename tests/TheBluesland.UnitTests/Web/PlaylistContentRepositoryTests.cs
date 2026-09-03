using Microsoft.Extensions.Configuration;
using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-008: the home page catalogue must list only published playlists. Reuses the shared
/// Fixtures/content-playlists directory (one published, two draft entries).
/// </summary>
public sealed class PlaylistContentRepositoryTests
{
    private readonly PlaylistContentRepository _repository = CreateRepository("content-playlists");

    [Fact]
    public async Task FindAllPublishedAsync_excludes_draft_playlists()
    {
        var published = await _repository.FindAllPublishedAsync(CancellationToken.None);

        published.ShouldContain(playlist => playlist.Slug == "masterpieces-of-erkin-the-father");
        published.ShouldAllBe(playlist => playlist.IsPublished);
        published.ShouldNotContain(playlist => playlist.Slug == "dear-mr-fantasy");
    }

    /// <summary>US-010 AC5/FR-020: an old slug listed in previousSlugs resolves to its playlist.</summary>
    [Fact]
    public async Task FindByPreviousSlugAsync_resolves_an_old_slug_to_the_playlist_that_now_owns_it()
    {
        var repository = CreateRepository("content-playlists-detail");

        var playlist = await repository.FindByPreviousSlugAsync("legacy-primary-slug", CancellationToken.None);

        playlist.ShouldNotBeNull();
        playlist.Slug.ShouldBe("primary-playlist");
    }

    [Fact]
    public async Task FindByPreviousSlugAsync_returns_null_for_a_slug_no_playlist_ever_used()
    {
        var repository = CreateRepository("content-playlists-detail");

        var playlist = await repository.FindByPreviousSlugAsync("never-existed-slug", CancellationToken.None);

        playlist.ShouldBeNull();
    }

    private static PlaylistContentRepository CreateRepository(string fixtureDirectoryName)
    {
        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureDirectoryName);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>(
                    PlaylistContentRepository.ContentDirectoryConfigKey,
                    contentDirectory),
            ])
            .Build();

        return new PlaylistContentRepository(configuration, new PlaylistContentReader());
    }
}
