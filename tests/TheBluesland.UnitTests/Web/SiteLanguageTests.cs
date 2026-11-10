using Shouldly;
using TheBluesland.Web.Analytics;
using TheBluesland.Web.Content;
using TheBluesland.Web.Localization;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>Pure pieces of docs/specs/site-i18n.md: URL prefix handling, formatting, content fallback.</summary>
public sealed class SiteLanguageTests
{
    [Theory]
    [InlineData("/tr", "tr", "/")]
    [InlineData("/en/", "en", "/")]
    [InlineData("/TR/about", "tr", "/about")]
    [InlineData("/en/playlists/x?mix=a", "en", "/playlists/x?mix=a")]
    [InlineData("/tr?genre=rock", "tr", "/?genre=rock")]
    public void A_language_prefix_is_split_from_the_rest_of_the_url(string input, string code, string rest)
    {
        SiteLanguage.TrySplit(input, out var actualCode, out var actualRest).ShouldBeTrue();
        actualCode.ShouldBe(code);
        actualRest.ShouldBe(rest);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/about")]
    [InlineData("/trx/about")]
    [InlineData("/de/about")]
    [InlineData("/not-found")]
    public void Anything_else_has_no_language_prefix(string input) =>
        SiteLanguage.TrySplit(input, out _, out _).ShouldBeFalse();

    [Theory]
    [InlineData("/", "/tr")]
    [InlineData("/about", "/tr/about")]
    [InlineData("/?genre=rock", "/tr?genre=rock")]
    [InlineData("/playlists/x", "/tr/playlists/x")]
    public void Url_prefixes_the_page_language(string path, string expected) =>
        new SiteLanguage("tr", "/tr").Url(path).ShouldBe(expected);

    [Fact]
    public void The_switch_url_keeps_the_page_and_query_and_falls_back_to_the_other_home()
    {
        new SiteLanguage("tr", "/tr/playlists/x?mix=same-vibe").SwitchUrl("en").ShouldBe("/en/playlists/x?mix=same-vibe");
        new SiteLanguage("en", "/en?genre=rock").SwitchUrl("tr").ShouldBe("/tr?genre=rock");
        new SiteLanguage("en", "/no-prefix-here").SwitchUrl("tr").ShouldBe("/tr");
    }

    [Fact]
    public void Dates_and_numbers_follow_the_page_language_without_icu_data()
    {
        var date = new DateOnly(2026, 11, 16);
        new SiteLanguage("tr", "/tr").FormatDate(date).ShouldBe("16 Kasım 2026");
        new SiteLanguage("en", "/en").FormatDate(date).ShouldBe("16 November 2026");
        new SiteLanguage("tr", "/tr").FormatNumber(1234).ShouldBe("1.234");
        new SiteLanguage("en", "/en").FormatNumber(1234).ShouldBe("1,234");
    }

    [Fact]
    public void Every_taxonomy_value_has_a_turkish_label_and_english_keeps_the_raw_value()
    {
        var values = new[]
        {
            "melancholic", "warm", "energetic", "raw", "nostalgic",
            "blues", "blues-rock", "rock", "soul", "jazz", "anadolu-rock", "punk", "metal", "indie", "folk", "funk",
            "country", "classical", "electronic", "world", "pop",
            "late-night", "night-drive", "road-trip", "slow-evening", "headphones", "focus", "dancing", "deep-listen",
            "pre-1970", "1970s", "1980s-1990s", "2000s-present", "mixed-era",
        };

        foreach (var value in values)
        {
            TaxonomyLabels.Label(value, "en").ShouldBe(value);
            TaxonomyLabels.Label(value, "tr").ShouldNotBeNullOrWhiteSpace();
        }

        TaxonomyLabels.Label("jazz", "tr").ShouldBe("caz");
        TaxonomyLabels.Label("brand-new-tag", "tr").ShouldBe("brand-new-tag");
    }

    [Theory]
    [InlineData("/tr")]
    [InlineData("/en/about")]
    [InlineData("/tr/collections")]
    [InlineData("/tr/weekly")]
    public void Prefixed_content_routes_are_recorded_as_page_views(string path) =>
        PageViewRoute.TryClassify(path, out _).ShouldBeTrue();

    [Fact]
    public void A_prefixed_playlist_page_keeps_its_slug_and_social_cards_are_still_not_page_views()
    {
        PageViewRoute.TryClassify("/tr/playlists/blue-monday", out var slug).ShouldBeTrue();
        slug.ShouldBe("blue-monday");
        PageViewRoute.TryClassify("/playlists/blue-monday/og-image.png", out _).ShouldBeFalse();
        PageViewRoute.TryClassify("/tr/no-such-page", out _).ShouldBeFalse();
    }

    [Fact]
    public void Turkish_editorial_text_falls_back_to_english_with_a_lang_attribute_only_when_it_is_missing()
    {
        var playlist = new PlaylistContent("s", "id", "Title", "Summary", [], [], [], [], "Note", true, null, 0, null, []);

        playlist.TitleIn("en").ShouldBe(("Title", null));
        playlist.TitleIn("tr").ShouldBe(("Title", "en"));

        var translated = playlist with { TitleTr = "Başlık", SummaryTr = "  ", CuratorNoteTr = "Not" };
        translated.TitleIn("tr").ShouldBe(("Başlık", null));
        translated.SummaryIn("tr").ShouldBe(("Summary", "en"));
        translated.CuratorNoteIn("tr").ShouldBe(("Not", null));
        translated.TitleIn("en").ShouldBe(("Title", null));
    }

    [Fact]
    public async Task The_reader_maps_optional_turkish_fields_and_splits_the_turkish_note()
    {
        var directory = Path.Combine(Path.GetTempPath(), "i18n-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "a.md"), """
                ---
                slug: a
                spotifyPlaylistId: 0iJt9LMebhOY0KSHSJw3cS
                title: English title
                summary: English summary
                titleTr: Türkçe başlık
                status: published
                ---
                English note.

                <!-- lang:tr -->
                Türkçe not.
                """);
            await File.WriteAllTextAsync(Path.Combine(directory, "b.md"), """
                ---
                slug: b
                spotifyPlaylistId: 0iJt9LMebhOY0KSHSJw3cS
                title: Only English
                status: published
                ---
                Just English.
                """);

            var playlists = await new PlaylistContentReader().ReadAllAsync(directory, CancellationToken.None);

            var a = playlists.Single(p => p.Slug == "a");
            a.TitleTr.ShouldBe("Türkçe başlık");
            a.SummaryTr.ShouldBeNull();
            a.CuratorNote.ShouldBe("English note.");
            a.CuratorNoteTr.ShouldBe("Türkçe not.");
            var b = playlists.Single(p => p.Slug == "b");
            b.TitleTr.ShouldBeNull();
            b.CuratorNote.ShouldBe("Just English.");
            b.CuratorNoteTr.ShouldBeNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
