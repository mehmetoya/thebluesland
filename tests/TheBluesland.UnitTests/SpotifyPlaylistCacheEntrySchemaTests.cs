using System.Reflection;
using Shouldly;
using TheBluesland.Data.Entities;
using Xunit;

namespace TheBluesland.UnitTests;

/// <summary>
/// US-001, acceptance criterion 3: the <c>spotify_playlist_cache</c> schema must contain exactly
/// the Spotify-owned, playlist-level facts allowed by spec section 9.4 and must never gain a
/// track-level field (title, track id, duration, ISRC, audio-feature data) - see section 11.2 and
/// Spotify Developer Policy §14.
/// </summary>
public sealed class SpotifyPlaylistCacheEntrySchemaTests
{
    private static readonly string[] ExpectedPropertyNames =
    [
        nameof(SpotifyPlaylistCacheEntry.SpotifyPlaylistId),
        nameof(SpotifyPlaylistCacheEntry.Name),
        nameof(SpotifyPlaylistCacheEntry.Description),
        nameof(SpotifyPlaylistCacheEntry.CoverImageUrl),
        nameof(SpotifyPlaylistCacheEntry.TrackCount),
        nameof(SpotifyPlaylistCacheEntry.Artists),
        nameof(SpotifyPlaylistCacheEntry.SpotifySnapshotId),
        nameof(SpotifyPlaylistCacheEntry.SyncedAt),
        nameof(SpotifyPlaylistCacheEntry.IsAvailable),
    ];

    // Substrings that would indicate a forbidden track-level field sneaked into the entity.
    private static readonly string[] ForbiddenPropertyNameFragments =
    [
        "TrackTitle",
        "TrackId",
        "TrackName",
        "Duration",
        "Isrc",
        "Tempo",
        "Energy",
        "Valence",
        "AudioFeature",
        "Danceability",
        "Loudness",
    ];

    private static PropertyInfo[] GetDeclaredProperties() =>
        typeof(SpotifyPlaylistCacheEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    [Fact]
    public void Entity_exposes_exactly_the_columns_defined_in_the_backlog()
    {
        var actualPropertyNames = GetDeclaredProperties().Select(p => p.Name).ToArray();

        actualPropertyNames.ShouldBe(ExpectedPropertyNames, ignoreOrder: true);
    }

    [Fact]
    public void Entity_never_exposes_a_track_level_field()
    {
        var actualPropertyNames = GetDeclaredProperties().Select(p => p.Name).ToArray();

        foreach (var forbiddenFragment in ForbiddenPropertyNameFragments)
        {
            actualPropertyNames.ShouldNotContain(
                name => name.Contains(forbiddenFragment, StringComparison.OrdinalIgnoreCase),
                $"entity must never expose a track-level field matching '{forbiddenFragment}' (spec section 9.4/11.2)");
        }
    }

    [Fact]
    public void TrackCount_is_an_aggregate_integer_not_a_track_collection()
    {
        var trackCountProperty = typeof(SpotifyPlaylistCacheEntry).GetProperty(nameof(SpotifyPlaylistCacheEntry.TrackCount));

        trackCountProperty.ShouldNotBeNull();
        trackCountProperty.PropertyType.ShouldBe(typeof(int));
    }

    [Fact]
    public void Artists_is_a_flat_display_name_array_not_a_per_track_attribution_structure()
    {
        var artistsProperty = typeof(SpotifyPlaylistCacheEntry).GetProperty(nameof(SpotifyPlaylistCacheEntry.Artists));

        artistsProperty.ShouldNotBeNull();
        artistsProperty.PropertyType.ShouldBe(typeof(string[]));
    }
}
