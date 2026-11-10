using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TheBluesland.UnitTests.WeeklyNewsletter.Clustering;
using TheBluesland.Web;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>weekly-archive: /weekly lists sent issues, each issue renders sanitized, sign-up goes to Kit.</summary>
public sealed class WeeklyArchiveTests : IAsyncLifetime
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-weekly");

    private readonly List<WebApplication> _apps = [];
    private readonly List<HttpClient> _clients = [];
    private HttpClient _http = null!;
    private HttpClient _noRedirect = null!;

    [Fact]
    public void Only_sent_issues_are_published_newest_first_turkish_first()
    {
        var repository = new WeeklyIssueRepository(Config(), new FixedTimeProvider(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero)));

        repository.Published().Select(issue => issue.Path).ShouldBe(["/weekly/2026-W46/tr", "/weekly/2026-W46/en"]);
        repository.Find("2099-W01", "tr").ShouldBeNull();
    }

    [Fact]
    public void An_issue_is_hidden_until_its_send_date_in_istanbul()
    {
        // Sunday 2026-11-08 23:30 UTC is already Monday 02:30 in Istanbul.
        new WeeklyIssueRepository(Config(), new FixedTimeProvider(new DateTimeOffset(2026, 11, 8, 20, 0, 0, TimeSpan.Zero))).Published().ShouldBeEmpty();
        new WeeklyIssueRepository(Config(), new FixedTimeProvider(new DateTimeOffset(2026, 11, 8, 23, 30, 0, TimeSpan.Zero))).Published().Count.ShouldBe(2);
    }

    [Fact]
    public void The_body_is_sanitized_and_structured()
    {
        var html = new WeeklyIssueRepository(Config()).Find("2026-W46", "tr")!.Html;

        html.ShouldContain("<h2>Richard Lloyd hayatını kaybetti</h2>");
        html.ShouldContain("<a href=\"https://pitchfork.com/x\">Pitchfork</a>");
        html.ShouldContain("<p class=\"weekly-lede\">Bu hafta beş haber, beş dakika.</p>");
        html.ShouldContain("<section class=\"weekly-story\">");
        html.ShouldContain("<p class=\"weekly-why\"><em>Neden önemli:</em>");
        html.ShouldContain("<p class=\"weekly-sources\">Kaynaklar:");
        html.ShouldContain("<p class=\"weekly-ai-note\"><em>Bu sayı yapay zekâ");
        html.ShouldNotContain("<script>");
        html.ShouldNotContain("javascript:");
    }

    [Fact]
    public void The_layout_marks_every_part_of_an_issue_and_derives_the_card_facts()
    {
        var layout = WeeklyIssueLayout.Build("""
            Intro sentence one. Sentence two.

            ## First

            Body <b>one</b>.

            *Why it matters:* Because.

            Sources: [A](https://a.test) · [B](https://b.test)

            ## Second

            Body two.

            *Why it matters:* Also.

            Sources: [C](https://c.test)

            See you next week.

            *AI note.*
            """);

        layout.Lede.ShouldBe("Intro sentence one.");
        layout.Headlines.ShouldBe(["First", "Second"]);
        layout.ReadMinutes.ShouldBe(1);
        Regex.Matches(layout.Html, "<section class=\"weekly-story\">").Count.ShouldBe(2);
        Regex.Matches(layout.Html, "</section>").Count.ShouldBe(2);
        layout.Html.ShouldContain("<p class=\"weekly-lede\">");
        layout.Html.ShouldContain("<p class=\"weekly-outro\">See you next week.</p>");
        layout.Html.ShouldContain("<p class=\"weekly-ai-note\"><em>AI note.</em></p>");
        layout.Html.ShouldContain("&lt;b&gt;one&lt;/b&gt;");
        layout.Html.IndexOf("weekly-outro", StringComparison.Ordinal).ShouldBeGreaterThan(layout.Html.LastIndexOf("</section>", StringComparison.Ordinal));
    }

    [Fact]
    public void Previous_and_next_issues_are_found_within_the_same_language()
    {
        var repository = new WeeklyIssueRepository(Config(), new FixedTimeProvider(new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var older = repository.Find("2026-W46", "tr")!;
        var newer = repository.Find("2099-W01", "tr")!;

        repository.Neighbours(older).ShouldBe((null, newer));
        repository.Neighbours(newer).ShouldBe((older, null));
        repository.Neighbours(repository.Find("2026-W46", "en")!).Next.ShouldBeNull();
    }

    [Fact]
    public void An_issue_knows_its_number_display_title_and_localized_date()
    {
        var issue = new WeeklyIssueRepository(Config()).Find("2026-W46", "tr")!;

        issue.Number.ShouldBe(46);
        issue.DisplayTitle.ShouldBe("46. hafta");
        issue.FormattedDate.ShouldBe("9 Kasım 2026");
        WeeklyIssue.FormatDate(issue.SendDate, "en").ShouldBe("9 November 2026");
    }

    [Fact]
    public async Task The_archive_shows_issue_cards_and_one_sign_up_form_with_a_button_per_configured_kit_form()
    {
        var response = await _http.GetAsync("/weekly");
        var body = await BodyAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("<title>TheBluesland Weekly - TheBluesland</title>");
        body.ShouldContain("Past issues");
        body.ShouldContain("lang=\"tr\">Geçmiş sayılar</p>");
        body.ShouldContain("Issue 46");
        body.ShouldContain("9 November 2026");
        body.ShouldContain("href=\"/weekly/2026-W46/tr\"");
        body.ShouldContain("href=\"/weekly/2026-W46/en\"");
        Regex.IsMatch(body, "<a [^>]*href=\"/weekly/2026-W46/tr\" lang=\"tr\" hreflang=\"tr\">Türkçe oku").ShouldBeTrue();
        body.ShouldContain(">Read in English");
        body.ShouldNotContain("TheBluesland Weekly · ");
        body.ShouldNotContain("2099-W01");

        body.ShouldContain("action=\"https://app.kit.com/forms/123/subscriptions\"");
        body.ShouldContain("formaction=\"https://app.kit.com/forms/123/subscriptions\"");
        body.ShouldContain("formaction=\"https://app.kit.com/forms/456/subscriptions\"");
        Regex.Matches(body, "name=\"email_address\"").Count.ShouldBe(1);
        Regex.Matches(body, "<form ").Count.ShouldBe(1);
        body.ShouldContain("Get it in Turkish");
        body.ShouldContain("Get it in English");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("form-action 'self' https://app.kit.com");
    }

    [Fact]
    public async Task An_empty_archive_says_when_the_first_issue_arrives_and_when_sign_ups_are_not_configured()
    {
        var (http, _) = await StartAsync(builder =>
        {
            builder.Configuration[WeeklyIssueRepository.ContentDirectoryConfigKey] = Path.Combine(Path.GetTempPath(), "no-weekly-" + Guid.NewGuid().ToString("N"));
            builder.Configuration["Weekly:KitFormIdTr"] = string.Empty;
            builder.Configuration["Weekly:KitFormIdEn"] = string.Empty;
        });

        var body = await BodyAsync(await http.GetAsync("/weekly"));

        body.ShouldContain("The first issue arrives Monday 08:00 (TR) / 15:00 (EN), Istanbul time.");
        body.ShouldNotContain("Subscribe above");
        body.ShouldContain("Sign-ups open soon.");
        body.ShouldNotContain("<form ");
    }

    [Fact]
    public async Task An_issue_page_has_a_masthead_a_language_switch_a_jump_link_and_a_sign_up_in_its_language()
    {
        var response = await _http.GetAsync("/weekly/2026-W46/tr");
        var body = await BodyAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("<article class=\"weekly weekly-issue\" lang=\"tr\">");
        body.ShouldContain("aria-label=\"Konum\"");
        body.ShouldContain("<span aria-current=\"page\">46. hafta</span>");
        body.ShouldContain("Sayı 46</p>");
        body.ShouldContain(">46. hafta</h1>");
        body.ShouldContain("<time datetime=\"2026-11-09\">9 Kasım 2026</time>");
        body.ShouldContain("1 haber");
        body.ShouldContain("~1 dk");
        body.ShouldContain("<span aria-hidden=\"true\">·</span>");
        body.ShouldContain("lang=\"tr\" hreflang=\"tr\" aria-current=\"page\">Türkçe</a>");
        body.ShouldContain("href=\"/weekly/2026-W46/en\" lang=\"en\" hreflang=\"en\">English</a>");
        body.ShouldContain("href=\"#weekly-subscribe\"");
        body.ShouldContain("<link rel=\"canonical\" href=\"https://");
        body.ShouldContain("Her pazartesi e-postanda");
        body.ShouldContain("E-posta adresin");
        body.ShouldContain("placeholder=\"ornek@eposta.com\"");
        // The page language's button is filled; the other one is outlined.
        Regex.IsMatch(body, "class=\"weekly-button\">Türkçe al</button>").ShouldBeTrue();
        Regex.IsMatch(body, "class=\"weekly-button weekly-button-outline\">İngilizce al</button>").ShouldBeTrue();
    }

    [Fact]
    public async Task The_english_issue_page_has_an_english_sign_up_with_the_english_button_filled()
    {
        var body = await BodyAsync(await _http.GetAsync("/weekly/2026-W46/en"));

        body.ShouldContain("Issue 46</p>");
        body.ShouldContain("aria-label=\"Breadcrumb\"");
        body.ShouldContain("Get it every Monday");
        Regex.IsMatch(body, "class=\"weekly-button\">Get it in English</button>").ShouldBeTrue();
        Regex.IsMatch(body, "class=\"weekly-button weekly-button-outline\">Get it in Turkish</button>").ShouldBeTrue();
    }

    [Fact]
    public async Task The_header_marks_the_current_section()
    {
        var body = await BodyAsync(await _http.GetAsync("/weekly"));

        body.ShouldContain("<a href=\"/weekly\" aria-current=\"page\">Weekly</a>");
        body.ShouldNotContain("<a href=\"/about\" aria-current");
    }

    [Theory]
    [InlineData("/weekly/2026-w46/tr", HttpStatusCode.MovedPermanently, "/weekly/2026-W46/tr")]
    [InlineData("/weekly/2026-W46/TR", HttpStatusCode.MovedPermanently, "/weekly/2026-W46/tr")]
    [InlineData("/weekly/2026-W46", HttpStatusCode.Found, "/weekly/2026-W46/tr")]
    [InlineData("/weekly/2026-w46", HttpStatusCode.Found, "/weekly/2026-W46/tr")]
    public async Task Non_canonical_issue_addresses_redirect(string path, HttpStatusCode status, string location)
    {
        var response = await _noRedirect.GetAsync(path);

        response.StatusCode.ShouldBe(status);
        response.Headers.Location!.ToString().ShouldEndWith(location);
    }

    [Theory]
    [InlineData("/weekly/2099-W01/tr")]
    [InlineData("/weekly/2026-W46/de")]
    [InlineData("/weekly/2099-W01")]
    [InlineData("/weekly/nonsense")]
    public async Task Unsent_or_unknown_issues_are_404(string path)
    {
        (await _noRedirect.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_missing_issue_404_renders_a_real_page_in_its_language_with_the_site_chrome()
    {
        var response = await _http.GetAsync("/weekly/2099-W01/tr");
        var body = await BodyAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain("<h1>Bu sayı bulunamadı</h1>");
        body.ShouldContain("Adres yanlış olabilir ya da sayı henüz yayımlanmadı.");
        body.ShouldContain("href=\"/weekly\">Tüm sayılara göz at</a>");
        body.ShouldNotContain("We couldn't find this issue");
        body.ShouldContain("<title>Issue not found - TheBluesland</title>");
        body.ShouldContain("class=\"site-header\"");
        body.ShouldContain("class=\"site-footer\"");
        body.ShouldContain("<meta name=\"robots\" content=\"noindex\"");
    }

    [Fact]
    public async Task A_missing_issue_with_an_unknown_language_shows_both_languages()
    {
        var response = await _http.GetAsync("/weekly/2026-W46/de");
        var body = await BodyAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain("<h1>We couldn't find this issue</h1>");
        body.ShouldContain("<h1>Bu sayı bulunamadı</h1>");
        body.ShouldContain("href=\"/weekly\">Browse all issues</a>");
    }

    [Theory]
    [InlineData("/no-such-page")]
    [InlineData("/playlists/no-such-playlist-slug")]
    [InlineData("/collections/no-such-collection")]
    public async Task Any_404_renders_a_page_with_a_heading_and_a_way_home(string path)
    {
        var response = await _http.GetAsync(path);
        var body = await BodyAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain("class=\"site-header\"");
        body.ShouldContain("<h1>Page not found</h1>");
        body.ShouldContain("<title>Page not found - TheBluesland</title>");
        body.ShouldContain("<a href=\"/\">browse the playlists</a>");
        body.ShouldContain("class=\"site-footer\"");
    }

    [Theory]
    [InlineData("tr", "Neredeyse tamam", "Kit'ten gelen e-postadaki onay bağlantısına tıkla")]
    [InlineData("en", "Almost there", "click the confirmation link")]
    [InlineData(null, "Almost there", "click the confirmation link")]
    public async Task The_thanks_page_asks_for_the_confirmation_click_and_is_not_indexed(string? language, string heading, string text)
    {
        var response = await _http.GetAsync(language is null ? "/weekly/thanks" : $"/weekly/thanks?lang={language}");
        var body = await BodyAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain($"<h1>{heading}</h1>");
        body.ShouldContain(text);
        body.ShouldContain("href=\"/weekly\"");
        body.ShouldContain("<meta name=\"robots\" content=\"noindex\"");
        body.ShouldNotContain("rel=\"canonical\"");
    }

    public async Task InitializeAsync()
    {
        var (http, app) = await StartAsync(builder =>
        {
            builder.Configuration[WeeklyIssueRepository.ContentDirectoryConfigKey] = Fixtures;
            builder.Configuration["Weekly:KitFormIdTr"] = "123";
            builder.Configuration["Weekly:KitFormIdEn"] = "456";
        });
        _http = http;
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _noRedirect = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
        _clients.Add(_noRedirect);
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        foreach (var app in _apps)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private async Task<(HttpClient Http, WebApplication App)> StartAsync(Action<WebApplicationBuilder> configure)
    {
        var app = WebHostFactory.Create([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists");
            configure(builder);
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] =
                "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=thebluesland;Timeout=2";
        });
        await app.StartAsync();
        _apps.Add(app);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var http = new HttpClient { BaseAddress = new Uri(address) };
        _clients.Add(http);
        return (http, app);
    }

    // Razor entity-encodes non-ASCII text (&#xFC;), so decode before comparing Turkish copy.
    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [WeeklyIssueRepository.ContentDirectoryConfigKey] = Fixtures }).Build();
}
