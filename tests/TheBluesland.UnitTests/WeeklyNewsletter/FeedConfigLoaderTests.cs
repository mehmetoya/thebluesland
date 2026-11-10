using Shouldly;
using TheBluesland.WeeklyNewsletter.Ingest;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter;

/// <summary>
/// news-ingest spec, Design section 3: feeds.yml is the only place sources are configured, and an
/// invalid file must stop the run before any feed is fetched rather than silently ingesting a
/// partial list.
/// </summary>
public sealed class FeedConfigLoaderTests
{
    private const string ValidYaml = """
        feeds:
          - id: billboard
            name: Billboard
            url: https://www.billboard.com/c/music/music-news/feed/
            enabled: true
          - id: music-business-worldwide
            name: Music Business Worldwide
            url: https://www.musicbusinessworldwide.com/feed/
            enabled: false
        """;

    [Fact]
    public void Parse_valid_yaml_returns_every_feed_with_its_fields()
    {
        var result = FeedConfigLoader.Parse(ValidYaml);

        result.IsValid.ShouldBeTrue();
        result.Feeds.ShouldBe(
        [
            new FeedConfig("billboard", "Billboard", new Uri("https://www.billboard.com/c/music/music-news/feed/"), Enabled: true),
            new FeedConfig("music-business-worldwide", "Music Business Worldwide", new Uri("https://www.musicbusinessworldwide.com/feed/"), Enabled: false),
        ]);
    }

    [Fact]
    public void EnabledFeeds_excludes_feeds_marked_enabled_false()
    {
        var result = FeedConfigLoader.Parse(ValidYaml);

        result.EnabledFeeds.Select(feed => feed.Id).ShouldBe(["billboard"]);
    }

    [Fact]
    public void Parse_feed_without_enabled_field_treats_it_as_enabled()
    {
        var result = FeedConfigLoader.Parse("""
            feeds:
              - id: nme
                name: NME
                url: https://www.nme.com/news/music/feed
            """);

        result.EnabledFeeds.Select(feed => feed.Id).ShouldBe(["nme"]);
    }

    [Theory]
    [InlineData("Billboard")]
    [InlineData("rolling_stone")]
    [InlineData("-nme")]
    [InlineData("nme-")]
    [InlineData("rolling--stone")]
    public void Parse_id_that_is_not_kebab_case_is_rejected(string id)
    {
        var result = FeedConfigLoader.Parse($"""
            feeds:
              - id: "{id}"
                name: Some Feed
                url: https://example.com/feed
            """);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.Contains(id, StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_duplicate_id_is_rejected()
    {
        var result = FeedConfigLoader.Parse("""
            feeds:
              - id: nme
                name: NME
                url: https://www.nme.com/news/music/feed
              - id: nme
                name: NME again
                url: https://www.nme.com/feed
            """);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.Contains("nme", StringComparison.Ordinal) && error.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("http://www.nme.com/news/music/feed")]
    [InlineData("/news/music/feed")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Parse_url_that_is_not_absolute_https_is_rejected(string url)
    {
        var result = FeedConfigLoader.Parse($"""
            feeds:
              - id: nme
                name: NME
                url: "{url}"
            """);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.Contains("nme", StringComparison.Ordinal) && error.Contains("url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_feed_without_name_is_rejected()
    {
        var result = FeedConfigLoader.Parse("""
            feeds:
              - id: nme
                url: https://www.nme.com/news/music/feed
            """);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.Contains("nme", StringComparison.Ordinal) && error.Contains("name", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("feeds: []")]
    [InlineData("feeds:")]
    [InlineData("")]
    public void Parse_empty_feed_list_is_rejected(string yaml)
    {
        var result = FeedConfigLoader.Parse(yaml);

        result.IsValid.ShouldBeFalse();
        result.Feeds.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_malformed_yaml_is_rejected_without_throwing()
    {
        var result = FeedConfigLoader.Parse("feeds:\n  - id: nme\n    name: [unclosed");

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Parse_invalid_file_returns_no_feeds_even_if_some_entries_are_valid()
    {
        var result = FeedConfigLoader.Parse("""
            feeds:
              - id: nme
                name: NME
                url: https://www.nme.com/news/music/feed
              - id: Bad_Id
                name: Bad
                url: https://example.com/feed
            """);

        result.IsValid.ShouldBeFalse();
        result.Feeds.ShouldBeEmpty();
        result.EnabledFeeds.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_misspelled_field_is_rejected_instead_of_silently_ignored()
    {
        var result = FeedConfigLoader.Parse("""
            feeds:
              - id: nme
                name: NME
                url: https://www.nme.com/news/music/feed
                enabeld: false
            """);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.Contains("enabeld", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_file_with_every_feed_disabled_is_rejected()
    {
        var result = FeedConfigLoader.Parse("""
            feeds:
              - id: nme
                name: NME
                url: https://www.nme.com/news/music/feed
                enabled: false
            """);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.Contains("no enabled feed", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_empty_list_item_is_rejected_without_throwing()
    {
        var result = FeedConfigLoader.Parse("""
            feeds:
              -
              - id: nme
                name: NME
                url: https://www.nme.com/news/music/feed
            """);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.Contains("empty list item", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_missing_file_is_rejected_without_throwing()
    {
        var missingPath = Path.Combine(AppContext.BaseDirectory, "does-not-exist", "feeds.yml");

        var result = await FeedConfigLoader.LoadAsync(missingPath, CancellationToken.None);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.Contains(missingPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_repository_feeds_yml_is_valid_and_has_sixteen_enabled_sources()
    {
        var result = await FeedConfigLoader.LoadAsync(FindRepoFile("tools/weekly-newsletter/feeds.yml"), CancellationToken.None);

        result.Errors.ShouldBeEmpty();
        result.EnabledFeeds.Select(feed => feed.Id).ShouldBe(
            [
                "billboard", "pitchfork", "rolling-stone", "nme", "stereogum", "consequence", "music-business-worldwide",
                "american-blues-scene", "blues-rock-review", "ultimate-classic-rock", "louder", "american-songwriter",
                "jazztimes", "guardian-music", "npr-music", "the-quietus",
            ]);
    }

    [Fact]
    public void Repository_feeds_yml_has_a_fixture_for_every_feed()
    {
        var result = FeedConfigLoader.Parse(File.ReadAllText(FindRepoFile("tools/weekly-newsletter/feeds.yml")));
        var fixturesDirectory = Path.GetDirectoryName(FindRepoFile("tests/TheBluesland.UnitTests/Fixtures/newsletter-feeds/billboard.xml"))!;

        foreach (var feed in result.Feeds)
        {
            File.Exists(Path.Combine(fixturesDirectory, $"{feed.Id}.xml")).ShouldBeTrue($"missing fixture for {feed.Id}");
        }
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, relativePath)))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new FileNotFoundException($"Could not locate {relativePath} above {AppContext.BaseDirectory}.")
            : Path.Combine(directory.FullName, relativePath);
    }
}
