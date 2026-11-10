using System.Text.RegularExpressions;
using Shouldly;
using TheBluesland.UnitTests.WeeklyNewsletter.Clustering;
using Xunit;

namespace TheBluesland.UnitTests.Workflows;

/// <summary>
/// docs/specs/weekly-newsletter-issue-review.md, Design sections 1-3, and ADR-0006 madde 5: the AI
/// key and the right to write the repository never share a job; the bot writes only a newsletter/
/// branch, only as a draft PR, and never approves or merges; CI runs when Mehmet marks it ready.
/// </summary>
public sealed partial class NewsletterDraftWorkflowTests
{
    private static readonly string Yaml = File.ReadAllText(RepoFiles.Find(".github/workflows/newsletter-draft.yml"));
    private static readonly string Header = Yaml[..Yaml.IndexOf("\njobs:", StringComparison.Ordinal)];
    private static readonly string DraftJob = Between(Yaml, "\n  draft:\n", "\n  open-pr:\n");
    private static readonly string OpenPrJob = Yaml[Yaml.IndexOf("\n  open-pr:\n", StringComparison.Ordinal)..];

    [Fact]
    public void Runs_on_sunday_11_15_istanbul_after_the_gemini_quota_reset_with_a_safety_retry_and_on_demand_one_run_at_a_time()
    {
        // Gemini's free quota resets at 00:00 Pacific = 08:00 UTC; both crons must fall after it.
        Header.ShouldContain("cron: \"15 8 * * 0\"");
        Header.ShouldContain("cron: \"0 11 * * 0\"");
        Header.ShouldNotContain("cron: \"0 7 * * 0\"");
        Header.ShouldNotContain("cron: \"0 10 * * 0\"");
        Header.ShouldContain("workflow_dispatch:");
        Header.ShouldContain("open_pr:");
        Header.ShouldContain("group: newsletter-draft");
        Header.ShouldNotContain("pull_request");
    }

    [Fact]
    public void The_workflow_default_is_read_only_and_only_open_pr_may_write()
    {
        Between(Header, "\npermissions:", "\nconcurrency:").Trim().ShouldBe("permissions:\n  contents: read");
        // The draft job (which holds the AI key) may only read; pull-requests: read lets Plan see the week's PR.
        Between(DraftJob, "permissions:", "timeout-minutes:").ShouldContain("contents: read\n      pull-requests: read");
        DraftJob.ShouldNotContain("write");
        OpenPrJob.ShouldContain("permissions:\n      contents: write\n      pull-requests: write\n");
    }

    [Fact]
    public void The_ai_key_and_database_secret_live_only_on_the_draft_step_and_open_pr_has_no_secret()
    {
        var draftStep = Between(DraftJob, "- name: Draft\n", "- name: Upload draft");

        SecretReference().Matches(Yaml).Select(match => match.Groups[1].Value).Order().ShouldBe(["GEMINI_API_KEY", "NEON_NEWSLETTER_CONNECTION_STRING"]);
        SecretReference().Matches(draftStep).Count.ShouldBe(2);
        OpenPrJob.ShouldNotContain("secrets.");
    }

    [Fact]
    public void The_bot_pushes_only_a_checked_newsletter_branch_and_only_opens_a_draft_pr()
    {
        OpenPrJob.ShouldContain("if ! [[ \"$WEEK\" =~ ^[0-9]{4}-W[0-9]{2}$ ]]; then");
        OpenPrJob.ShouldContain("branch=\"newsletter/$WEEK\"");
        GitPush().Matches(Yaml).Select(match => match.Value.Trim()).ShouldBe(["git push origin \"HEAD:refs/heads/$branch\""]);
        OpenPrJob.ShouldContain("gh pr create --draft --base main");
        OpenPrJob.ShouldContain("validate-weekly");
        Yaml.ShouldNotContain("gh pr merge");
        Yaml.ShouldNotContain("gh pr review");
        Yaml.ShouldNotContain("--approve");
        Yaml.ShouldNotContain("git push --force");
    }

