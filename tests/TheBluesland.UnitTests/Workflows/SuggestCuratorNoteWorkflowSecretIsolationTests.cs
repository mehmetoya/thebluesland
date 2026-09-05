using Shouldly;
using Xunit;

namespace TheBluesland.UnitTests.Workflows;

/// <summary>
/// US-016 AC4/ADR-0005 madde 3: mirrors the isolation pattern already pinned for
/// sync-spotify.yml/ci.yml/deploy.yml onto suggest-curator-note.yml - it must have exactly the two
/// secrets it needs (ANTHROPIC_API_KEY, NEON_READONLY_CONNECTION_STRING) and none of the
/// sync-only ones. Reads the checked-in workflow file as plain text, same approach as the sibling
/// tests.
/// </summary>
public sealed class SuggestCuratorNoteWorkflowSecretIsolationTests
{
    private static readonly string[] SyncOnlySecrets =
    [
        "SPOTIFY_CLIENT_ID",
        "SPOTIFY_REFRESH_TOKEN",
        "NEON_SYNC_CONNECTION_STRING",
    ];

    private static string SecretExpression(string secretName) => $"secrets.{secretName}";

    [Theory]
    [InlineData("SPOTIFY_CLIENT_ID")]
    [InlineData("SPOTIFY_REFRESH_TOKEN")]
    [InlineData("NEON_SYNC_CONNECTION_STRING")]
    public void SuggestCuratorNoteWorkflow_never_grants_access_to_a_sync_only_secret(string secretName)
    {
        var yaml = File.ReadAllText(FindRepoFile(".github/workflows/suggest-curator-note.yml"));

        yaml.ShouldNotContain(SecretExpression(secretName));
    }

    [Fact]
    public void SuggestCuratorNoteWorkflow_declares_its_own_two_secrets()
    {
        var yaml = File.ReadAllText(FindRepoFile(".github/workflows/suggest-curator-note.yml"));

        yaml.ShouldContain(SecretExpression("ANTHROPIC_API_KEY"));
        yaml.ShouldContain(SecretExpression("NEON_READONLY_CONNECTION_STRING"));
    }

    [Fact]
    public void SuggestCuratorNoteWorkflow_only_triggers_on_workflow_dispatch()
    {
        var yaml = File.ReadAllText(FindRepoFile(".github/workflows/suggest-curator-note.yml"));

        yaml.ShouldNotContain("schedule:");
        yaml.ShouldNotContain("pull_request");
        yaml.ShouldNotContain("push:");
    }

    [Theory]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData(".github/workflows/deploy.yml")]
    [InlineData(".github/workflows/sync-spotify.yml")]
    public void OtherWorkflow_never_grants_access_to_the_ai_or_readonly_secret(string relativePath)
    {
        var yaml = File.ReadAllText(FindRepoFile(relativePath));

        yaml.ShouldNotContain(SecretExpression("ANTHROPIC_API_KEY"));
        yaml.ShouldNotContain(SecretExpression("NEON_READONLY_CONNECTION_STRING"));
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
