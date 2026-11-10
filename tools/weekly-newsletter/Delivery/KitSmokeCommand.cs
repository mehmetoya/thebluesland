using System.Globalization;
using TheBluesland.WeeklyNewsletter.Reporting;

namespace TheBluesland.WeeklyNewsletter.Delivery;

/// <summary>
/// The <c>kit-smoke</c> subcommand: proves the Kit create/read/delete path before the first real send.
/// It creates a draft (no <c>send_at</c>, so Kit can never send it) for the weekly-tr tag under the
/// description <c>thebluesland-weekly-smoke:&lt;utc timestamp&gt;</c> (not a delivery marker, so the
/// planner ignores it), reads it back, deletes it, and prints what happened plus the account's
/// non-secret facts. The account's primary (login) e-mail is printed masked; it is not the broadcast sender, which Kit picks from the sender addresses set in its UI.
/// Exit 0 only when all three steps worked.
/// </summary>
public static class KitSmokeCommand
{
    public const string DescriptionPrefix = "thebluesland-weekly-smoke:";

    public static async Task<int> RunAsync(
        string toolDirectory,
        string? apiKey,
        HttpClient httpClient,
        TimeProvider clock,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken,
        TimeSpan? retryDelay = null)
    {
        var loaded = await DeliveryOptionsLoader.LoadAsync(Path.Combine(toolDirectory, "delivery.yml"), cancellationToken);
        var problems = loaded.Errors.ToList();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            problems.Add($"{KitClient.ApiKeyVariable} is not set.");
        }

        if (problems.Count > 0)
        {
            await error.WriteLineAsync("Cannot run the Kit smoke test:");
            foreach (var problem in problems)
            {
                await error.WriteLineAsync($"  - {problem}");
            }

            return 1;
        }

        var tagId = loaded.Options!.Languages["tr"].KitTagId;
        var description = $"{DescriptionPrefix}{clock.GetUtcNow().UtcDateTime:yyyyMMddTHHmmssZ}";
        var request = new KitBroadcastRequest(
            description,
            new IssueEmail("TBL Weekly smoke test", "Smoke test. Never sent.", "<p>Smoke test. This draft is deleted right away and is never sent.</p>"),
            SendAt: null,
            tagId,
            Public: false);

        var kit = new KitClient(httpClient, apiKey!, retryDelay);
        var rows = new List<(string Step, bool Ok, string Detail)>();
        long? id = null;
        try
        {
            id = await kit.CreateAsync(request, cancellationToken);
            rows.Add(("create", true, $"broadcast {id.Value.ToString(CultureInfo.InvariantCulture)} (draft, no send_at, public false)"));

            var read = await kit.GetAsync(id.Value, cancellationToken);
            var wrong = new List<string>();
            if (read.Id != id)
            {
                wrong.Add($"id {read.Id}");
            }

            if (read.Status != "draft")
            {
                wrong.Add($"status '{read.Status}' (expected draft)");
            }

            if (read.SendAt is not null)
            {
                wrong.Add("a send_at is set");
            }

            if (read.TagId != tagId)
            {
                wrong.Add($"tag filter {read.TagId?.ToString(CultureInfo.InvariantCulture) ?? "none"} (expected {tagId})");
            }

            if (read.Description?.Trim() != description)
            {
                wrong.Add("description differs");
            }

            rows.Add(("read", wrong.Count == 0, wrong.Count == 0
                ? $"id matches, status draft, no send_at, tag filter {tagId}"
                : $"unexpected: {string.Join("; ", wrong)}"));
        }
        catch (Exception ex) when (IsKitFailure(ex, cancellationToken))
        {
            rows.Add((rows.Count == 0 ? "create" : "read", false, MarkdownText.Cell(ex.Message)));
        }

        if (id is { } created)
        {
            try
            {
                await kit.DeleteAsync(created, cancellationToken);
                rows.Add(("delete", true, $"broadcast {created.ToString(CultureInfo.InvariantCulture)} removed"));
            }
            catch (Exception ex) when (IsKitFailure(ex, cancellationToken))
            {
                rows.Add(("delete", false, $"{MarkdownText.Cell(ex.Message)} Delete broadcast {created.ToString(CultureInfo.InvariantCulture)} by hand in Kit."));
            }
        }

        var account = "unavailable";
        try
        {
            var details = await kit.GetAccountAsync(cancellationToken);
            account = string.Join(
                ", ",
                new[]
                {
                    details.Name is { Length: > 0 } name ? $"name {MarkdownText.Cell(name)}" : null,
                    details.Plan is { Length: > 0 } plan ? $"plan {MarkdownText.Cell(plan)}" : null,
                    details.PrimaryEmail is { Length: > 0 } email ? $"account e-mail {MarkdownText.Cell(Mask(email))}" : null,
                }.Where(part => part is not null));
            if (account.Length == 0)
            {
                account = "the API exposed none of name, plan or sender";
            }
        }
        catch (Exception ex) when (IsKitFailure(ex, cancellationToken))
        {
            account = $"unavailable ({MarkdownText.Cell(ex.Message)})";
        }

        var ok = rows.Count == 3 && rows.All(row => row.Ok);
        await output.WriteAsync("## Kit smoke test\n\n| Step | Result | Detail |\n|---|---|---|\n");
        foreach (var (step, stepOk, detail) in rows)
        {
            await output.WriteAsync($"| {step} | {(stepOk ? "✅ ok" : "❌ failed")} | {detail} |\n");
        }

        await output.WriteAsync($"| account | ℹ️ | {account} |\n\n{(ok ? "Kit create, read and delete work; nothing was sent." : "**The smoke test failed.** Nothing was sent (the broadcast is a draft).")}\n");
        if (!ok)
        {
            await error.WriteLineAsync("Kit smoke test failed.");
        }

        return ok ? 0 : 1;
    }

    /// <summary>"mehmet@example.com" becomes "m***@example.com": the domain is what matters when checking the sender.</summary>
    private static string Mask(string email)
    {
        var at = email.LastIndexOf('@');
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }

    private static bool IsKitFailure(Exception ex, CancellationToken cancellationToken) =>
        (ex is KitException or HttpRequestException or System.Text.Json.JsonException || ex is TaskCanceledException)
        && !cancellationToken.IsCancellationRequested;
}
