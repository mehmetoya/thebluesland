using Shouldly;
using TheBluesland.Web.Content;
using TheBluesland.Web.Seo;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-011 AC4/FR-032: regression test guarding that structured data never contains a track-title-
/// shaped field. <see cref="PlaylistContent"/> has no track-level data to copy from (spec 9.4/11.2),
/// so this should stay true automatically - this test exists so a future field addition to
/// <see cref="PlaylistContent"/> or <see cref="StructuredDataBuilder"/> cannot silently break that.
/// </summary>
public sealed class StructuredDataBuilderTests
{
    private static readonly PlaylistContent Content = new(
        Slug: "primary-playlist",
        SpotifyPlaylistId: "0iJt9LMebhOY0KSHSJw3cS",
        Title: "Primary Playlist Fixture",
        Summary: "A solid summary describing this fixture playlist in plain, sufficiently long prose.",
        Moods: ["warm"],
        Genres: ["blues"],
        Occasions: ["late-night"],
        Era: "1970s",
        CuratorNote: "Curator note body.",
        IsPublished: true,
        Featured: false,
        DisplayOrder: 0,
        PublishedAt: new DateOnly(2026, 1, 1),
        PreviousSlugs: []);

    [Fact]
    public void BuildCollectionPage_contains_no_track_shaped_field()
    {
        var json = StructuredDataBuilder.BuildCollectionPage(Content, "https://thebluesland.example/playlists/primary-playlist");

        json.ShouldContain("\"@type\":\"CollectionPage\"");
        json.ShouldContain("Primary Playlist Fixture");
        json.ShouldNotContain("track", Case.Insensitive);
        json.ShouldNotContain("tracklist", Case.Insensitive);
    }

    [Fact]
    public void BuildBreadcrumbList_contains_no_track_shaped_field()
    {
        var json = StructuredDataBuilder.BuildBreadcrumbList(
            "https://thebluesland.example/",
            Content.Title,
            "https://thebluesland.example/playlists/primary-playlist");

        json.ShouldContain("\"@type\":\"BreadcrumbList\"");
        json.ShouldNotContain("track", Case.Insensitive);
    }

    [Fact]
    public void BuildWebSite_contains_no_track_shaped_field()
    {
        var json = StructuredDataBuilder.BuildWebSite("https://thebluesland.example/");

        json.ShouldContain("\"@type\":\"WebSite\"");
        json.ShouldNotContain("track", Case.Insensitive);
    }
}
