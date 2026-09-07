using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

public sealed class PlaylistEraAssignmentTests
{
    [Theory]
    [InlineData("mixed-era", "1970s", "1970s")]
    [InlineData("1970s", "1980s-1990s", "1970s")]
    [InlineData("mixed-era", "unknown-era", "mixed-era")]
    [InlineData("mixed-era", null, "mixed-era")]
    public void Apply_enriches_only_mixed_era_and_preserves_editorial_fallback(
        string currentEra,
        string? computedEra,
        string expectedEra)
    {
        var playlist = CreatePlaylist([currentEra]);
        string[] eras = computedEra is null ? [] : [computedEra];

        var result = PlaylistEraAssignment.Apply(playlist, new Dictionary<string, string[]> { ["id"] = eras });

        result.Eras.ShouldBe([expectedEra]);
        playlist.Eras.ShouldBe([currentEra]);
    }

    [Fact]
    public void Apply_preserves_explicit_multi_era_assignment()
    {
        var playlist = CreatePlaylist(["mixed-era", "1970s"]);
        var result = PlaylistEraAssignment.Apply(playlist, new Dictionary<string, string[]> { ["id"] = ["pre-1970"] });

        result.ShouldBeSameAs(playlist);
    }

    private static PlaylistContent CreatePlaylist(string[] eras) => new(
        "slug", "id", "Title", "Summary", [], [], [], eras, "Note", true, false, 0, null, []);
}
