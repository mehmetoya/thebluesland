using Shouldly;
using TheBluesland.Web.Cache;
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
        Eras: ["1970s"],
        CuratorNote: "Curator note body.",
        IsPublished: true,
        Featured: false,
        DisplayOrder: 0,
        PublishedAt: new DateOnly(2026, 1, 1),
        PreviousSlugs: []);

    private static readonly PlaylistCacheSnapshot PlayableSnapshot =
        new(IsPlayable: true, TrackCount: 44, CoverImageUrl: "https://i.scdn.co/image/cover.jpg");

    [Fact]
    public void CollectionPage_describes_playlist_and_omits_invalid_spotify_urls()
    {
        using var document = System.Text.Json.JsonDocument.Parse(StructuredDataBuilder.BuildCollectionPage(
            Content with { SpotifyPlaylistId = "invalid" },
            PlayableSnapshot,
            "https://example.com/playlists/primary-playlist"));
        var page = document.RootElement;
        page.GetProperty("datePublished").GetString().ShouldBe("2026-01-01");
        var mainEntity = page.GetProperty("mainEntity");
        mainEntity.GetProperty("@type").GetString().ShouldBe("MusicPlaylist");
        mainEntity.TryGetProperty("url", out _).ShouldBeFalse();
        page.GetProperty("keywords").GetArrayLength().ShouldBe(4);

        // AEO: the playlist's own track count and cover image, already visible elsewhere on the
        // same page, are restated here for answer engines - not new disclosure (spec 9.4/11.2 is
        // about track-LEVEL data: titles/ids/ISRC, never an aggregate count already on the page).
        mainEntity.GetProperty("numTracks").GetInt32().ShouldBe(44);
        mainEntity.GetProperty("image").GetString().ShouldBe("https://i.scdn.co/image/cover.jpg");
    }

    [Fact]
    public void CollectionPage_omits_num_tracks_and_image_when_the_cache_is_unavailable()
    {
        using var document = System.Text.Json.JsonDocument.Parse(StructuredDataBuilder.BuildCollectionPage(
            Content, PlaylistCacheSnapshot.Unavailable, "https://example.com/playlists/primary-playlist"));

        var mainEntity = document.RootElement.GetProperty("mainEntity");
        mainEntity.TryGetProperty("numTracks", out _).ShouldBeFalse();
        mainEntity.TryGetProperty("image", out _).ShouldBeFalse();
    }

    [Fact]
    public void BuildCollectionPage_contains_no_track_level_field()
    {
        var json = StructuredDataBuilder.BuildCollectionPage(
            Content, PlayableSnapshot, "https://thebluesland.example/playlists/primary-playlist");

        json.ShouldContain("\"@type\":\"CollectionPage\"");
        json.ShouldContain("Primary Playlist Fixture");

        // "numTracks"/"track" itself is fine (an aggregate count, asserted above) - what must never
        // appear is anything shaped like a single track: title, id, ISRC or duration.
        json.ShouldNotContain("trackTitle", Case.Insensitive);
        json.ShouldNotContain("trackId", Case.Insensitive);
        json.ShouldNotContain("tracklist", Case.Insensitive);
        json.ShouldNotContain("isrc", Case.Insensitive);
        json.ShouldNotContain("duration", Case.Insensitive);
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

    [Fact]
    public void BuildFaqPage_emits_one_question_per_item_in_order()
    {
        (string Question, string Answer)[] items =
        [
            ("Is TheBluesland affiliated with Spotify?", "No, it's an independent personal project."),
            ("How often is the site updated?", "Roughly once a month."),
        ];

        using var document = System.Text.Json.JsonDocument.Parse(StructuredDataBuilder.BuildFaqPage(items));
        var root = document.RootElement;

        root.GetProperty("@type").GetString().ShouldBe("FAQPage");
        var mainEntity = root.GetProperty("mainEntity");
        mainEntity.GetArrayLength().ShouldBe(2);
        mainEntity[0].GetProperty("@type").GetString().ShouldBe("Question");
        mainEntity[0].GetProperty("name").GetString().ShouldBe(items[0].Question);
        mainEntity[0].GetProperty("acceptedAnswer").GetProperty("@type").GetString().ShouldBe("Answer");
        mainEntity[0].GetProperty("acceptedAnswer").GetProperty("text").GetString().ShouldBe(items[0].Answer);
        mainEntity[1].GetProperty("name").GetString().ShouldBe(items[1].Question);
    }

    [Fact]
    public void BuildAboutPage_names_the_curator_without_a_sameAs_link()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            StructuredDataBuilder.BuildAboutPage("Mehmet", "Curator of TheBluesland.", "https://thebluesland.example/about"));
        var root = document.RootElement;

        root.GetProperty("@type").GetString().ShouldBe("AboutPage");
        var person = root.GetProperty("mainEntity");
        person.GetProperty("@type").GetString().ShouldBe("Person");
        person.GetProperty("name").GetString().ShouldBe("Mehmet");
        person.GetProperty("description").GetString().ShouldBe("Curator of TheBluesland.");
        person.TryGetProperty("sameAs", out _).ShouldBeFalse();
    }
}
