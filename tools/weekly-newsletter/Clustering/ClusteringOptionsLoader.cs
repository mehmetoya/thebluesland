using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TheBluesland.WeeklyNewsletter.Clustering;

/// <summary>
/// Reads and validates <c>clustering.yml</c>. Like feeds.yml, every problem is reported together and
/// unknown keys are errors, so a typo cannot silently fall back to a default.
/// </summary>
public static class ClusteringOptionsLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static async Task<ClusteringOptionsLoadResult> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new ClusteringOptionsLoadResult(null, [$"Clustering configuration file not found: {path}"]);
        }

        return Parse(await File.ReadAllTextAsync(path, cancellationToken));
    }

    public static ClusteringOptionsLoadResult Parse(string yaml)
    {
        Document? document;
        try
        {
            document = Deserializer.Deserialize<Document?>(yaml);
        }
        catch (YamlException ex)
        {
            return new ClusteringOptionsLoadResult(null, [$"clustering.yml is not valid YAML: {ex.Message}"]);
        }

        var errors = new List<string>();
        if (document?.SimilarityThreshold is not (> 0 and < 1))
        {
            errors.Add("similarityThreshold must be a number between 0 and 1 (exclusive).");
        }

        if (document?.MaxHoursApart is not > 0)
        {
            errors.Add("maxHoursApart must be a positive number of hours.");
        }

        if (document?.SummaryCharacters is not >= 0)
        {
            errors.Add("summaryCharacters must be zero or a positive number.");
        }

        var abbreviations = new List<Abbreviation>();
        foreach (var (entry, index) in (document?.Abbreviations ?? []).Select((entry, index) => (entry, index)))
        {
            var from = entry?.From?.Trim();
            var to = entry?.To?.Trim();
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            {
                errors.Add($"abbreviation #{index + 1}: both 'from' and 'to' are required.");
            }
            else if (!string.Equals(from, from.ToLower(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                errors.Add($"abbreviation #{index + 1}: 'from' must be lowercase (it is matched after lowercasing).");
            }
            else
            {
                abbreviations.Add(new Abbreviation(from, to));
            }
        }

        return errors.Count > 0
            ? new ClusteringOptionsLoadResult(null, errors)
            : new ClusteringOptionsLoadResult(
                new ClusteringOptions(
                    document!.SimilarityThreshold!.Value,
                    TimeSpan.FromHours(document.MaxHoursApart!.Value),
                    document.SummaryCharacters!.Value,
                    abbreviations),
                []);
    }

    private sealed class Document
    {
        public double? SimilarityThreshold { get; set; }

        public double? MaxHoursApart { get; set; }

        public int? SummaryCharacters { get; set; }

        public List<AbbreviationEntry?>? Abbreviations { get; set; }
    }

    private sealed class AbbreviationEntry
    {
        public string? From { get; set; }

        public string? To { get; set; }
    }
}
