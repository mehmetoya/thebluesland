using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

public sealed class PlaylistSearchTests
{
    private static readonly PlaylistContent PlaylistWithSearchableFields = new(
        Slug: "istanbul-after-dark",
        SpotifyPlaylistId: "0iJt9LMebhOY0KSHSJw3cS",
        Title: "Istanbul After Dark",
        Summary: "Deep sounds for a quiet night.",
        Moods: ["melancholic"],
        Genres: ["anadolu-rock"],
        Occasions: ["night-drive"],
        Eras: ["1970s"],
        CuratorNote: "Not part of visitor search.",
        IsPublished: true,
        FeaturedOrder: null,
        DisplayOrder: 0,
        PublishedAt: new DateOnly(2026, 1, 1),
        PreviousSlugs: []);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_with_an_empty_query_returns_the_original_catalogue_order(string query)
    {
        var playlists = new[]
        {
            PlaylistWithSearchableFields with { Slug = "second", DisplayOrder = 2 },
            PlaylistWithSearchableFields with { Slug = "first", DisplayOrder = 1 },
        };

        var result = PlaylistSearch.Apply(playlists, query);

        result.ShouldBe(playlists);
    }

    [Theory]
    [InlineData("istanbul")]
    [InlineData("deep sounds")]
    [InlineData("melancholic")]
    [InlineData("anadolu rock")]
    [InlineData("night drive")]
    [InlineData("1970s")]
    public void Apply_matches_title_summary_and_each_taxonomy_dimension(string query)
    {
        var result = PlaylistSearch.Apply([PlaylistWithSearchableFields], query);

        result.ShouldBe([PlaylistWithSearchableFields]);
    }

    [Fact]
    public void Apply_matches_without_case_or_punctuation_sensitivity()
    {
        var result = PlaylistSearch.Apply(
            [PlaylistWithSearchableFields],
            "MELANCHOLIC, ANADOLU-ROCK");

        result.ShouldBe([PlaylistWithSearchableFields]);
    }

    [Fact]
    public void Apply_ranks_partial_matches_by_distinct_query_terms_and_keeps_stable_ties()
    {
        var fullMatch = PlaylistWithSearchableFields with
        {
            Slug = "full-match",
            Title = "Melancholic Anadolu Rock",
            Summary = string.Empty,
            Moods = [],
            Genres = [],
            Occasions = [],
            Eras = [],
        };
        var partialMatch = PlaylistWithSearchableFields with
        {
            Slug = "partial-match",
            Title = "Rock collection",
            Summary = string.Empty,
            Moods = [],
            Genres = [],
            Occasions = [],
            Eras = [],
        };
        var tiedPartialMatch = partialMatch with { Slug = "tied-partial-match" };

        var result = PlaylistSearch.Apply(
            [partialMatch, fullMatch, tiedPartialMatch],
            "melancholic anadolu rock rock");

        result.ShouldBe([fullMatch, partialMatch, tiedPartialMatch]);
    }

    [Fact]
    public void Apply_ignores_curator_notes_and_returns_no_result_when_no_search_term_matches()
    {
        var result = PlaylistSearch.Apply([PlaylistWithSearchableFields], "private curator");

        result.ShouldBeEmpty();
    }
}
