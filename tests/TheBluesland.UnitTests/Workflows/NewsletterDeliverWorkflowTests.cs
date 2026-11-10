using System.Text.RegularExpressions;
using Shouldly;
using TheBluesland.UnitTests.WeeklyNewsletter.Clustering;
using Xunit;

namespace TheBluesland.UnitTests.Workflows;

/// <summary>
/// docs/specs/weekly-newsletter-issue-delivery.md, Design sections 2 and 5: the Kit key lives in
/// one workflow on one step; that workflow holds nothing else, cannot write the repository, and
/// the web app never mentions Kit.
/// </summary>
public sealed partial class NewsletterDeliverWorkflowTests
{
    private const string Workflow = ".github/workflows/newsletter-deliver.yml";
    private static readonly string Yaml = File.ReadAllText(RepoFiles.Find(Workflow));

    [Fact]
    public void Only_the_deliver_workflow_holds_the_kit_key()
    {
        var directory = Path.GetDirectoryName(RepoFiles.Find(Workflow))!;

        Directory.GetFiles(directory, "*.y*ml")
            .Where(path => File.ReadAllText(path).Contains("secrets.KIT_API_KEY", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ShouldBe(["newsletter-deliver.yml"]);
    }

    [Fact]
    public void The_kit_key_is_its_only_secret_and_sits_on_the_deliver_step()
    {
        var step = Yaml[Yaml.IndexOf("- name: Deliver", StringComparison.Ordinal)..];

        SecretReference().Matches(Yaml).Select(match => match.Groups[1].Value).ShouldBe(["KIT_API_KEY"]);
        step.ShouldContain("KIT_API_KEY: ${{ secrets.KIT_API_KEY }}");
    }

    [Fact]
    public void It_runs_on_merges_to_weekly_content_before_each_send_and_on_demand_as_a_dry_run()
    {
        Yaml.ShouldContain("branches: [main]");
        Yaml.ShouldContain("- \"content/weekly/**\"");
        Yaml.ShouldContain("cron: \"30 4 * * 1\"");
        Yaml.ShouldContain("cron: \"30 11 * * 1\"");
        Yaml.ShouldContain("workflow_dispatch:");
        Yaml.ShouldContain("if [ \"$EVENT_NAME\" = \"workflow_dispatch\" ] && [ \"$APPLY_INPUT\" != \"true\" ]; then args+=(--dry-run); fi");
        Yaml.ShouldContain("if [ \"$EVENT_NAME\" = \"workflow_dispatch\" ] && [ \"$TASK_INPUT\" = \"setup-tags\" ]; then args=(kit-tags weekly-tr weekly-en); fi");
        Yaml.ShouldContain("group: newsletter-deliver");
        Yaml.ShouldNotContain("pull_request");
    }

    [Fact]
    public void A_smoke_task_runs_kit_smoke_on_demand_only_and_never_the_tag_sync()
    {
        Yaml.ShouldContain("options: [deliver, setup-tags, smoke]");
        Yaml.ShouldContain("if [ \"$EVENT_NAME\" = \"workflow_dispatch\" ] && [ \"$TASK_INPUT\" = \"smoke\" ]; then args=(kit-smoke); fi");
        // The sync runs only when the task is "deliver".
        Yaml.ShouldContain("if [ \"${args[0]}\" = \"deliver\" ]; then\n            sync=(kit-sync-tags)");
    }

    [Fact]
    public void A_failed_tag_sync_does_not_stop_the_delivery_but_still_ends_the_step_red_with_a_warning()
    {
        var script = Script();

        script.ShouldContain("sync_status=0");
        script.ShouldContain("\"${sync[@]}\" | tee -a \"$GITHUB_STEP_SUMMARY\" || sync_status=$?");
        script.IndexOf("|| sync_status=$?", StringComparison.Ordinal).ShouldBeLessThan(script.IndexOf("\"${args[@]}\"", StringComparison.Ordinal));
        script.ShouldContain("::warning title=Kit tag sync failed::");
        script.ShouldContain("if [ \"$sync_status\" -ne 0 ]; then exit 1; fi");
        script.ShouldContain("if [ \"$deliver_status\" -ne 0 ]; then exit \"$deliver_status\"; fi");
    }

    [Fact]
    public void Warnings_are_raised_only_after_workflow_commands_are_switched_back_on_and_carry_fixed_text()
    {
        var script = Script();
        var resume = script.IndexOf("echo \"::${stop_token}::\"", StringComparison.Ordinal);

        resume.ShouldBeGreaterThan(script.IndexOf("::stop-commands::", StringComparison.Ordinal));
        script.IndexOf("::warning title=Weekly issue not sent::", StringComparison.Ordinal).ShouldBeGreaterThan(resume);
        script.IndexOf("::warning title=Kit tag sync failed::", StringComparison.Ordinal).ShouldBeGreaterThan(resume);
        script.ShouldContain("| tee -a \"$GITHUB_STEP_SUMMARY\" \"$report\" || deliver_status=$?");
        script.ShouldContain("LATE — [a-z]{2} will not be sent this week");
    }

    [Fact]
    public void Scheduled_runs_warm_the_site_up_best_effort_without_any_secret()
    {
        var step = Between(Yaml, "- name: Warm up the site\n", "- name: Deliver");

        step.ShouldContain("if: github.event_name == 'schedule'");
        step.ShouldContain("run: curl -fsS --max-time 60 https://thebluesland.com/health/ready || true");
        step.ShouldNotContain("secrets.");
        step.ShouldNotContain("env:");
    }

    [Fact]
    public void It_is_read_only_and_never_interpolates_into_its_script()
    {
        Yaml.ShouldContain("permissions:\n  contents: read\n");
        Yaml.ShouldNotContain(": write");
        Yaml.ShouldNotContain("git push");
        var script = Yaml[Yaml.IndexOf("run: |", StringComparison.Ordinal)..];
        script.ShouldNotContain("${{");
        script.ShouldContain("set -euo pipefail");
        script.ShouldContain("::stop-commands::");
    }

    [Fact]
    public void The_web_app_never_talks_to_kit()
    {
        var source = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(RepoFiles.Find(Workflow))))!, "src");

        Directory.GetFiles(source, "*.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path) is var text && (text.Contains("KIT_API_KEY", StringComparison.Ordinal) || text.Contains("api.kit.com", StringComparison.Ordinal)))
            .ShouldBeEmpty();
    }

    private static string Script()
    {
        var start = Yaml.IndexOf("run: |", StringComparison.Ordinal);
        return Yaml[start..];
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        return text[from..text.IndexOf(end, from, StringComparison.Ordinal)];
    }

    [GeneratedRegex(@"secrets\.([A-Z0-9_]+)")]
    private static partial Regex SecretReference();
}
