using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-009 AC1 / spec FR-001: the home page catalogue is ordered by displayOrder ascending
/// (lower = shown first - see <see cref="PlaylistCatalogueSort"/>'s own doc comment for why),
/// then by publishedAt descending as the tiebreaker.
/// </summary>
public sealed class PlaylistCatalogueSortTests
{
    [Fact]
    public void Apply_orders_by_displayOrder_ascending()
    {
        var last = Playlist("last", displayOrder: 5, publishedAt: new DateOnly(2026, 1, 1));
        var first = Playlist("first", displayOrder: 0, publishedAt: new DateOnly(2026, 1, 1));
        var middle = Playlist("middle", displayOrder: 2, publishedAt: new DateOnly(2026, 1, 1));

        var result = PlaylistCatalogueSort.Apply([last, first, middle]);

        result.Select(p => p.Slug).ShouldBe(["first", "middle", "last"]);
    }

    [Fact]
    public void Apply_breaks_a_displayOrder_tie_with_publishedAt_descending()
    {
        var older = Playlist("older", displayOrder: 0, publishedAt: new DateOnly(2026, 1, 1));
        var newer = Playlist("newer", displayOrder: 0, publishedAt: new DateOnly(2026, 3, 1));

        var result = PlaylistCatalogueSort.Apply([older, newer]);

        result.Select(p => p.Slug).ShouldBe(["newer", "older"]);
    }

    [Fact]
    public void Apply_uses_displayOrder_before_publishedAt_when_both_differ()
    {
        // A much later publishedAt must not override a lower displayOrder - displayOrder is the
        // primary key, publishedAt only a tiebreaker.
        var lowerOrderOlder = Playlist("lower-order-older", displayOrder: 0, publishedAt: new DateOnly(2020, 1, 1));
        var higherOrderNewer = Playlist("higher-order-newer", displayOrder: 1, publishedAt: new DateOnly(2026, 1, 1));

        var result = PlaylistCatalogueSort.Apply([higherOrderNewer, lowerOrderOlder]);

        result.Select(p => p.Slug).ShouldBe(["lower-order-older", "higher-order-newer"]);
    }

    private static PlaylistContent Playlist(string slug, int displayOrder, DateOnly? publishedAt) =>
        new(
            Slug: slug,
            SpotifyPlaylistId: "0iJt9LMebhOY0KSHSJw3cS",
            Title: slug,
            Summary: "Fixture summary text used only for sort-order tests.",
            Moods: ["warm"],
            Genres: ["blues"],
            Occasions: ["late-night"],
            Era: "1970s",
            CuratorNote: "Fixture curator note.",
            IsPublished: true,
            Featured: false,
            DisplayOrder: displayOrder,
            PublishedAt: publishedAt,
            PreviousSlugs: []);
}
