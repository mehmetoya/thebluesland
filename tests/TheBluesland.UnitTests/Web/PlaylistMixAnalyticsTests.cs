using Shouldly;
using TheBluesland.Web.Analytics;
using Xunit;

namespace TheBluesland.UnitTests.Web;

public sealed class PlaylistMixAnalyticsTests
{
    [Fact]
    public void TryClassify_returns_preview_event_for_a_valid_mode_route()
    {
        var classified = PlaylistMixAnalytics.TryClassify(
            "/playlists/my-playlist",
            "more-energetic",
            null,
            null,
            null,
            out var eventType);

        classified.ShouldBeTrue();
        eventType.ShouldBe("playlist_mix_preview_more_energetic");
    }

    [Fact]
    public void TryClassify_returns_applied_event_for_a_valid_selection_route()
    {
        var classified = PlaylistMixAnalytics.TryClassify(
            "/playlists/my-playlist",
            "same-vibe",
            "related-playlist",
            null,
            null,
            out var eventType);

        classified.ShouldBeTrue();
        eventType.ShouldBe("playlist_mix_applied_same_vibe");
    }

    [Fact]
    public void TryClassify_returns_dismissed_event_for_a_hidden_preview_route()
    {
        var classified = PlaylistMixAnalytics.TryClassify(
            "/playlists/my-playlist",
            "relaxed-flow",
            null,
            "1",
            null,
            out var eventType);

        classified.ShouldBeTrue();
        eventType.ShouldBe("playlist_mix_dismissed_relaxed_flow");
    }

    [Fact]
    public void TryClassify_returns_preview_event_when_an_applied_mix_is_being_previewed_again()
    {
        var classified = PlaylistMixAnalytics.TryClassify(
            "/playlists/my-playlist",
            "deeper-cuts",
            "related-playlist",
            null,
            "1",
            out var eventType);

        classified.ShouldBeTrue();
        eventType.ShouldBe("playlist_mix_preview_deeper_cuts");
    }

    [Theory]
    [InlineData("/about", "same-vibe", null, null, null)]
    [InlineData("/playlists/my-playlist", "unknown", null, null, null)]
    [InlineData("/playlists/my-playlist", "same-vibe", "invalid/slug", null, null)]
    [InlineData("/playlists/my-playlist", "same-vibe", "my-playlist", null, null)]
    [InlineData("/playlists/my-playlist", "same-vibe", "related-playlist", "1", "1")]
    [InlineData("/playlists/my-playlist", "same-vibe", null, null, "0")]
    public void TryClassify_ignores_invalid_or_conflicting_mix_state(
        string path,
        string mode,
        string? selection,
        string? hidden,
        string? preview)
    {
        var classified = PlaylistMixAnalytics.TryClassify(path, mode, selection, hidden, preview, out var eventType);

        classified.ShouldBeFalse();
        eventType.ShouldBeEmpty();
    }
}
