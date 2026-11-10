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

    [Fact]
    public void IngestWorkflow_references_no_secret_other_than_its_own()
    {
        var yaml = File.ReadAllText(FindRepoFile(IngestWorkflow));

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
