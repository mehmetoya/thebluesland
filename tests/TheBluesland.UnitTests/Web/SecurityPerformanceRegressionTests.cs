using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Shouldly;
using TheBluesland.Web;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

public sealed class SecurityPerformanceRegressionTests
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Username=test;Password=test;Database=test;Timeout=1";
    private const string PublicOrigin = "https://catalogue.example";

    [Theory]
    [InlineData("/playlists/dear-mr-fantasy")]
    [InlineData("/playlists/dear-mr-fantasy/og-image.png")]
    public async Task Draft_content_is_not_served(string path)
    {
        await using var app = CreateApp(Fixture("content-playlists"));
        await app.StartAsync();
        using var client = CreateClient(app);
        using var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unrecognized_host_is_rejected()
    {
        await using var app = CreateApp(Fixture("content-playlists"));
        await app.StartAsync();
        using var client = CreateClient(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/robots.txt");
        request.Headers.Host = "attacker.invalid";
        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Forwarded_headers_cannot_change_the_public_origin()
    {
        await using var app = CreateApp(Fixture("content-playlists"));
        await app.StartAsync();
        using var client = CreateClient(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/robots.txt");
        request.Headers.Add("X-Forwarded-Host", "attacker.invalid");
        request.Headers.Add("X-Forwarded-Proto", "http");
        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain($"Sitemap: {PublicOrigin}/sitemap.xml");
    }

    [Theory]
    [InlineData("content-validation/valid-occasion-dancing", HttpStatusCode.OK)]
    [InlineData("missing-content-directory", HttpStatusCode.ServiceUnavailable)]
    [InlineData("content-validation/malformed-yaml", HttpStatusCode.ServiceUnavailable)]
    [InlineData("content-validation/draft-missing-publishedat", HttpStatusCode.ServiceUnavailable)]
    [InlineData("content-validation/unapproved-genre", HttpStatusCode.ServiceUnavailable)]
    public async Task Readiness_requires_valid_published_content(string fixture, HttpStatusCode expected)
    {
        await using var app = CreateApp(Fixture(fixture));
        await app.StartAsync();
        using var client = CreateClient(app);
        using var response = await client.GetAsync("/health/ready");
        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task Readiness_rejects_an_empty_directory()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            await using var app = CreateApp(directory.FullName);
            await app.StartAsync();
            using var client = CreateClient(app);
            using var response = await client.GetAsync("/health/ready");
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData("/og-image.png")]
    [InlineData("/playlists/masterpieces-of-erkin-the-father/og-image.png")]
    public async Task Social_cards_are_cached_even_with_different_query_strings(string path)
    {
        await using var app = CreateApp(Fixture("content-playlists"));
        await app.StartAsync();
        using var client = CreateClient(app);
        using var first = await client.GetAsync(path + "?cache=first");
        using var second = await client.GetAsync(path + "?cache=second");
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.Headers.Age.ShouldNotBeNull();
        (await second.Content.ReadAsByteArrayAsync()).ShouldBe(await first.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Catalogue_is_reused_until_restart_and_draft_aliases_are_hidden()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var source = File.ReadAllText(Path.Combine(Fixture("content-playlists"), "draft-with-id.md"));
            source = source.Replace("status: draft", "status: draft\npreviousSlugs: [old-draft]");
            var file = Path.Combine(directory.FullName, "draft.md");
            await File.WriteAllTextAsync(file, source);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PlaylistContentRepository.ContentDirectoryConfigKey] = directory.FullName,
            }).Build();
            var repository = new PlaylistContentRepository(configuration, new PlaylistContentReader());
            var snapshots = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => repository.LoadAllAsync(default)));
            snapshots.ShouldAllBe(snapshot => ReferenceEquals(snapshot, snapshots[0]));
            (await repository.FindByPreviousSlugAsync("old-draft", default)).ShouldBeNull();
            await File.WriteAllTextAsync(file, source.Replace("status: draft", "status: published"));
            (await repository.FindBySlugAsync("dear-mr-fantasy", default)).ShouldBeNull();
            var restarted = new PlaylistContentRepository(configuration, new PlaylistContentReader());
            (await restarted.FindBySlugAsync("dear-mr-fantasy", default)).ShouldNotBeNull();
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Large_page_numbers_show_the_full_catalogue_without_overflow()
    {
        var all = await new PlaylistContentReader().ReadAllAsync(Fixture("content-playlists"), default);
        PlaylistCataloguePage.Take(all, int.MaxValue).Count.ShouldBe(all.Count);
        PlaylistCataloguePage.HasMore(all, int.MaxValue).ShouldBeFalse();
        PlaylistCataloguePage.NextPage(int.MaxValue).ShouldBe(int.MaxValue);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static HttpClient CreateClient(WebApplication app) => new() { BaseAddress = new Uri(app.Urls.Single()) };

    private static WebApplication CreateApp(string directory) => WebHostFactory.Create([], builder =>
    {
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration[PlaylistContentRepository.ContentDirectoryConfigKey] = directory;
        builder.Configuration[$"ConnectionStrings:{WebHostFactory.ConnectionStringName}"] = UnreachableConnectionString;
        builder.Configuration[TheBluesland.Web.Seo.SiteUrl.PublicOriginConfigKey] = PublicOrigin;
    });
}