    [Fact]
    public void The_draft_step_ingests_first_and_an_ingest_failure_only_warns()
    {
        var step = Between(DraftJob, "- name: Draft\n", "- name: Upload draft");

        step.ShouldContain("ingest_status=0");
        step.ShouldContain("-- ingest | tee -a \"$GITHUB_STEP_SUMMARY\" || ingest_status=$?");
        step.IndexOf("-- ingest |", StringComparison.Ordinal).ShouldBeLessThan(step.IndexOf("\"${args[@]}\"", StringComparison.Ordinal));
        step.ShouldContain("::warning title=Ingest failed before drafting::");
        step.IndexOf("::warning title=Ingest failed", StringComparison.Ordinal).ShouldBeGreaterThan(step.IndexOf("echo \"::${stop_token}::\"", StringComparison.Ordinal));
        step.ShouldContain("exit \"$draft_status\"");
    }

    [Fact]
    public void A_week_is_skipped_only_when_a_pr_exists_or_issue_files_are_on_main_and_a_prless_branch_gets_its_pr()
    {
        var plan = Between(DraftJob, "- name: Plan\n", "- name: Draft\n");
        plan.ShouldContain("gh pr list --head \"newsletter/$week\" --state all");
        plan.ShouldNotContain("ls-remote");
        plan.ShouldContain("[ \"$pr_count\" != \"0\" ] || [ -e \"content/weekly/$week.tr.md\" ]");

        OpenPrJob.ShouldContain("gh pr list --head \"$branch\" --state all");
        OpenPrJob.ShouldContain("git switch -c \"$branch\" FETCH_HEAD");
        OpenPrJob.IndexOf("gh pr list --head", StringComparison.Ordinal).ShouldBeLessThan(OpenPrJob.IndexOf("gh pr create --draft", StringComparison.Ordinal));
        OpenPrJob.ShouldNotContain("--force");
    }

    [Fact]
    public void Only_the_issue_files_are_committed_never_backups_or_the_pr_body()
    {
        OpenPrJob.ShouldContain("git add \"content/weekly/$WEEK.tr.md\" \"content/weekly/$WEEK.en.md\"");
        OpenPrJob.ShouldNotContain("git add .");
        OpenPrJob.ShouldNotContain("git add -A");
        OpenPrJob.ShouldContain("--body-file \"$body\"");
    }

    [Fact]
    public void Inputs_and_model_output_never_reach_a_script_by_interpolation()
    {
        var scripts = RunBlocks(Yaml).ToList();

        scripts.Count.ShouldBe(3);
        scripts.ShouldAllBe(script => !script.Contains("${{", StringComparison.Ordinal));
        scripts.ShouldAllBe(script => script.Contains("set -euo pipefail", StringComparison.Ordinal));
    }

    [Fact]
    public void Ci_runs_when_mehmet_marks_the_bot_pr_ready_and_validates_weekly_issues()
    {
        var ci = File.ReadAllText(RepoFiles.Find(".github/workflows/ci.yml"));

        ci.ShouldContain("types: [opened, synchronize, reopened, ready_for_review]");
        ci.ShouldContain("-- validate-weekly ${{ github.workspace }}/content/weekly");
    }

    /// <summary>The body of every multi-line <c>run: |</c> block.</summary>
    private static IEnumerable<string> RunBlocks(string yaml)
    {
        var lines = yaml.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimEnd().EndsWith("run: |", StringComparison.Ordinal))
            {
                continue;
            }

            var indent = lines[i].Length - lines[i].TrimStart().Length;
            var block = lines.Skip(i + 1).TakeWhile(line => line.Trim().Length == 0 || line.Length - line.TrimStart().Length > indent);
            yield return string.Join('\n', block);
        }
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        return text[from..text.IndexOf(end, from, StringComparison.Ordinal)];
    }

    [GeneratedRegex(@"secrets\.([A-Z0-9_]+)")]
    private static partial Regex SecretReference();

    [GeneratedRegex(@"git push[^\n]*")]
    private static partial Regex GitPush();
}
