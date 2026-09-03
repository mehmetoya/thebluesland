using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-010 AC4 / spec FR-023: up to three published playlists, ranked by shared-tag overlap
/// (moods+genres+occasions+era, see <see cref="RelatedPlaylistRanking"/>'s own doc comment for why
/// era counts), excluding the current playlist and any candidate with zero overlap, with a
/// deterministic displayOrder/publishedAt tie-break. A pure-function suite, independent of the
/// content-reading/repository layer (same shape as <see cref="PlaylistFilterTests"/>).
/// </summary>
public sealed class RelatedPlaylistRankingTests
{
    private static readonly PlaylistContent Current = Playlist(
        slug: "current-playlist",
        moods: ["warm"],
        genres: ["blues"],
        occasions: ["late-night"],
        era: "1970s");

    [Fact]
    public void Apply_ranks_candidates_by_descending_shared_tag_count()
    {
        var strongMatch = Playlist("strong-match", moods: ["warm"], genres: ["blues"], occasions: ["headphones"], era: "1970s");
        var weakMatch = Playlist("weak-match", moods: ["warm"], genres: ["soul"], occasions: ["headphones"], era: "mixed-era");

        var result = RelatedPlaylistRanking.Apply([strongMatch, weakMatch], Current);

        result.ShouldBe([strongMatch, weakMatch]);
    }

    [Fact]
    public void Apply_never_includes_the_current_playlist_itself()
    {
        var result = RelatedPlaylistRanking.Apply([Current], Current);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Apply_excludes_candidates_that_share_zero_tags()
    {
        var noOverlap = Playlist("no-overlap", moods: ["melancholic"], genres: ["jazz"], occasions: ["slow-evening"], era: "mixed-era");

        var result = RelatedPlaylistRanking.Apply([noOverlap], Current);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Apply_caps_the_result_at_three_even_when_more_candidates_overlap()
    {
        var candidates = Enumerable.Range(0, 5)
            .Select(index => Playlist($"candidate-{index}", moods: ["warm"], genres: ["blues"], occasions: ["headphones"], era: "1970s"))
            .ToList();

        var result = RelatedPlaylistRanking.Apply(candidates, Current);

        result.Count.ShouldBe(3);
    }

    /// <summary>
    /// Two candidates tied on shared-tag count break the tie the same way the home page catalogue
    /// orders playlists (displayOrder ascending, then publishedAt descending).
    /// </summary>
    [Fact]
    public void Apply_breaks_a_shared_tag_count_tie_with_displayOrder_then_publishedAt_descending()
    {
        var earlierDisplayOrder = Playlist(
            "earlier-display-order", moods: ["warm"], genres: [], occasions: [], era: "mixed-era",
            displayOrder: 0, publishedAt: new DateOnly(2026, 1, 1));
        var laterDisplayOrder = Playlist(
            "later-display-order", moods: ["warm"], genres: [], occasions: [], era: "mixed-era",
            displayOrder: 1, publishedAt: new DateOnly(2026, 6, 1));

        var result = RelatedPlaylistRanking.Apply([laterDisplayOrder, earlierDisplayOrder], Current);

        result.ShouldBe([earlierDisplayOrder, laterDisplayOrder]);
    }

    /// <summary>
    /// Found in review: PlaylistTags.All excludes a blank era, so two playlists that both simply
    /// have no era (Era defaults to "" when front matter omits it) must not appear to "share" it.
    /// </summary>
    [Fact]
    public void Apply_does_not_treat_two_playlists_with_no_era_as_sharing_a_tag()
    {
        var noEraNoOtherOverlap = Playlist(
            "no-era-no-overlap", moods: ["melancholic"], genres: ["jazz"], occasions: ["slow-evening"], era: "");
        var currentWithNoEra = Playlist(
            "current-no-era", moods: ["warm"], genres: ["blues"], occasions: ["late-night"], era: "");

        var result = RelatedPlaylistRanking.Apply([noEraNoOtherOverlap], currentWithNoEra);

        result.ShouldBeEmpty();
    }

    private static PlaylistContent Playlist(
        string slug,
        IReadOnlyList<string> moods,
        IReadOnlyList<string> genres,
        IReadOnlyList<string> occasions,
        string era,
        int displayOrder = 0,
        DateOnly? publishedAt = null) =>
        new(
            Slug: slug,
            SpotifyPlaylistId: "0iJt9LMebhOY0KSHSJw3cS",
            Title: slug,
            Summary: "Fixture summary text used only for related-playlist ranking tests.",
            Moods: moods,
            Genres: genres,
            Occasions: occasions,
            Era: era,
            CuratorNote: "Fixture curator note.",
            IsPublished: true,
            Featured: false,
            DisplayOrder: displayOrder,
            PublishedAt: publishedAt ?? new DateOnly(2026, 1, 1),
            PreviousSlugs: []);
}
