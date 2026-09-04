using System.Text.RegularExpressions;

namespace TheBluesland.Web.Content;

/// <summary>
/// The single source of truth for the "22-character base62 spotifyPlaylistId" format (spec 9.1,
/// US-006's <see cref="PlaylistContentValidator"/>). US-012 AC2/SEC-004 additionally reuse this at
/// render time (see <c>PlaylistDetailView.razor</c>'s embed/"Open in Spotify" URL construction) so a
/// malformed <c>spotifyPlaylistId</c> can never be turned into a Spotify URL even if a content file
/// slipped past CI validation - defense in depth, not a replacement for
/// <see cref="PlaylistContentValidator"/>'s own build-time check (spec 12.4(a): "must not accept
/// arbitrary iframe URLs from content files").
/// </summary>
public static class SpotifyPlaylistIdFormat
{
    public const int Length = 22;

    private static readonly Regex Pattern = new($"^[A-Za-z0-9]{{{Length}}}$", RegexOptions.Compiled);

    public static bool IsValid(string? spotifyPlaylistId) =>
        spotifyPlaylistId is { Length: Length } && Pattern.IsMatch(spotifyPlaylistId);
}
