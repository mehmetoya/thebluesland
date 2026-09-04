using Shouldly;
using Xunit;

namespace TheBluesland.UnitTests.Workflows;

/// <summary>
/// US-014 AC3/SEC-001 regression test: extends the isolation pattern already pinned for ci.yml
/// (<see cref="CiWorkflowSecretIsolationTests"/>) onto deploy.yml. deploy.yml must never gain
/// access to the sync-only secrets - its only secret is RENDER_DEPLOY_HOOK_URL, which carries no
/// Spotify/AI/database credential itself (see deploy.yml's own header comment). Reads the checked-
/// in workflow/blueprint files as plain text, same approach as the sibling test.
/// </summary>
public sealed class DeployWorkflowSecretIsolationTests
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
    public void DeployWorkflow_never_grants_access_to_a_sync_only_secret(string secretName)
    {
        var deployYaml = File.ReadAllText(FindRepoFile(".github/workflows/deploy.yml"));

        deployYaml.ShouldNotContain(SecretExpression(secretName));
    }

    [Fact]
    public void DeployWorkflow_uses_the_render_deploy_hook_secret()
    {
        var deployYaml = File.ReadAllText(FindRepoFile(".github/workflows/deploy.yml"));

        deployYaml.ShouldContain(SecretExpression("RENDER_DEPLOY_HOOK_URL"));
    }

    /// <summary>
    /// SEC-001: render.yaml declares the connection-string env var by name only, with
    /// <c>sync: false</c>, never a literal connection string value that would leak a credential
    /// (or a fake-looking placeholder that later gets copy-pasted as real) into git history.
    /// </summary>
    [Fact]
    public void RenderBlueprint_declares_connection_string_without_a_literal_value()
    {
        var renderYaml = File.ReadAllText(FindRepoFile(".github/render.yaml"));

        renderYaml.ShouldContain("ConnectionStrings__SpotifyPlaylistCache");
        renderYaml.ShouldContain("sync: false");
        renderYaml.ShouldNotContain("Host=");
    }

    /// <summary>
    /// SEC-001: render.yaml must never declare a Spotify or AI provider credential as an env var
    /// - the production web app's only runtime secret is the read-only Neon connection string.
    /// Matches the declaration pattern (<c>key: NAME</c>), not a bare substring, so this stays a
    /// real check even though render.yaml's own header comment legitimately names these
    /// credentials in prose while explaining why they must never appear below as an env var.
    /// </summary>
    [Theory]
    [InlineData("SPOTIFY_CLIENT_ID")]
    [InlineData("SPOTIFY_REFRESH_TOKEN")]
    [InlineData("NEON_SYNC_CONNECTION_STRING")]
    [InlineData("ANTHROPIC_API_KEY")]
    public void RenderBlueprint_never_declares_a_spotify_or_ai_credential(string credentialName)
    {
        var renderYaml = File.ReadAllText(FindRepoFile(".github/render.yaml"));

        renderYaml.ShouldNotContain($"key: {credentialName}");
    }

    /// <summary>
    /// Guards the negative assertions above from passing for the wrong reason: if sync-spotify.yml
    /// ever stopped declaring these three secrets, "deploy.yml doesn't reference them either" would
    /// be true but meaningless.
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
