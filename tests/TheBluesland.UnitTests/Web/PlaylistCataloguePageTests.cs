using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-019: page N shows the first N * PageSize playlists cumulatively. A pure-function suite,
/// independent of the query-string/UI layer (see <see cref="PlaylistCataloguePage"/>'s own doc
/// comment).
/// </summary>
public sealed class PlaylistCataloguePageTests
{
    private static readonly IReadOnlyList<PlaylistContent> ThirtyPlaylists =
        Enumerable.Range(0, 30).Select(index => Playlist($"playlist-{index:D2}")).ToList();

    [Fact]
    public void Take_with_no_page_query_returns_only_the_first_PageSize_playlists()
    {
        var result = PlaylistCataloguePage.Take(ThirtyPlaylists, pageQuery: null);

        result.Count.ShouldBe(PlaylistCataloguePage.PageSize);
        result.ShouldBe(ThirtyPlaylists.Take(PlaylistCataloguePage.PageSize));
    }

    [Fact]
    public void Take_with_page_2_returns_the_first_two_pages_worth_cumulatively()
    {
        var result = PlaylistCataloguePage.Take(ThirtyPlaylists, pageQuery: 2);

        result.Count.ShouldBe(Math.Min(ThirtyPlaylists.Count, PlaylistCataloguePage.PageSize * 2));
        result.ShouldBe(ThirtyPlaylists.Take(PlaylistCataloguePage.PageSize * 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Take_with_a_non_positive_page_query_falls_back_to_page_1(int invalidPage)
    {
        var result = PlaylistCataloguePage.Take(ThirtyPlaylists, invalidPage);

        result.Count.ShouldBe(PlaylistCataloguePage.PageSize);
    }

    [Fact]
    public void Take_never_returns_more_than_the_total_playlist_count()
    {
        var result = PlaylistCataloguePage.Take(ThirtyPlaylists, pageQuery: 99);

        result.Count.ShouldBe(ThirtyPlaylists.Count);
    }

    [Fact]
    public void HasMore_is_true_when_more_playlists_remain_beyond_the_current_page()
    {
        PlaylistCataloguePage.HasMore(ThirtyPlaylists, pageQuery: null).ShouldBeTrue();
    }

    [Fact]
    public void HasMore_is_false_once_every_playlist_is_visible()
    {
        PlaylistCataloguePage.HasMore(ThirtyPlaylists, pageQuery: 2).ShouldBeFalse();
    }

    [Fact]
    public void NextPage_increments_from_the_current_page()
    {
        PlaylistCataloguePage.NextPage(pageQuery: null).ShouldBe(2);
        PlaylistCataloguePage.NextPage(pageQuery: 2).ShouldBe(3);
    }

    private static PlaylistContent Playlist(string slug) =>
        new(
            Slug: slug,
            SpotifyPlaylistId: "0iJt9LMebhOY0KSHSJw3cS",
            Title: slug,
            Summary: "Fixture summary text used only for pagination tests.",
            Moods: ["warm"],
            Genres: ["rock"],
            Occasions: ["road-trip"],
            Era: "mixed-era",
            CuratorNote: "Fixture curator note.",
            IsPublished: true,
            Featured: false,
            DisplayOrder: 0,
            PublishedAt: new DateOnly(2026, 1, 1),
            PreviousSlugs: []);
}
