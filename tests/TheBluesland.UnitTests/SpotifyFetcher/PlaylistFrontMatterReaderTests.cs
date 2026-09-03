using Shouldly;
using TheBluesland.SpotifyFetcher.Content;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// US-003: the sync tool's only way of discovering "which playlists exist" is the
/// spotifyPlaylistId front-matter field in content/playlists/*.md (spec section 12.4). These
/// fixtures live in the test project (Fixtures/content-playlists), never the real (possibly
/// absent) repository content/ directory.
/// </summary>
public sealed class PlaylistFrontMatterReaderTests
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly PlaylistFrontMatterReader _reader = new();

    [Fact]
    public async Task ReadDistinctSpotifyPlaylistIdsAsync_returns_one_id_per_distinct_playlist()
    {
        var contentDirectory = Path.Combine(FixturesRoot, "content-playlists");

        var playlistIds = await _reader.ReadDistinctSpotifyPlaylistIdsAsync(contentDirectory, CancellationToken.None);

        playlistIds.ShouldBe(["0iJt9LMebhOY0KSHSJw3cS", "2m8X8fsMWor8A5AnmOHwzy"], ignoreOrder: true);
    }

    [Fact]
    public async Task ReadDistinctSpotifyPlaylistIdsAsync_deduplicates_a_playlist_id_repeated_across_files()
    {
        var contentDirectory = Path.Combine(FixturesRoot, "content-playlists");

        var playlistIds = await _reader.ReadDistinctSpotifyPlaylistIdsAsync(contentDirectory, CancellationToken.None);

        // valid-with-id.md and duplicate-of-first.md both carry 0iJt9LMebhOY0KSHSJw3cS.
        playlistIds.Count(id => id == "0iJt9LMebhOY0KSHSJw3cS").ShouldBe(1);
    }

    [Fact]
    public async Task ReadDistinctSpotifyPlaylistIdsAsync_includes_draft_status_files()
    {
        var contentDirectory = Path.Combine(FixturesRoot, "content-playlists");

        var playlistIds = await _reader.ReadDistinctSpotifyPlaylistIdsAsync(contentDirectory, CancellationToken.None);

        // draft-with-id.md has status: draft - drafts are synced too (spec section 18.4).
        playlistIds.ShouldContain("2m8X8fsMWor8A5AnmOHwzy");
    }

    [Fact]
    public async Task ReadDistinctSpotifyPlaylistIdsAsync_returns_empty_when_directory_does_not_exist()
    {
        var contentDirectory = Path.Combine(FixturesRoot, "does-not-exist");

        var playlistIds = await _reader.ReadDistinctSpotifyPlaylistIdsAsync(contentDirectory, CancellationToken.None);

        playlistIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadDistinctSpotifyPlaylistIdsAsync_returns_empty_when_directory_has_no_markdown_files()
    {
        var contentDirectory = Path.Combine(FixturesRoot, "content-playlists-no-md");

        var playlistIds = await _reader.ReadDistinctSpotifyPlaylistIdsAsync(contentDirectory, CancellationToken.None);

        playlistIds.ShouldBeEmpty();
    }
}
