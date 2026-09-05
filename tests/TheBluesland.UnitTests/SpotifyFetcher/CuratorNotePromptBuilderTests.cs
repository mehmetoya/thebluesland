using Shouldly;
using TheBluesland.Data.Entities;
using TheBluesland.SpotifyFetcher.CuratorNote;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// ADR-0005 madde 1/US-016 AC1: pins the four-field boundary at the unit that actually builds the
/// AI prompt. The entry below is built with every column populated, including the ones ADR-0005
/// explicitly forbids as AI input (cover image URL, snapshot id, synced-at, availability, and the
/// row's own id) - the assertions prove none of those forbidden values leak into the prompt text,
/// even though the whole row was available to build it from.
/// </summary>
public sealed class CuratorNotePromptBuilderTests
{
    [Fact]
    public void Build_includes_only_the_four_permitted_fields()
    {
        var entry = new SpotifyPlaylistCacheEntry
        {
            SpotifyPlaylistId = "FORBIDDEN-PLAYLIST-ID-00000000",
            Name = "Masterpieces of Erkin the Father",
            Description = "Anadolu rock essentials.",
            CoverImageUrl = "https://forbidden.example/cover.jpg",
            TrackCount = 44,
            Artists = ["Erkin Koray"],
            SpotifySnapshotId = "forbidden-snapshot-token",
            SyncedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            IsAvailable = true,
        };

        var prompt = CuratorNotePromptBuilder.Build(entry);

        prompt.ShouldContain("Masterpieces of Erkin the Father");
        prompt.ShouldContain("Anadolu rock essentials.");
        prompt.ShouldContain("44");
        prompt.ShouldContain("Erkin Koray");

        prompt.ShouldNotContain("FORBIDDEN-PLAYLIST-ID-00000000");
        prompt.ShouldNotContain("https://forbidden.example/cover.jpg");
        prompt.ShouldNotContain("forbidden-snapshot-token");
        prompt.ShouldNotContain("2026-01-01");
    }

    [Fact]
    public void Build_tolerates_a_missing_description_and_no_artists()
    {
        var entry = new SpotifyPlaylistCacheEntry
        {
            SpotifyPlaylistId = "some-id",
            Name = "A Bare Playlist",
            Description = null,
            CoverImageUrl = null,
            TrackCount = 0,
            Artists = [],
            SpotifySnapshotId = null,
            SyncedAt = DateTimeOffset.UtcNow,
            IsAvailable = true,
        };

        var prompt = CuratorNotePromptBuilder.Build(entry);

        prompt.ShouldContain("A Bare Playlist");
        prompt.ShouldContain("(none provided)");
        prompt.ShouldContain("(none listed)");
    }
}
