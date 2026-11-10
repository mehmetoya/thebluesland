using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TheBluesland.WeeklyNewsletter.Ingest;

/// <summary>
/// Reads and validates <c>feeds.yml</c> (news-ingest spec, Design section 3). Every problem in the
/// file is collected and reported together, and any problem makes the whole file invalid: the
/// ingest command then exits non-zero before fetching anything.
/// </summary>
public static partial class FeedConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        // No IgnoreUnmatchedProperties: a typo such as "enabeld: false" must fail the file, not
        // silently leave the feed enabled.
        .Build();

    public static async Task<FeedConfigLoadResult> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return FeedConfigLoadResult.Invalid([$"Feed configuration file not found: {path}"]);
        }

        var yaml = await File.ReadAllTextAsync(path, cancellationToken);
        return Parse(yaml);
    }

    public static FeedConfigLoadResult Parse(string yaml)
    {
        FeedsDocument? document;
        try
        {
            document = Deserializer.Deserialize<FeedsDocument?>(yaml);
        }
        catch (YamlException ex)
        {
            return FeedConfigLoadResult.Invalid([$"feeds.yml is not valid YAML: {ex.Message}"]);
        }

        var entries = document?.Feeds ?? [];
        if (entries.Count == 0)
        {
            return FeedConfigLoadResult.Invalid(["feeds.yml defines no feeds."]);
        }

        var errors = new List<string>();
        var feeds = new List<FeedConfig>(entries.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index] is not { } entry)
            {
                errors.Add($"feed #{index + 1}: empty list item.");
                continue;
            }

            var label = string.IsNullOrWhiteSpace(entry.Id) ? $"feed #{index + 1}" : $"feed '{entry.Id}'";

            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                errors.Add($"{label}: id is required.");
            }
            else if (!KebabCaseId().IsMatch(entry.Id))
            {
                errors.Add($"{label}: id '{entry.Id}' must be kebab-case (lowercase letters, digits, single hyphens).");
            }
            else if (!seenIds.Add(entry.Id))
            {
                errors.Add($"{label}: duplicate id '{entry.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                errors.Add($"{label}: name is required.");
            }

            if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            {
                errors.Add($"{label}: url '{entry.Url}' must be an absolute https URL.");
            }

            if (errors.Count == 0 && url is not null)
            {
                feeds.Add(new FeedConfig(entry.Id!, entry.Name!.Trim(), url, entry.Enabled ?? true));
            }
        }

        if (errors.Count == 0 && !feeds.Any(feed => feed.Enabled))
        {
            errors.Add("feeds.yml has no enabled feed (every entry is enabled: false).");
        }

        return errors.Count == 0 ? new FeedConfigLoadResult(feeds, []) : FeedConfigLoadResult.Invalid(errors);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KebabCaseId();

    private sealed class FeedsDocument
    {
        public List<FeedEntry?>? Feeds { get; set; }
    }

    private sealed class FeedEntry
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Url { get; set; }

        public bool? Enabled { get; set; }
    }
}
