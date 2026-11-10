using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TheBluesland.Web;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// docs/specs/site-i18n.md end to end, against the real in-process host: the "/" language redirect,
/// the 301s from every pre-localization URL, <c>lang</c>/canonical/hreflang/og:locale on every page
/// in both languages, the switcher, the cookie, and the URLs that must stay unprefixed.
/// </summary>
public sealed class SiteLocalizationTests : IAsyncLifetime
{
    private const string Origin = "https://thebluesland.onrender.com";
    private const string Slug = "masterpieces-of-erkin-the-father";

    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _app = WebHostFactory.Create([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] =
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists");
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] =
                "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=thebluesland;Timeout=2";
        });
        await _app.StartAsync();

        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData(null, null, "/en")]
    [InlineData("tr-TR,tr;q=0.9,en;q=0.8", null, "/tr")]
    [InlineData("en-US,en;q=0.9,tr;q=0.8", null, "/en")]
    [InlineData("de-DE,tr;q=0.5", null, "/tr")]
    [InlineData("fr-FR,de;q=0.8", null, "/en")]
    [InlineData("en-US", "tr", "/tr")] // the saved choice beats the browser's language
    [InlineData("tr-TR", "en", "/en")]
    [InlineData("tr-TR", "xx", "/tr")] // a junk cookie is ignored
    public async Task Root_redirects_to_the_saved_or_browser_language_and_is_never_cached(string? acceptLanguage, string? cookie, string location)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"lang={cookie}");
        }

        using var response = await _http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(location);
        response.Headers.Vary.ShouldContain("Accept-Language");
        response.Headers.Vary.ShouldContain("Cookie");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Root_redirect_keeps_the_query_string_of_an_old_filtered_link()
    {
        using var response = await _http.GetAsync("/?genre=blues&page=2");

        response.Headers.Location!.OriginalString.ShouldBe("/en?genre=blues&page=2");
    }

    [Theory]
    [InlineData("/about", "/en/about")]
    [InlineData("/privacy", "/en/privacy")]
    [InlineData("/terms", "/en/terms")]
    [InlineData("/collections", "/en/collections")]
    [InlineData("/collections/blues", "/en/collections/blues")]
    [InlineData("/weekly", "/en/weekly")]
    [InlineData("/playlists/" + Slug, "/en/playlists/" + Slug)]
    [InlineData("/playlists/" + Slug + "?mix=same-vibe", "/en/playlists/" + Slug + "?mix=same-vibe")]
    public async Task Pre_localization_urls_move_permanently_to_the_english_twin(string oldPath, string location)
    {
        using var response = await _http.GetAsync(oldPath);

        response.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.OriginalString.ShouldBe(location);
    }

    [Theory]
    [InlineData("/sitemap.xml", "application/xml")]
    [InlineData("/robots.txt", "text/plain")]
    [InlineData("/llms.txt", "text/plain")]
    [InlineData("/playlists.json", "application/json")]
    [InlineData("/og-image.png", "image/png")]
    [InlineData("/playlists/" + Slug + "/og-image.png", "image/png")]
    [InlineData("/health/live", null)]
    public async Task Non_page_urls_are_never_prefixed_or_redirected(string path, string? mediaType)
    {
        using var response = await _http.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        if (mediaType is not null)
        {
            response.Content.Headers.ContentType!.MediaType.ShouldBe(mediaType);
        }
    }

    [Theory]
    [InlineData("tr", "", "/")]
    [InlineData("en", "", "/")]
    [InlineData("tr", "/about", "/about")]
    [InlineData("en", "/about", "/about")]
    [InlineData("tr", "/privacy", "/privacy")]
    [InlineData("en", "/terms", "/terms")]
    [InlineData("tr", "/collections", "/collections")]
    [InlineData("en", "/collections/blues", "/collections/blues")]
    [InlineData("tr", "/playlists/" + Slug, "/playlists/" + Slug)]
    [InlineData("en", "/playlists/" + Slug, "/playlists/" + Slug)]
    [InlineData("tr", "/weekly", "/weekly")]
    [InlineData("en", "/weekly", "/weekly")]
    public async Task Every_page_renders_in_both_languages_with_lang_canonical_and_hreflang(string language, string suffix, string neutralPath)
    {
        var body = await GetOkAsync($"/{language}{suffix}");

        body.ShouldContain($"<html lang=\"{language}\">");
        body.ShouldContain($"<link rel=\"canonical\" href=\"{Origin}/{language}{(neutralPath == "/" ? string.Empty : neutralPath)}\" />");
        var tail = neutralPath == "/" ? string.Empty : neutralPath;
        body.ShouldContain($"<link rel=\"alternate\" hreflang=\"tr\" href=\"{Origin}/tr{tail}\" />");
        body.ShouldContain($"<link rel=\"alternate\" hreflang=\"en\" href=\"{Origin}/en{tail}\" />");
        body.ShouldContain($"<link rel=\"alternate\" hreflang=\"x-default\" href=\"{Origin}/en{tail}\" />");
        body.ShouldContain($"<meta property=\"og:locale\" content=\"{(language == "tr" ? "tr_TR" : "en_US")}\" />");
        body.ShouldContain($"<meta property=\"og:locale:alternate\" content=\"{(language == "tr" ? "en_US" : "tr_TR")}\" />");
        // Social cards stay language-neutral and unprefixed.
        body.ShouldContain("/og-image.png");
        body.ShouldNotContain($"/{language}/og-image.png");
    }

    [Fact]
    public async Task Turkish_chrome_and_taxonomy_labels_are_translated_and_english_stays_as_before()
    {
        var turkish = await GetOkAsync("/tr");
        turkish.ShouldContain("Çalma listeleri");
        turkish.ShouldContain("Koleksiyonlar");
        turkish.ShouldContain("Hakkında");
        turkish.ShouldContain("Filtreleri uygula");
        turkish.ShouldContain("Tarza ya da ruh hâline göre çalma listesi bul");
        turkish.ShouldContain("melankolik");
        turkish.ShouldContain("Spotify ile bağlantılı değildir");
        // Editorial text is English-only today: marked so, never machine-translated.
        turkish.ShouldContain("<h2 lang=\"en\">Masterpieces of Erkin the Father</h2>");

        var english = await GetOkAsync("/en");
        english.ShouldContain("Apply filters");
        english.ShouldContain("melancholic");
        english.ShouldNotContain("Filtreleri uygula");
        english.ShouldContain("<h2>Masterpieces of Erkin the Father</h2>");
    }

    [Fact]
    public async Task Turkish_search_matches_turkish_tag_labels()
    {
        var turkish = await GetOkAsync("/tr?q=enerjik");
        turkish.ShouldContain("Masterpieces of Erkin the Father"); // tagged "energetic"

        var english = await GetOkAsync("/en?q=enerjik");
        english.ShouldNotContain("Masterpieces of Erkin the Father");
    }

    [Fact]
    public async Task The_switcher_links_to_the_same_page_in_the_other_language_and_marks_the_current_one()
    {
        var turkish = await GetOkAsync("/tr/about");
        turkish.ShouldContain("<nav aria-label=\"Dil\" class=\"lang-switcher\">");
        turkish.ShouldContain("<a href=\"/tr/about\" lang=\"tr\" hreflang=\"tr\" aria-label=\"Türkçe\" title=\"Türkçe\" aria-current=\"page\">TR</a>");
        turkish.ShouldContain("href=\"/en/about\" lang=\"en\" hreflang=\"en\" aria-label=\"English\" title=\"English\"");

        var english = await GetOkAsync("/en/playlists/" + Slug);
        english.ShouldContain($"href=\"/tr/playlists/{Slug}\" lang=\"tr\" hreflang=\"tr\"");

        // A filtered home page keeps its filters when the language changes.
        var filtered = await GetOkAsync("/tr?genre=rock");
        filtered.ShouldContain("href=\"/en?genre=rock\" lang=\"en\" hreflang=\"en\"");

        // Home counts as the Playlists section in both languages.
        (await GetOkAsync("/tr")).ShouldContain("<a href=\"/tr\" aria-current=\"page\">Çalma listeleri</a>");
    }

    [Fact]
    public async Task Visiting_a_prefixed_page_saves_the_language_in_a_cookie_only_when_it_changes()
    {
        using var first = await _http.GetAsync("/tr/about");
        var cookie = first.Headers.GetValues("Set-Cookie").Single();
        cookie.ShouldStartWith("lang=tr");
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);
        cookie.ShouldContain("max-age=31536000", Case.Insensitive);

        using var sameLanguage = new HttpRequestMessage(HttpMethod.Get, "/tr/about");
        sameLanguage.Headers.Add("Cookie", "lang=tr");
        using var repeat = await _http.SendAsync(sameLanguage);
        repeat.Headers.Contains("Set-Cookie").ShouldBeFalse();

        using var otherLanguage = new HttpRequestMessage(HttpMethod.Get, "/en/about");
        otherLanguage.Headers.Add("Cookie", "lang=tr");
        using var switched = await _http.SendAsync(otherLanguage);
        switched.Headers.GetValues("Set-Cookie").Single().ShouldStartWith("lang=en");

        // QA-01: plain HTTP is not Secure; behind the TLS-terminating proxy (X-Forwarded-Proto) it is.
        // (The client kept the lang=en cookie from above, so a /tr page sets the cookie again.)
        cookie.ShouldNotContain("secure", Case.Insensitive);
        using var proxied = new HttpRequestMessage(HttpMethod.Get, "/tr/about");
        proxied.Headers.Add("X-Forwarded-Proto", "https");
        using var proxiedResponse = await _http.SendAsync(proxied);
        proxiedResponse.StatusCode.ShouldBe(HttpStatusCode.OK, proxiedResponse.Headers.ToString());
        proxiedResponse.Headers.GetValues("Set-Cookie").Single().ShouldContain("secure", Case.Insensitive);

        // Errors and non-page URLs never set it.
        using var missing = await _http.GetAsync("/tr/no-such-page");
        missing.Headers.Contains("Set-Cookie").ShouldBeFalse();
        using var sitemap = await _http.GetAsync("/sitemap.xml");
        sitemap.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [Fact]
    public async Task Sitemap_lists_both_languages_with_alternates_and_x_default()
    {
        var xml = await _http.GetStringAsync("/sitemap.xml");

        xml.ShouldContain("xmlns:xhtml=\"http://www.w3.org/1999/xhtml\"");
        xml.ShouldContain($"<url><loc>{Origin}/tr/about</loc>");
        xml.ShouldContain($"<url><loc>{Origin}/en/about</loc>");
        xml.ShouldContain($"<xhtml:link rel=\"alternate\" hreflang=\"tr\" href=\"{Origin}/tr/about\"/>");
        xml.ShouldContain($"<xhtml:link rel=\"alternate\" hreflang=\"en\" href=\"{Origin}/en/about\"/>");
        xml.ShouldContain($"<xhtml:link rel=\"alternate\" hreflang=\"x-default\" href=\"{Origin}/en/about\"/>");
        xml.ShouldContain($"<url><loc>{Origin}/tr</loc>");
        xml.ShouldContain($"<url><loc>{Origin}/tr/playlists/{Slug}</loc>");
        xml.ShouldNotContain($"<loc>{Origin}/about</loc>");
    }

    [Fact]
    public async Task Structured_data_names_the_page_language()
    {
        (await GetOkAsync("/tr/about")).ShouldContain("\"inLanguage\":\"tr\"");
        (await GetOkAsync("/en/about")).ShouldContain("\"inLanguage\":\"en\"");
        // QA-06: the WebSite description is in the page language too, not English under inLanguage "tr".
        var turkishHome = await GetOkAsync("/tr");
        turkishHome.ShouldContain("\"inLanguage\":\"tr\"");
        turkishHome.ShouldNotContain("personal collection of Spotify playlists, rooted in blues");
        (await GetOkAsync("/en")).ShouldContain("personal collection of Spotify playlists, rooted in blues");
        // The playlist's own text is English until a Turkish field exists.
        (await GetOkAsync("/tr/playlists/" + Slug)).ShouldContain("\"inLanguage\":\"en\"");
    }

    [Fact]
    public async Task About_privacy_and_terms_are_fully_turkish_on_the_turkish_site()
    {
        var about = await GetOkAsync("/tr/about");
        about.ShouldContain("<h1>TheBluesland Hakkında</h1>");
        about.ShouldContain("Sık sorulan sorular");
        about.ShouldNotContain("Frequently asked questions");

        var privacy = await GetOkAsync("/tr/privacy");
        privacy.ShouldContain("<h1>Gizlilik</h1>");
        privacy.ShouldContain("Kit'e (kit.com)");
        privacy.ShouldNotContain("Browsing the site");

        var terms = await GetOkAsync("/tr/terms");
        terms.ShouldContain("<h1>Koşullar</h1>");
        terms.ShouldContain("Spotify AB");
    }

    [Fact]
    public async Task Turkish_collection_cards_and_pages_use_the_taxonomy_label_for_title_and_description()
    {
        var index = await GetOkAsync("/tr/collections");
        index.ShouldContain("<h3>Melankolik çalma listeleri</h3>");
        index.ShouldContain("<p>Melankolik çalma listelerini keşfet.</p>");
        index.ShouldContain("<h3>Anadolu rock çalma listeleri</h3>");
        index.ShouldContain("<h3>1970 öncesi çalma listeleri</h3>");
        index.ShouldNotContain("Melancholic Playlists");

        var detail = await GetOkAsync("/tr/collections/melancholic");
        detail.ShouldContain("<h1>Melankolik çalma listeleri</h1>");
        // Only the editorial introduction stays English, and says so.
        detail.ShouldContain("<p lang=\"en\">For when the mood calls");

        (await GetOkAsync("/en/collections")).ShouldContain("<h3>Melancholic Playlists</h3>");
    }

    [Fact]
    public async Task Turkish_static_pages_use_the_corrected_wording()
    {
        var about = await GetOkAsync("/tr/about");
        about.ShouldContain("TheBluesland de Spotify");
        about.ShouldContain("ruh hâl");
        about.ShouldNotContain("ruh hali");
        about.ShouldContain("<p class=\"lede\">");
        about.ShouldContain("href=\"/tr/weekly\">TheBluesland Weekly</a>");
        (await GetOkAsync("/en/about")).ShouldContain("href=\"/en/weekly\">TheBluesland Weekly</a>");

        var terms = await GetOkAsync("/tr/terms");
        terms.ShouldContain("<h2>Dinleme ve erişim</h2>");
        terms.ShouldNotContain("erişilebilirlik");

        var privacy = await GetOkAsync("/tr/privacy");
        privacy.ShouldContain("Spotify, verileri nasıl");
        privacy.ShouldContain("kendi gizlilik politikasında</a>");
    }

    [Theory]
    [InlineData("/tr/weekly", "birkaç dakikada")]
    [InlineData("/en/weekly", "in a few minutes")]
    public async Task The_slogan_promises_a_few_minutes_not_five(string path, string expected)
    {
        var body = await GetOkAsync(path);

        body.ShouldContain(expected);
        body.ShouldNotContain("five minutes");
        body.ShouldNotContain("beş dakika");
    }

    private async Task<string> GetOkAsync(string path)
    {
        using var response = await _http.GetAsync(path);
        var body = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return body;
    }
}
