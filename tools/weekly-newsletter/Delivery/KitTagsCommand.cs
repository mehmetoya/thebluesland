using System.Globalization;
using System.Text.RegularExpressions;

namespace TheBluesland.WeeklyNewsletter.Delivery;

/// <summary>
/// The <c>kit-tags</c> subcommand (issue-delivery spec, T0): makes sure the delivery tags exist in
/// Kit and prints their ids for delivery.yml. Idempotent: an existing tag is reused, never renamed.
/// Kit's free plan has no MCP access, so this is the API route for the one setup step code can do.
/// </summary>
public static partial class KitTagsCommand
{
    public static readonly IReadOnlyList<string> DefaultTags = ["weekly-tr", "weekly-en"];

    public static async Task<int> RunAsync(
        IReadOnlyList<string> names,
        string? apiKey,
        HttpClient httpClient,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken,
        TimeSpan? retryDelay = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await error.WriteLineAsync($"{KitClient.ApiKeyVariable} is not set.");
            return 1;
        }

        if (names.FirstOrDefault(name => !TagName().IsMatch(name)) is { } invalid)
        {
            await error.WriteLineAsync($"'{invalid}' is not a tag name (lowercase letters, digits and '-', up to 50).");
            return 1;
        }

        var kit = new KitClient(httpClient, apiKey, retryDelay);
        var rows = new List<(KitTag Tag, bool Created)>();
        try
        {
            var existing = await kit.ListTagsAsync(cancellationToken);
            foreach (var name in names)
            {
                rows.Add(existing.FirstOrDefault(tag => tag.Name == name) is { } found
                    ? (found, false)
                    : (await kit.CreateTagAsync(name, cancellationToken), true));
            }
        }
        catch (Exception ex) when (ex is KitException or HttpRequestException or System.Text.Json.JsonException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            await error.WriteLineAsync($"Kit tag setup failed: {ex.Message}");
            return 1;
        }

        await output.WriteAsync("## Kit tags\n\n| Tag | Kit id | |\n|---|---|---|\n");
        foreach (var (tag, created) in rows)
        {
            await output.WriteAsync(string.Create(CultureInfo.InvariantCulture, $"| {tag.Name} | {tag.Id} | {(created ? "created" : "already existed")} |\n"));
        }

        await output.WriteAsync("\nThese ids go into tools/weekly-newsletter/delivery.yml (kitTagId).\n");
        return 0;
    }

    [GeneratedRegex("^[a-z0-9-]{1,50}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TagName();
}
