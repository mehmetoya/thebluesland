using Shouldly;
using TheBluesland.Web.Content;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// US-007: exercises the exact code path <c>Program.cs</c> calls for the
/// <c>dotnet run --project src/TheBluesland.Web -- validate-content</c> CI invocation
/// (spec section 18.1, item 3). Reuses US-006's fixture directories under
/// Fixtures/content-validation rather than mutating the real content/playlists directory, per the
/// acceptance criteria: a violating file must fail with a visible file/field/rule line, a valid
/// file must pass.
/// </summary>
public sealed class ContentValidationCliTests
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "content-validation");

    [Fact]
    public async Task RunAsync_returns_zero_and_reports_pass_for_a_fully_valid_directory()
    {
        var output = new StringWriter();

        var exitCode = await ContentValidationCli.RunAsync(Path.Combine(FixturesRoot, "valid"), output, CancellationToken.None);

        exitCode.ShouldBe(0);
        output.ToString().ShouldContain("Content validation passed");
    }

    [Fact]
    public async Task RunAsync_returns_nonzero_and_prints_file_field_and_rule_for_an_invalid_directory()
    {
        var output = new StringWriter();

        var exitCode = await ContentValidationCli.RunAsync(Path.Combine(FixturesRoot, "bad-spotify-id"), output, CancellationToken.None);

        exitCode.ShouldBe(1);
        // Acceptance criterion 3: file name + field name + the rule that was broken must be
        // visible in the log line for each issue.
        output.ToString().ShouldContain("file.md: spotifyPlaylistId: spotifyPlaylistId must be exactly 22 base62 characters");
    }

    [Fact]
    public async Task RunAsync_reports_every_issue_when_a_directory_has_more_than_one()
    {
        var output = new StringWriter();

        var exitCode = await ContentValidationCli.RunAsync(Path.Combine(FixturesRoot, "duplicate-slug"), output, CancellationToken.None);

        exitCode.ShouldBe(1);
        var text = output.ToString();
        text.ShouldContain("file-a.md: slug:");
        text.ShouldContain("file-b.md: slug:");
    }

    [Fact]
    public async Task RunAsync_passes_for_the_real_repository_content_playlists_directory()
    {
        // Smoke test: exercises the mechanism against the actual content this repo currently
        // ships, read-only (no mutation), so a future PR that breaks real content fails this same
        // check the CI job runs.
        var output = new StringWriter();
        var contentDirectory = Path.Combine(FindRepositoryRoot(), "content", "playlists");

        var exitCode = await ContentValidationCli.RunAsync(contentDirectory, output, CancellationToken.None);

        exitCode.ShouldBe(0, output.ToString());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TheBluesland.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Could not locate TheBluesland.slnx above '{AppContext.BaseDirectory}'.");
    }
}
