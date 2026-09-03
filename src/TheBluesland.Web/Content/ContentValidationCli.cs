namespace TheBluesland.Web.Content;

/// <summary>
/// US-007 (spec section 18.1, item 3): runs <see cref="PlaylistContentValidator"/> against
/// <c>content/playlists</c> and reports the outcome in a CI-friendly, line-per-issue format,
/// without starting the web host. Invoked from <c>Program.cs</c> via a <c>validate-content</c>
/// command-line argument (<c>dotnet run --project src/TheBluesland.Web -- validate-content</c>) -
/// see that file's header comment for why this lives here rather than in a new console project.
/// Extracted out of <c>Program.cs</c> (a top-level-statements file, not directly unit-testable) so
/// this class can be exercised by tests against fixture directories.
/// </summary>
public static class ContentValidationCli
{
    /// <summary>The first argument that switches <c>Program.cs</c> into this CLI mode.</summary>
    public const string CommandArgument = "validate-content";

    /// <summary>
    /// Validates every <c>*.md</c> file under <paramref name="contentDirectory"/> and writes one
    /// line per issue to <paramref name="output"/> as <c>fileName: field: message</c> - the
    /// acceptance-criteria-mandated shape (file name + field name + the rule that was broken).
    /// Returns 0 when valid, 1 when at least one issue was found, so the caller (a CI job) fails
    /// loudly on invalid content.
    /// </summary>
    public static async Task<int> RunAsync(
        string contentDirectory,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var validator = new PlaylistContentValidator();
        var result = await validator.ValidateAllAsync(contentDirectory, cancellationToken);

        if (result.IsValid)
        {
            await output.WriteLineAsync($"Content validation passed: no issues found in '{contentDirectory}'.");
            return 0;
        }

        await output.WriteLineAsync(
            $"Content validation failed: {result.Issues.Count} issue(s) found in '{contentDirectory}'.");
        foreach (var issue in result.Issues)
        {
            await output.WriteLineAsync($"{issue.FileName}: {issue.Field}: {issue.Message}");
        }

        return 1;
    }
}
