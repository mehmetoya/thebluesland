using System.Globalization;

namespace TheBluesland.WeeklyNewsletter.Delivery;

/// <summary>
/// The <c>kit-sync-tags</c> subcommand: Kit's free plan has no "form subscribe → add tag" rule, so
/// before each delivery run this tags every confirmed subscriber of a language's sign-up form
/// (delivery.yml <c>kitFormId</c>) with that language's delivery tag. Idempotent; only subscriber
/// ids pass through memory and only counts are printed, never an e-mail address.
/// </summary>
public static class KitSyncTagsCommand
{
    public static async Task<int> RunAsync(
        string toolDirectory,
        string? apiKey,
        bool dryRun,
        HttpClient httpClient,
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
            await error.WriteLineAsync("Cannot sync Kit tags:");
            foreach (var problem in problems)
            {
                await error.WriteLineAsync($"  - {problem}");
            }

            return 1;
        }

        var kit = new KitClient(httpClient, apiKey!, retryDelay);
        var rows = new List<string>();
        try
        {
            foreach (var (code, language) in loaded.Options!.Languages.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (language.KitFormId is not { } formId)
                {
                    rows.Add($"| {code} | – | – | no kitFormId |");
                    continue;
                }

                var tagged = (await kit.ListTagSubscriberIdsAsync(language.KitTagId, cancellationToken)).ToHashSet();
                var missing = (await kit.ListFormSubscriberIdsAsync(formId, cancellationToken)).Where(id => !tagged.Contains(id)).Distinct().ToList();
                if (!dryRun)
                {
                    foreach (var subscriberId in missing)
                    {
                        await kit.TagSubscriberAsync(language.KitTagId, subscriberId, cancellationToken);
                    }
                }

                rows.Add(string.Create(CultureInfo.InvariantCulture, $"| {code} | {formId} | {language.KitTagId} | {missing.Count} {(dryRun ? "to tag" : "tagged")}, {tagged.Count} already |"));
            }
        }
        catch (Exception ex) when (ex is KitException or HttpRequestException or System.Text.Json.JsonException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            await error.WriteLineAsync($"Kit tag sync failed: {ex.Message}");
            return 1;
        }

        await output.WriteAsync($"## Kit form → tag sync{(dryRun ? " (dry run)" : string.Empty)}\n\n| Language | Form | Tag | Subscribers |\n|---|---|---|---|\n");
        foreach (var row in rows)
        {
            await output.WriteAsync(row + "\n");
        }

        await output.WriteAsync("\n");
        return 0;
    }
}
