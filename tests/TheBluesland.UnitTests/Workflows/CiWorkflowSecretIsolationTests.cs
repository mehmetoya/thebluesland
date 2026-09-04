using Shouldly;
using Xunit;

namespace TheBluesland.UnitTests.Workflows;

/// <summary>
/// US-013 AC3/SEC-001 regression test: pins the secret-scope isolation pattern already established
/// for sync-spotify.yml (see that workflow's own header comment) onto ci.yml too - ci.yml must
/// never gain access to the sync-only secrets, even by accident (e.g. a future `env:` block
/// copy-paste). Reads the checked-in workflow files as plain text rather than re-implementing YAML
/// parsing, matching how direct the property being pinned actually is.
/// </summary>
public sealed class CiWorkflowSecretIsolationTests
{
    private static readonly string[] SyncOnlySecrets =
    [
        "SPOTIFY_CLIENT_ID",
        "SPOTIFY_REFRESH_TOKEN",
        "NEON_SYNC_CONNECTION_STRING",
    ];

    // The GitHub Actions expression form that actually grants a job access to a repository
    // secret - not the bare secret name, which ci.yml's own header comment legitimately mentions
    // in prose (see that comment's SEC-001 note) without granting any access at all.
    private static string SecretExpression(string secretName) => $"secrets.{secretName}";

    [Theory]
    [InlineData("SPOTIFY_CLIENT_ID")]
    [InlineData("SPOTIFY_REFRESH_TOKEN")]
    [InlineData("NEON_SYNC_CONNECTION_STRING")]
    public void CiWorkflow_never_grants_access_to_a_sync_only_secret(string secretName)
    {
        var ciYaml = File.ReadAllText(FindRepoFile(".github/workflows/ci.yml"));

        ciYaml.ShouldNotContain(SecretExpression(secretName));
    }

    /// <summary>
    /// Guards the test above from passing for the wrong reason: if sync-spotify.yml ever stopped
    /// declaring these three secrets, "ci.yml doesn't reference them either" would be true but
    /// meaningless. Pinning that sync-spotify.yml still declares all three keeps this a real
    /// isolation check, not a vacuous one.
    /// </summary>
    [Fact]
    public void SyncWorkflow_still_declares_all_three_sync_only_secrets()
    {
        var syncYaml = File.ReadAllText(FindRepoFile(".github/workflows/sync-spotify.yml"));

        foreach (var secretName in SyncOnlySecrets)
        {
            syncYaml.ShouldContain(SecretExpression(secretName));
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
