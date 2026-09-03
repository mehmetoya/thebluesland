using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-005: the web render surface needs a wider front-matter field set than
/// tools/spotify-playlist-fetcher's reader (title/summary/tags/curator note, not just
/// spotifyPlaylistId). Reuses the same fixtures as PlaylistFrontMatterReaderTests
/// (Fixtures/content-playlists) since they already carry the full field set.
/// </summary>
public sealed class PlaylistContentReaderTests
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly PlaylistContentReader _reader = new();

    [Fact]
    public async Task ReadAllAsync_maps_title_summary_tags_and_curator_note_body()
    {
        var contentDirectory = Path.Combine(FixturesRoot, "content-playlists");

        var playlists = await _reader.ReadAllAsync(contentDirectory, CancellationToken.None);

        var playlist = playlists.Single(p => p.Slug == "masterpieces-of-erkin-the-father");
        playlist.SpotifyPlaylistId.ShouldBe("0iJt9LMebhOY0KSHSJw3cS");
        playlist.Title.ShouldBe("Masterpieces of Erkin the Father");
        playlist.Summary.ShouldBe("Anadolu rock energy from one of Turkey's founding psychedelic voices.");
        playlist.Moods.ShouldBe(["energetic", "raw"]);
        playlist.Genres.ShouldBe(["anadolu-rock", "rock"]);
        playlist.Occasions.ShouldBe(["night-drive"]);
        playlist.CuratorNote.ShouldContain("Curator note body.");
        playlist.IsPublished.ShouldBeTrue();
    }

    [Fact]
    public async Task ReadAllAsync_maps_draft_status_as_not_published()
    {
        var contentDirectory = Path.Combine(FixturesRoot, "content-playlists");

        var playlists = await _reader.ReadAllAsync(contentDirectory, CancellationToken.None);

        var playlist = playlists.Single(p => p.Slug == "dear-mr-fantasy");
        playlist.IsPublished.ShouldBeFalse();
    }

    [Fact]
    public async Task ReadAllAsync_returns_empty_when_directory_does_not_exist()
    {
        var contentDirectory = Path.Combine(FixturesRoot, "does-not-exist");

        var playlists = await _reader.ReadAllAsync(contentDirectory, CancellationToken.None);

        playlists.ShouldBeEmpty();
    }
}
