using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using TheBluesland.Web.Cache;
using TheBluesland.Web.Content;
using TheBluesland.Web.Feed;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// docs/specs/playlists-json-feed.md Testing Strategy: pure mapping/ordering tests over in-memory
/// content and cache snapshots - shape and required fields, slug uniqueness/pattern, ordering,
/// optional-field omission (no nulls or empty strings), collection membership and degradation.
/// </summary>
public sealed partial class PlaylistsFeedBuilderTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, PlaylistCacheSnapshot> NoSnapshots =
        new Dictionary<string, PlaylistCacheSnapshot>();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [Fact]
    public void Build_emits_the_contract_shape_in_contract_field_order()
    {
        var playlist = Playlist("bluesland", "Bluesland", new DateOnly(2025, 3, 14), spotifyId: "id-1", genres: ["blues"]);
        var snapshots = Snapshots(("id-1", new PlaylistCacheSnapshot(true, 3195, "https://i.scdn.co/image/abc")));

        using var json = Parse(Serialize([playlist], snapshots));

        json.RootElement.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["generatedAt", "playlists"]);
        json.RootElement.GetProperty("generatedAt").GetString().ShouldBe("2026-09-18T12:00:00Z");

        var item = json.RootElement.GetProperty("playlists").EnumerateArray().Single();
        item.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["slug", "title", "url", "description", "trackCount", "image", "collections", "addedAt"]);
        item.GetProperty("slug").GetString().ShouldBe("bluesland");
        item.GetProperty("title").GetString().ShouldBe("Bluesland");
        item.GetProperty("url").GetString().ShouldBe("https://thebluesland.com/playlists/bluesland");
        item.GetProperty("description").GetString().ShouldBe(Summary);
        item.GetProperty("trackCount").GetInt32().ShouldBe(3195);
        item.GetProperty("image").GetString().ShouldBe("https://i.scdn.co/image/abc");
        item.GetProperty("collections").EnumerateArray().Select(value => value.GetString()).ShouldBe(["blues"]);
        item.GetProperty("addedAt").GetString().ShouldBe("2025-03-14");
    }

    [Fact]
    public void Build_orders_newest_first_then_by_title_ignoring_case_then_by_slug()
    {
        var playlists = new[]
        {
            Playlist("d-old", "Delta", new DateOnly(2026, 1, 1)),
            Playlist("b-tie", "bravo", new DateOnly(2026, 2, 1)),
            Playlist("a-tie", "Alpha", new DateOnly(2026, 2, 1)),
            Playlist("z-new", "Zulu", new DateOnly(2026, 3, 1)),
            Playlist("same-title-2", "Echo", new DateOnly(2026, 2, 1)),
            Playlist("same-title-1", "echo", new DateOnly(2026, 2, 1)),
        };

        using var json = Parse(Serialize(playlists, NoSnapshots));

        Slugs(json).ShouldBe(["z-new", "a-tie", "b-tie", "same-title-1", "same-title-2", "d-old"]);
    }

    [Fact]
    public void Build_is_deterministic_regardless_of_input_order()
    {
        var playlists = new[]
        {
            Playlist("one", "One", new DateOnly(2026, 1, 1)),
            Playlist("two", "Two", new DateOnly(2026, 1, 1)),
            Playlist("three", "Three", new DateOnly(2026, 5, 1)),
        };

        var forward = Serialize(playlists, NoSnapshots);
        var reversed = Serialize(playlists.Reverse().ToArray(), NoSnapshots);

        reversed.ShouldBe(forward);
    }

    [Fact]
    public void Build_omits_every_optional_field_when_unknown_and_never_emits_null_or_empty_strings()
    {
        var playlist = Playlist("plain", "Plain", new DateOnly(2026, 1, 1));

        using var json = Parse(Serialize([playlist], NoSnapshots));

        var item = json.RootElement.GetProperty("playlists").EnumerateArray().Single();
        item.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["slug", "title", "url", "description", "addedAt"]);
        AssertNoNullOrEmptyString(json.RootElement);
    }

    [Fact]
    public void Build_takes_track_count_and_image_only_from_a_playable_snapshot()
    {
        var playlists = new[]
        {
            Playlist("playable-zero", "A", new DateOnly(2026, 4, 1), spotifyId: "zero"),
            Playlist("not-playable", "B", new DateOnly(2026, 3, 1), spotifyId: "gone"),
        };
        var snapshots = Snapshots(
            ("zero", new PlaylistCacheSnapshot(true, 0, "https://i.scdn.co/image/zero")),
            ("gone", new PlaylistCacheSnapshot(false, 42, "https://i.scdn.co/image/gone")));

        using var json = Parse(Serialize(playlists, snapshots));

        var items = json.RootElement.GetProperty("playlists").EnumerateArray().ToList();
        items[0].GetProperty("trackCount").GetInt32().ShouldBe(0);
        items[0].GetProperty("image").GetString().ShouldBe("https://i.scdn.co/image/zero");
        items[1].TryGetProperty("trackCount", out _).ShouldBeFalse();
        items[1].TryGetProperty("image", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("http://i.scdn.co/image/insecure")]
    [InlineData("/relative/path.jpg")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Build_drops_a_cover_that_is_not_an_absolute_https_url(string cover)
    {
        var playlist = Playlist("cover", "Cover", new DateOnly(2026, 1, 1), spotifyId: "id");
        var snapshots = Snapshots(("id", new PlaylistCacheSnapshot(true, 5, cover)));

        using var json = Parse(Serialize([playlist], snapshots));

        json.RootElement.GetProperty("playlists").EnumerateArray().Single()
            .TryGetProperty("image", out _).ShouldBeFalse();
    }

    [Fact]
    public void Build_lists_collection_membership_in_collection_definition_order()
    {
        // "blues" (genre) is defined before "warm" (mood) in PlaylistCollections.All.
        var member = Playlist("member", "Member", new DateOnly(2026, 1, 1), genres: ["blues-rock"], moods: ["warm"]);
        var outsider = Playlist("outsider", "Outsider", new DateOnly(2026, 1, 2), genres: ["jazz"], moods: []);

        using var json = Parse(Serialize([member, outsider], NoSnapshots));

        var items = json.RootElement.GetProperty("playlists").EnumerateArray().ToDictionary(
            item => item.GetProperty("slug").GetString()!);
        items["member"].GetProperty("collections").EnumerateArray().Select(value => value.GetString())
            .ShouldBe(["blues", "warm"]);
        items["outsider"].TryGetProperty("collections", out _).ShouldBeFalse();
    }

    [Fact]
    public void Build_skips_a_published_entry_that_has_no_published_date_instead_of_inventing_one()
    {
        var dated = Playlist("dated", "Dated", new DateOnly(2026, 1, 1));
        var undated = Playlist("undated", "Undated", publishedAt: null);

        using var json = Parse(Serialize([dated, undated], NoSnapshots));

        Slugs(json).ShouldBe(["dated"]);
    }

    [Fact]
    public void Build_caps_a_long_description_at_the_contract_maximum()
    {
        var playlist = Playlist("long", "Long", new DateOnly(2026, 1, 1), summary: new string('x', 400));

        using var json = Parse(Serialize([playlist], NoSnapshots));

        var description = json.RootElement.GetProperty("playlists").EnumerateArray().Single()
            .GetProperty("description").GetString()!;
        description.Length.ShouldBeLessThanOrEqualTo(PlaylistsFeedBuilder.DescriptionMaxLength);
        description.ShouldEndWith("…");
    }

    [Fact]
    public void Build_omits_a_blank_description_instead_of_emitting_an_empty_string()
    {
        var playlist = Playlist("blank", "Blank", new DateOnly(2026, 1, 1), summary: "   ");

        using var json = Parse(Serialize([playlist], NoSnapshots));

        json.RootElement.GetProperty("playlists").EnumerateArray().Single()
            .TryGetProperty("description", out _).ShouldBeFalse();
    }

    [Fact]
    public void Build_output_has_unique_slugs_that_match_the_contract_pattern()
    {
        var playlists = new[]
        {
            Playlist("blues-101", "A", new DateOnly(2026, 1, 1)),
            Playlist("jazz", "B", new DateOnly(2026, 1, 2)),
            Playlist("anadolu-rock-70s", "C", new DateOnly(2026, 1, 3)),
        };

        using var json = Parse(Serialize(playlists, NoSnapshots));

        var slugs = Slugs(json);
        slugs.Distinct().Count().ShouldBe(slugs.Count);
        slugs.ShouldAllBe(slug => SlugPattern().IsMatch(slug));
    }

    [Fact]
    public void Build_formats_generated_at_as_utc_with_second_precision()
    {
        var feed = PlaylistsFeedBuilder.Build(
            [],
            NoSnapshots,
            slug => $"https://thebluesland.com/playlists/{slug}",
            new DateTimeOffset(2026, 9, 18, 15, 30, 45, 123, TimeSpan.FromHours(3)));

        feed.GeneratedAt.ShouldBe("2026-09-18T12:30:45Z");
    }

    [Fact]
    public void Serialize_writes_apostrophes_ampersands_and_turkish_characters_as_written()
    {
        var playlist = Playlist("odd", "Mehmet's Şarkı & Türkü", new DateOnly(2026, 1, 1));

        var body = Serialize([playlist], NoSnapshots);

        body.ShouldContain("Mehmet's Şarkı & Türkü");
        body.ShouldNotContain("\\u");
    }

    private const string Summary = "A plain-text blurb that is comfortably longer than forty characters.";

    private static string Serialize(
        IReadOnlyList<PlaylistContent> published,
        IReadOnlyDictionary<string, PlaylistCacheSnapshot> snapshots) =>
        PlaylistsFeedBuilder.Serialize(PlaylistsFeedBuilder.Build(
            published,
            snapshots,
            slug => $"https://thebluesland.com/playlists/{slug}",
            GeneratedAt));

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    private static List<string> Slugs(JsonDocument json) =>
        json.RootElement.GetProperty("playlists").EnumerateArray()
            .Select(item => item.GetProperty("slug").GetString()!)
            .ToList();

    private static IReadOnlyDictionary<string, PlaylistCacheSnapshot> Snapshots(
        params (string SpotifyPlaylistId, PlaylistCacheSnapshot Snapshot)[] entries) =>
        entries.ToDictionary(entry => entry.SpotifyPlaylistId, entry => entry.Snapshot);

    private static PlaylistContent Playlist(
        string slug,
        string title,
        DateOnly? publishedAt,
        string? spotifyId = null,
        string summary = Summary,
        string[]? genres = null,
        string[]? moods = null) =>
        new(
            Slug: slug,
            SpotifyPlaylistId: spotifyId ?? $"spotify-{slug}",
            Title: title,
            Summary: summary,
            Moods: moods ?? [],
            Genres: genres ?? [],
            Occasions: [],
            Eras: [],
            CuratorNote: "Curator note.",
            IsPublished: true,
            FeaturedOrder: null,
            DisplayOrder: 0,
            PublishedAt: publishedAt,
            PreviousSlugs: []);

    private static void AssertNoNullOrEmptyString(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                Assert.Fail("The feed must never emit null.");
                break;
            case JsonValueKind.String:
                element.GetString().ShouldNotBeNullOrEmpty();
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AssertNoNullOrEmptyString(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertNoNullOrEmptyString(item);
                }

                break;
        }
    }
}
