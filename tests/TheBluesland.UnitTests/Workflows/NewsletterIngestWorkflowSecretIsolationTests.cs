using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace TheBluesland.UnitTests.Workflows;

/// <summary>
/// docs/specs/weekly-newsletter-news-ingest.md, Design sections 5-6: the newsletter connection
/// string lives only in newsletter workflows, and the ingest workflow holds no other secret.
/// Scans every file in .github/workflows, so a future workflow cannot quietly start using it.
/// </summary>
public sealed partial class NewsletterIngestWorkflowSecretIsolationTests
{
    private const string NewsletterSecret = "NEON_NEWSLETTER_CONNECTION_STRING";
    private const string IngestWorkflow = ".github/workflows/newsletter-ingest.yml";
    private const string PreviewWorkflow = ".github/workflows/newsletter-preview.yml";
    private const string DraftWorkflow = ".github/workflows/newsletter-draft.yml";

    [Fact]
    public void Only_newsletter_workflows_reference_the_newsletter_connection_string()
    {
        var offenders = Directory.GetFiles(WorkflowsDirectory(), "*.yml")
            .Concat(Directory.GetFiles(WorkflowsDirectory(), "*.yaml"))
            .Where(path => !Path.GetFileName(path).StartsWith("newsletter-", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains($"secrets.{NewsletterSecret}", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(IngestWorkflow)]
    [InlineData(PreviewWorkflow)]
    public void Newsletter_workflows_reference_no_secret_other_than_their_own(string workflow)
    {
        var yaml = File.ReadAllText(FindRepoFile(workflow));

        var secrets = SecretReference().Matches(yaml).Select(match => match.Groups[1].Value).Distinct().ToList();

        secrets.ShouldBe([NewsletterSecret]);
    }

    [Fact]
    public void IngestWorkflow_runs_every_two_hours_and_on_demand_with_read_only_contents()
    {
        var yaml = File.ReadAllText(FindRepoFile(IngestWorkflow));

        yaml.ShouldContain("cron: \"17 */2 * * *\"");
        yaml.ShouldContain("workflow_dispatch:");
        yaml.ShouldContain("contents: read");
        yaml.ShouldNotContain("contents: write");
        yaml.ShouldContain("group: newsletter-db");
    }

    [Fact]
    public void IngestWorkflow_fails_the_step_when_the_tool_fails_despite_the_tee()
    {
        var yaml = File.ReadAllText(FindRepoFile(IngestWorkflow));

        yaml.ShouldContain("set -euo pipefail");
    }

    [Fact]
    public void PreviewWorkflow_is_dispatch_only_read_only_and_never_interpolates_inputs_into_the_script()
    {
        var yaml = File.ReadAllText(FindRepoFile(PreviewWorkflow));

        yaml.ShouldContain("workflow_dispatch:");
        yaml.ShouldNotContain("schedule:");
        yaml.ShouldNotContain("pull_request");
        yaml.ShouldContain("contents: read");
        yaml.ShouldNotContain("contents: write");
        yaml.ShouldContain("set -euo pipefail");
        var script = yaml[yaml.IndexOf("run: |", StringComparison.Ordinal)..];
        script.ShouldNotContain("${{");
    }

    [Fact]
    public void DraftWorkflow_holds_only_the_newsletter_and_gemini_secrets_on_one_step_and_is_dispatch_only_and_read_only()
    {
        var yaml = File.ReadAllText(FindRepoFile(DraftWorkflow));

        SecretReference().Matches(yaml).Select(match => match.Groups[1].Value).Distinct().Order().ShouldBe(["GEMINI_API_KEY", NewsletterSecret]);
        SecretReference().Matches(yaml).Count.ShouldBe(2);
        yaml.ShouldContain("workflow_dispatch:");
        yaml.ShouldNotContain("schedule:");
        yaml.ShouldNotContain("pull_request");
        yaml.ShouldContain("contents: read");
        yaml.ShouldNotContain("contents: write");
        yaml.ShouldNotContain("pull-requests: write");
        yaml.ShouldContain("set -euo pipefail");
        var script = yaml[yaml.IndexOf("run: |", StringComparison.Ordinal)..yaml.IndexOf("- name: Upload draft", StringComparison.Ordinal)];
        script.ShouldNotContain("${{");
    }

    [GeneratedRegex(@"secrets\.([A-Z0-9_]+)")]
    private static partial Regex SecretReference();

    private static string WorkflowsDirectory() => Path.GetDirectoryName(FindRepoFile(IngestWorkflow))!;

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
