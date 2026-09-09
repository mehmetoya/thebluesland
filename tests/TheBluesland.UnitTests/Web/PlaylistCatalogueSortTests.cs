using Shouldly;
using TheBluesland.Web.Cache;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// Rewritten 2026-09-10 (docs/specs/catalogue-priority-and-follower-sort.md): the home page now
/// orders by an explicit, hand-picked <see cref="PlaylistContent.FeaturedOrder"/> first, then by
/// Spotify follower count descending - see <see cref="PlaylistCatalogueSort"/>'s own doc comment
/// for why the previous displayOrder/publishedAt rule was replaced (it differentiated almost
/// nothing in the real catalogue).
/// </summary>
public sealed class PlaylistCatalogueSortTests
{
    [Fact]
    public void Apply_orders_featured_playlists_first_by_featuredOrder_ascending()
    {
        var second = Playlist("second", featuredOrder: 2);
        var first = Playlist("first", featuredOrder: 1);
        var unfeatured = Playlist("unfeatured", featuredOrder: null);

        var result = PlaylistCatalogueSort.Apply([unfeatured, second, first], NoSignals);

        result.Select(p => p.Slug).ShouldBe(["first", "second", "unfeatured"]);
    }

    [Fact]
    public void Apply_orders_unfeatured_playlists_by_follower_count_descending()
    {
        var fewer = Playlist("fewer", featuredOrder: null);
        var more = Playlist("more", featuredOrder: null);
        var signals = new Dictionary<string, PlaylistCacheSignals>
        {
            [fewer.SpotifyPlaylistId] = new(ComputedEras: null, FollowerCount: 10),
            [more.SpotifyPlaylistId] = new(ComputedEras: null, FollowerCount: 500),
        };

        var result = PlaylistCatalogueSort.Apply([fewer, more], signals);

        result.Select(p => p.Slug).ShouldBe(["more", "fewer"]);
    }

    [Fact]
    public void Apply_places_a_playlist_with_no_follower_data_after_every_playlist_that_has_one()
    {
        // Zero is still real, known data - it must still outrank "we don't know" (no cache row,
        // never synced, or the database was unreachable), not be treated identically to it.
        var zeroFollowers = Playlist("zero-followers", featuredOrder: null);
        var unknown = Playlist("unknown", featuredOrder: null);
        var signals = new Dictionary<string, PlaylistCacheSignals>
        {
            [zeroFollowers.SpotifyPlaylistId] = new(ComputedEras: null, FollowerCount: 0),
        };

        var result = PlaylistCatalogueSort.Apply([unknown, zeroFollowers], signals);

        result.Select(p => p.Slug).ShouldBe(["zero-followers", "unknown"]);
    }

    [Fact]
    public void Apply_puts_every_featured_playlist_before_every_unfeatured_one_regardless_of_followers()
    {
        var unfeaturedWithManyFollowers = Playlist("unfeatured-popular", featuredOrder: null);
        var featuredWithNoFollowerData = Playlist("featured-unmeasured", featuredOrder: 1);
        var signals = new Dictionary<string, PlaylistCacheSignals>
        {
            [unfeaturedWithManyFollowers.SpotifyPlaylistId] = new(ComputedEras: null, FollowerCount: 1_000_000),
        };

        var result = PlaylistCatalogueSort.Apply(
            [unfeaturedWithManyFollowers, featuredWithNoFollowerData], signals);

        result.Select(p => p.Slug).ShouldBe(["featured-unmeasured", "unfeatured-popular"]);
    }

    private static readonly IReadOnlyDictionary<string, PlaylistCacheSignals> NoSignals =
        new Dictionary<string, PlaylistCacheSignals>();

    private static PlaylistContent Playlist(string slug, int? featuredOrder) =>
        new(
            Slug: slug,
            SpotifyPlaylistId: $"spotify-id-{slug}",
            Title: slug,
            Summary: "Fixture summary text used only for sort-order tests.",
            Moods: ["warm"],
            Genres: ["blues"],
            Occasions: ["late-night"],
            Eras: ["1970s"],
            CuratorNote: "Fixture curator note.",
            IsPublished: true,
            FeaturedOrder: featuredOrder,
            DisplayOrder: 0,
            PublishedAt: null,
            PreviousSlugs: []);
}
