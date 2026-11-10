using System.Net;
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

    private WebApplication _app = null!;
    private HttpClient _http = null!;

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
    public void The_body_is_sanitized()
    {
        var html = new WeeklyIssueRepository(Config()).Find("2026-W46", "tr")!.Html;

        html.ShouldContain("<h2>Richard Lloyd hayatını kaybetti</h2>");
        html.ShouldContain("<a href=\"https://pitchfork.com/x\">Pitchfork</a>");
        html.ShouldNotContain("<script>");
        html.ShouldNotContain("javascript:");
    }

    [Fact]
    public async Task The_archive_lists_issues_and_offers_only_configured_kit_forms()
    {
        var response = await _http.GetAsync("/weekly");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("<title>TheBluesland Weekly - TheBluesland</title>");
        body.ShouldContain("href=\"/weekly/2026-W46/tr\"");
        body.ShouldContain("href=\"/weekly/2026-W46/en\"");
        body.ShouldNotContain("2099-W01");
        body.ShouldContain("action=\"https://app.kit.com/forms/123/subscriptions\"");
        body.ShouldContain("name=\"email_address\"");
        body.Split("app.kit.com/forms/").Length.ShouldBe(2);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("form-action 'self' https://app.kit.com");
    }

    [Fact]
    public async Task An_issue_page_renders_and_links_its_other_language()
    {
        var response = await _http.GetAsync("/weekly/2026-W46/tr");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("46. hafta</h1>");
        body.ShouldContain("lang=\"tr\"");
        body.ShouldContain("href=\"/weekly/2026-W46/en\"");
        body.ShouldContain("<link rel=\"canonical\" href=\"https://");
        body.ShouldNotContain("<script>alert");
    }

    [Theory]
    [InlineData("/weekly/2099-W01/tr")]
    [InlineData("/weekly/2026-W46/de")]
    public async Task Unsent_or_unknown_issues_are_404(string path)
    {
        (await _http.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    public async Task InitializeAsync()
    {
        _app = WebHostFactory.Create([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-playlists");
            builder.Configuration[WeeklyIssueRepository.ContentDirectoryConfigKey] = Fixtures;
            builder.Configuration["Weekly:KitFormIdTr"] = "123";
            builder.Configuration["Weekly:KitFormIdEn"] = string.Empty;
            builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] =
                "Host=127.0.0.1;Port=1;Username=postgres;Password=postgres;Database=thebluesland;Timeout=2";
        });
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _http = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [WeeklyIssueRepository.ContentDirectoryConfigKey] = Fixtures }).Build();
}
