using System.Reflection;
using Shouldly;
using TheBluesland.SpotifyFetcher.Spotify;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// US-003 regression: the sync tool must never carry a track-level field (title, track id,
/// duration, ISRC, audio-feature data) beyond the in-memory computation of track count and artist
/// list (spec section 9.4/11.2). Mirrors SpotifyPlaylistCacheEntrySchemaTests, but for the
/// fetcher's own DTO rather than the persisted entity.
/// </summary>
public sealed class SpotifyPlaylistSummarySchemaTests
{
    private static readonly string[] ExpectedPropertyNames =
    [
        nameof(SpotifyPlaylistSummary.Name),
        nameof(SpotifyPlaylistSummary.Description),
        nameof(SpotifyPlaylistSummary.CoverImageUrl),
        nameof(SpotifyPlaylistSummary.TrackCount),
        nameof(SpotifyPlaylistSummary.Artists),
        nameof(SpotifyPlaylistSummary.SnapshotId),
    ];

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
        typeof(SpotifyPlaylistSummary).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    [Fact]
    public void Summary_exposes_exactly_the_fields_the_sync_tool_is_allowed_to_carry()
    {
        var actualPropertyNames = GetDeclaredProperties().Select(p => p.Name).ToArray();

        actualPropertyNames.ShouldBe(ExpectedPropertyNames, ignoreOrder: true);
    }

    [Fact]
    public void Summary_never_exposes_a_track_level_field()
    {
        var actualPropertyNames = GetDeclaredProperties().Select(p => p.Name).ToArray();

        foreach (var forbiddenFragment in ForbiddenPropertyNameFragments)
        {
            actualPropertyNames.ShouldNotContain(
                name => name.Contains(forbiddenFragment, StringComparison.OrdinalIgnoreCase),
                $"summary must never expose a track-level field matching '{forbiddenFragment}' (spec section 9.4/11.2)");
        }
    }
}
