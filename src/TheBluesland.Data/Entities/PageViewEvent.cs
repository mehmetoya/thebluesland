namespace TheBluesland.Data.Entities;

/// <summary>
/// One recorded page view or Spotify-click event (docs/specs/visitor-and-playlist-click-analytics.md).
/// Written only by the production web app's <c>analytics_writer</c> role (insert-only, see
/// <c>Scripts/create-analytics-role.sql</c>) through <see cref="AnalyticsDbContext"/> - a context
/// completely separate from <see cref="SpotifyPlaylistCacheEntry"/>'s <see cref="TheBlueslandDbContext"/>,
/// so a bug in this write path can never touch <c>spotify_playlist_cache</c> (spec Design section 1).
///
/// <see cref="VisitorHash"/> is never a raw IP address - see the spec's section 3 formula
/// (<c>TheBluesland.Web.Analytics.VisitorHashing</c>). No cookie is set anywhere in this app.
/// </summary>
public sealed class PageViewEvent
{
    public long Id { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Either <c>"page_view"</c> or <c>"spotify_click"</c>.</summary>
    public required string EventType { get; init; }

    /// <summary>Request path, e.g. <c>"/"</c>, <c>"/playlists/my-slug"</c>, <c>"/out/my-slug"</c>.</summary>
    public required string Path { get; init; }

    /// <summary>Set for playlist-detail views and every <c>spotify_click</c>; null otherwise.</summary>
    public string? PlaylistSlug { get; init; }

    /// <summary>Date-scoped SHA-256 hash - never a raw IP. See <see cref="OccurredAt"/> for when.</summary>
    public required string VisitorHash { get; init; }
}
