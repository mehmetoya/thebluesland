using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-006: validates content/playlists/*.md front matter against the v0.2 schema and taxonomy
/// (spec section 8-9) - required fields, format/range rules, approved taxonomy values, and
/// slug/spotifyPlaylistId uniqueness across files. Each fixture directory under
/// Fixtures/content-validation isolates exactly one rule so assertions stay unambiguous.
/// </summary>
public sealed class PlaylistContentValidatorTests
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-validation");

    private readonly PlaylistContentValidator _validator = new();

    [Fact]
    public async Task ValidateAllAsync_returns_no_issues_for_a_fully_valid_published_file()
    {
        var result = await ValidateFixtureAsync("valid");

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task ValidateAllAsync_fails_when_schemaVersion_is_missing()
    {
        var result = await ValidateFixtureAsync("missing-schema-version");

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Field == "schemaVersion");
    }

    [Fact]
    public async Task ValidateAllAsync_fails_when_spotifyPlaylistId_is_not_22_base62_characters()
    {
        var result = await ValidateFixtureAsync("bad-spotify-id");

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Field == "spotifyPlaylistId");
    }

    [Fact]
    public async Task ValidateAllAsync_fails_when_title_is_outside_the_3_to_80_character_range()
    {
        var result = await ValidateFixtureAsync("title-too-short");

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Field == "title");
    }

    [Fact]
    public async Task ValidateAllAsync_fails_when_moods_contains_an_unapproved_value()
    {
        var result = await ValidateFixtureAsync("unapproved-mood");

        result.Issues.ShouldContain(issue => issue.Field == "moods" && issue.Message.Contains("bogus-mood"));
    }

    [Fact]
    public async Task ValidateAllAsync_fails_when_genres_contains_an_unapproved_value()
    {
        var result = await ValidateFixtureAsync("unapproved-genre");

        result.Issues.ShouldContain(issue => issue.Field == "genres" && issue.Message.Contains("bogus-genre"));
    }

    [Fact]
    public async Task ValidateAllAsync_fails_when_occasions_contains_an_unapproved_value()
    {
        var result = await ValidateFixtureAsync("unapproved-occasion");

        result.Issues.ShouldContain(issue => issue.Field == "occasions" && issue.Message.Contains("bogus-occasion"));
    }

    [Fact]
    public async Task ValidateAllAsync_fails_when_era_is_an_unapproved_value()
    {
        var result = await ValidateFixtureAsync("unapproved-era");

        result.Issues.ShouldContain(issue => issue.Field == "era" && issue.Message.Contains("bogus-era"));
    }

    [Fact]
    public async Task ValidateAllAsync_fails_both_files_when_two_files_share_the_same_slug()
    {
        var result = await ValidateFixtureAsync("duplicate-slug");

        var slugIssues = result.Issues.Where(issue => issue.Field == "slug").ToList();
        slugIssues.Count.ShouldBe(2);
        slugIssues.Select(issue => issue.FileName).ShouldBe(["file-a.md", "file-b.md"], ignoreOrder: true);
    }

    [Fact]
    public async Task ValidateAllAsync_fails_both_files_when_two_files_share_the_same_spotifyPlaylistId()
    {
        var result = await ValidateFixtureAsync("duplicate-spotify-id");

        var idIssues = result.Issues.Where(issue => issue.Field == "spotifyPlaylistId").ToList();
        idIssues.Count.ShouldBe(2);
        idIssues.Select(issue => issue.FileName).ShouldBe(["file-a.md", "file-b.md"], ignoreOrder: true);
    }

    [Fact]
    public async Task ValidateAllAsync_passes_a_draft_file_that_omits_publishedAt()
    {
        var result = await ValidateFixtureAsync("draft-missing-publishedat");

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task ValidateAllAsync_fails_a_draft_file_that_omits_slug_and_spotifyPlaylistId()
    {
        var result = await ValidateFixtureAsync("draft-missing-slug-and-id");

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Field == "slug");
        result.Issues.ShouldContain(issue => issue.Field == "spotifyPlaylistId");
    }

    [Fact]
    public async Task ValidateAllAsync_fails_a_published_file_with_no_markdown_body()
    {
        var result = await ValidateFixtureAsync("published-missing-body");

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Field == "body");
    }

    [Fact]
    public async Task ValidateAllAsync_passes_when_exactly_four_files_are_featured()
    {
        var result = await ValidateFixtureAsync("featured-within-cap");

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task ValidateAllAsync_fails_every_featured_file_when_a_fifth_file_is_also_featured()
    {
        var result = await ValidateFixtureAsync("featured-over-cap");

        var featuredIssues = result.Issues.Where(issue => issue.Field == "featured").ToList();
        featuredIssues.Count.ShouldBe(5);
        featuredIssues.Select(issue => issue.FileName).ShouldBe(
            ["file-1.md", "file-2.md", "file-3.md", "file-4.md", "file-5.md"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task ValidateAllAsync_does_not_count_a_featured_draft_toward_the_published_featured_cap()
    {
        var result = await ValidateFixtureAsync("featured-draft-not-counted");

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task ValidateAllAsync_fails_when_publishedAt_is_not_a_valid_iso_date()
    {
        var result = await ValidateFixtureAsync("bad-publishedat-format");

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Field == "publishedAt");
    }

    [Fact]
    public async Task ValidateAllAsync_reports_malformed_yaml_as_an_issue_instead_of_throwing()
    {
        var result = await ValidateFixtureAsync("malformed-yaml");

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Field == "frontMatter");
    }

    private Task<PlaylistContentValidationResult> ValidateFixtureAsync(string fixtureName) =>
        _validator.ValidateAllAsync(Path.Combine(FixturesRoot, fixtureName), CancellationToken.None);
}
