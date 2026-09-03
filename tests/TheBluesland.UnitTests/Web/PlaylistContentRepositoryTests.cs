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
    private readonly PlaylistContentRepository _repository = CreateRepository();

    [Fact]
    public async Task FindAllPublishedAsync_excludes_draft_playlists()
    {
        var published = await _repository.FindAllPublishedAsync(CancellationToken.None);

        published.ShouldContain(playlist => playlist.Slug == "masterpieces-of-erkin-the-father");
        published.ShouldAllBe(playlist => playlist.IsPublished);
        published.ShouldNotContain(playlist => playlist.Slug == "dear-mr-fantasy");
    }

    private static PlaylistContentRepository CreateRepository()
    {
        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists");
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
