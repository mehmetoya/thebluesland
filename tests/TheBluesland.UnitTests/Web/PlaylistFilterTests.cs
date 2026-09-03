using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-009 AC2 / spec FR-011: within one dimension, selected values combine with OR; across
/// dimensions, active selections combine with AND. A pure-function suite, independent of the
/// query-string/UI layer (see <see cref="PlaylistFilter"/>'s own doc comment).
/// </summary>
public sealed class PlaylistFilterTests
{
    private static readonly PlaylistContent MelancholicLateNight = Playlist(
        slug: "melancholic-late-night",
        moods: ["melancholic"],
        genres: ["blues"],
        occasions: ["late-night"],
        era: "pre-1970");

    private static readonly PlaylistContent WarmRoadTrip = Playlist(
        slug: "warm-road-trip",
        moods: ["warm"],
        genres: ["rock"],
        occasions: ["road-trip"],
        era: "1970s");

    private static readonly PlaylistContent EnergeticWarmHeadphones = Playlist(
        slug: "energetic-warm-headphones",
        moods: ["energetic", "warm"],
        genres: ["soul"],
        occasions: ["headphones"],
        era: "2000s-present");

    private static readonly IReadOnlyList<PlaylistContent> AllPlaylists =
        [MelancholicLateNight, WarmRoadTrip, EnergeticWarmHeadphones];

    [Fact]
    public void Apply_with_no_active_filters_returns_every_playlist()
    {
        var result = PlaylistFilter.Apply(AllPlaylists, PlaylistFilterCriteria.None);

        result.ShouldBe(AllPlaylists);
    }

    [Fact]
    public void Apply_combines_multiple_selected_values_in_the_same_dimension_with_OR()
    {
        var criteria = new PlaylistFilterCriteria(Moods: ["warm", "melancholic"], Genres: [], Occasions: [], Eras: []);

        var result = PlaylistFilter.Apply(AllPlaylists, criteria);

        result.ShouldBe([MelancholicLateNight, WarmRoadTrip, EnergeticWarmHeadphones], ignoreOrder: true);
    }

    [Fact]
    public void Apply_combines_different_dimensions_with_AND()
    {
        // "warm" (mood) matches WarmRoadTrip and EnergeticWarmHeadphones, but only WarmRoadTrip
        // also has the road-trip occasion - AND across dimensions must exclude the other.
        var criteria = new PlaylistFilterCriteria(Moods: ["warm"], Genres: [], Occasions: ["road-trip"], Eras: []);

        var result = PlaylistFilter.Apply(AllPlaylists, criteria);

        result.ShouldBe([WarmRoadTrip]);
    }

    [Fact]
    public void Apply_returns_empty_when_no_playlist_satisfies_every_active_dimension()
    {
        var criteria = new PlaylistFilterCriteria(Moods: ["warm"], Genres: [], Occasions: ["late-night"], Eras: []);

        var result = PlaylistFilter.Apply(AllPlaylists, criteria);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Apply_filters_by_era()
    {
        var criteria = new PlaylistFilterCriteria(Moods: [], Genres: [], Occasions: [], Eras: ["pre-1970"]);

        var result = PlaylistFilter.Apply(AllPlaylists, criteria);

        result.ShouldBe([MelancholicLateNight]);
    }

    private static PlaylistContent Playlist(
        string slug,
        IReadOnlyList<string> moods,
        IReadOnlyList<string> genres,
        IReadOnlyList<string> occasions,
        string era) =>
        new(
            Slug: slug,
            SpotifyPlaylistId: "0iJt9LMebhOY0KSHSJw3cS",
            Title: slug,
            Summary: "Fixture summary text used only for filter matching tests.",
            Moods: moods,
            Genres: genres,
            Occasions: occasions,
            Era: era,
            CuratorNote: "Fixture curator note.",
            IsPublished: true,
            Featured: false,
            DisplayOrder: 0,
            PublishedAt: new DateOnly(2026, 1, 1));
}
