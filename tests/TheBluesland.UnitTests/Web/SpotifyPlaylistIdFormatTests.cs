using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-012 AC2/spec 12.4(a)/SEC-004: the shared "22-character base62" guard reused by both
/// <see cref="PlaylistContentValidator"/> (build-time) and PlaylistDetailView (render-time).
/// </summary>
public sealed class SpotifyPlaylistIdFormatTests
{
    [Theory]
    [InlineData("0iJt9LMebhOY0KSHSJw3cS")]
    [InlineData("2m8X8fsMWor8A5AnmOHwzy")]
    public void IsValid_accepts_a_22_character_base62_id(string spotifyPlaylistId)
    {
        SpotifyPlaylistIdFormat.IsValid(spotifyPlaylistId).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tooShort")]
    [InlineData("0iJt9LMebhOY0KSHSJw3cSExtraCharsMakeThisTooLong")]
    [InlineData("0iJt9LMebhOY0KSHSJw3c/")]
    [InlineData("javascript:alert(1)//")]
    public void IsValid_rejects_anything_that_is_not_exactly_22_base62_characters(string? spotifyPlaylistId)
    {
        SpotifyPlaylistIdFormat.IsValid(spotifyPlaylistId).ShouldBeFalse();
    }
}
