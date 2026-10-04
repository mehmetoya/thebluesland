using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

public sealed class PlaylistMixRankingTests
{
    [Fact]
    public void Apply_prioritizes_the_energetic_mood_for_more_energetic_mode()
    {
        var mellow = Playlist("mellow", ["warm"], ["soul"], ["slow-evening"], []);
        var energetic = Playlist("energetic", ["energetic"], ["rock"], ["night-drive"], []);

        var result = PlaylistMixRanking.Apply([mellow, energetic], Playlist("current", [], [], [], []), PlaylistMixMode.MoreEnergetic);

        result.ShouldBe([energetic, mellow]);
    }

    [Fact]
    public void Apply_prioritizes_relaxed_moods_and_occasions_for_relaxed_flow_mode()
    {
        var energetic = Playlist("energetic", ["energetic"], ["rock"], ["dancing"], []);
        var relaxed = Playlist("relaxed", ["warm", "melancholic"], ["soul"], ["late-night", "slow-evening"], []);

        var result = PlaylistMixRanking.Apply([energetic, relaxed], Playlist("current", [], [], [], []), PlaylistMixMode.RelaxedFlow);

        result.ShouldBe([relaxed, energetic]);
    }

    [Fact]
    public void Apply_prioritizes_deep_listen_occasion_for_deeper_cuts_mode()
    {
        var broad = Playlist("broad", ["warm"], ["rock"], ["headphones"], []);
        var deep = Playlist("deep", ["raw"], ["jazz"], ["deep-listen"], []);

        var result = PlaylistMixRanking.Apply([broad, deep], Playlist("current", [], [], [], []), PlaylistMixMode.DeeperCuts);

        result.ShouldBe([deep, broad]);
    }

    private static PlaylistContent Playlist(
        string slug,
        IReadOnlyList<string> moods,
        IReadOnlyList<string> genres,
        IReadOnlyList<string> occasions,
        IReadOnlyList<string> eras) =>
        new(
            Slug: slug,
            SpotifyPlaylistId: "0iJt9LMebhOY0KSHSJw3cS",
            Title: slug,
            Summary: "Fixture summary.",
            Moods: moods,
            Genres: genres,
            Occasions: occasions,
            Eras: eras,
            CuratorNote: "Fixture note.",
            IsPublished: true,
            FeaturedOrder: null,
            DisplayOrder: 0,
            PublishedAt: new DateOnly(2026, 1, 1),
            PreviousSlugs: []);
}
