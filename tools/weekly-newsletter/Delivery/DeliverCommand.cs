using TheBluesland.WeeklyNewsletter.Review;

namespace TheBluesland.WeeklyNewsletter.Delivery;

/// <summary>
/// The <c>deliver</c> subcommand (issue-delivery spec, Design section 6): validated issue files in
/// main → e-mail HTML → Kit's marked broadcasts → plan → create, update or cancel → job summary.
/// Never reads, stores or logs a subscriber. Exit 0 when Kit matches main (late merges included,
/// they are a decision, not an error, but they are announced as a warning); 1 on bad configuration,
/// an invalid issue file, a Kit error or two broadcasts with one marker of which one is already
/// sending or sent. Two unsent duplicates are repaired: the newer is deleted.
/// </summary>
public static class DeliverCommand
{
    public static async Task<int> RunAsync(
        string toolDirectory,
        string contentDirectory,
        string? apiKey,
        bool dryRun,
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

        problems.AddRange(WeeklyIssueValidator.ValidateDirectory(contentDirectory));
        if (problems.Count > 0)
        {
            await error.WriteLineAsync("Cannot deliver; nothing was sent to Kit:");
            foreach (var problem in problems)
            {
                await error.WriteLineAsync($"  - {problem}");
            }

            return 1;
        }

        var options = loaded.Options!;
        var issues = Directory.Exists(contentDirectory)
            ? Directory.GetFiles(contentDirectory)
                .Order(StringComparer.Ordinal)
                .Select(path => WeeklyIssueValidator.Read(Path.GetFileName(path), File.ReadAllText(path))!)
                .Select(issue => new PlannedIssue(
                    issue.Id,
                    issue.Language,
                    options.SendAt(issue.SendDate, issue.Language),
                    IssueEmailRenderer.Render(issue),
                    options.Languages[issue.Language.Code].KitTagId))
                .ToList()
            : [];

        var kit = new KitClient(httpClient, apiKey!, retryDelay);
        var now = clock.GetUtcNow();
        IReadOnlyList<DeliveryAction> actions;
        try
        {
            actions = DeliveryPlanner.Plan(issues, await kit.ListAsync(cancellationToken), now, options.MinimumLead);
        }
        catch (Exception ex) when (IsKitFailure(ex, cancellationToken))
        {
            await error.WriteLineAsync($"Could not read Kit's broadcasts: {ex.Message} Nothing was changed.");
            return 1;
        }

        if (actions.Any(action => action.Kind == DeliveryActionKind.Conflict))
        {
            await output.WriteAsync(DeliveryReportFormatter.ToMarkdown(actions, now, options.TimeZone, dryRun, "duplicate broadcasts in Kit; check the Kit dashboard."));
            return 1;
        }

        var done = new List<DeliveryAction>();
        foreach (var action in actions)
        {
            try
            {
                var created = dryRun ? null : await ApplyAsync(kit, action, options, cancellationToken);
                done.Add(created is { } id ? action with { BroadcastId = id } : action);
            }
            catch (Exception ex) when (IsKitFailure(ex, cancellationToken))
            {
                await output.WriteAsync(DeliveryReportFormatter.ToMarkdown(done, now, options.TimeZone, dryRun, $"{action.Marker}: {ex.Message}"));
                await error.WriteLineAsync($"Kit refused {action.Marker}: {ex.Message}");
                return 1;
            }
        }

        await output.WriteAsync(DeliveryReportFormatter.ToMarkdown(done, now, options.TimeZone, dryRun));
        return 0;
    }

    /// <summary>Carries out one action; returns the broadcast id a create produced.</summary>
    private static async Task<long?> ApplyAsync(KitClient kit, DeliveryAction action, DeliveryOptions options, CancellationToken cancellationToken)
    {
        switch (action.Kind)
        {
            case DeliveryActionKind.Create:
                return await kit.CreateAsync(Request(action, options), cancellationToken);
            case DeliveryActionKind.Update:
                await kit.UpdateAsync(action.BroadcastId!.Value, Request(action, options), action.EmailTemplateId, cancellationToken);
                return null;
            case DeliveryActionKind.Cancel:
            case DeliveryActionKind.DeleteDuplicate:
                await kit.DeleteAsync(action.BroadcastId!.Value, cancellationToken);
                return null;
            default:
                return null;
        }
    }

    private static KitBroadcastRequest Request(DeliveryAction action, DeliveryOptions options) =>
        new(action.Marker, action.Issue!.Email, action.Issue.SendAt, action.Issue.TagId, options.KitPublic);

    private static bool IsKitFailure(Exception ex, CancellationToken cancellationToken) =>
        (ex is KitException or HttpRequestException or System.Text.Json.JsonException || ex is TaskCanceledException)
        && !cancellationToken.IsCancellationRequested;
}
