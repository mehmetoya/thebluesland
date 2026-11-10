using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TheBluesland.WeeklyNewsletter.Ranking;

/// <summary>Reads and validates <c>ranking.yml</c>; every problem reported together, unknown keys are errors.</summary>
public static partial class RankingOptionsLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static async Task<RankingOptionsLoadResult> LoadAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path)
            ? Parse(await File.ReadAllTextAsync(path, cancellationToken))
            : new RankingOptionsLoadResult(null, [$"Ranking configuration file not found: {path}"]);

    public static RankingOptionsLoadResult Parse(string yaml)
    {
        Document? document;
        try
        {
            document = Deserializer.Deserialize<Document?>(yaml);
        }
        catch (YamlException ex)
        {
            return new RankingOptionsLoadResult(null, [$"ranking.yml is not valid YAML: {ex.Message}"]);
        }

        var errors = new List<string>();
        Require(document?.Picks is > 0, "picks must be a positive number.", errors);
        Require(document?.Backups is >= 0, "backups must be zero or a positive number.", errors);
        Require(document?.CoverageWeight is > 0, "coverageWeight must be positive.", errors);
        Require(document?.MaxPerType is > 0, "maxPerType must be a positive number.", errors);
        Require(document?.RelatedThreshold is > 0 and <= 1, "relatedThreshold must be between 0 (exclusive) and 1.", errors);
        Require(document?.GenreSwapMinRatio is >= 0 and <= 1, "genreSwapMinRatio must be between 0 and 1.", errors);

        var genre = (document?.GenreKeywords ?? []).Select(word => word?.Trim() ?? string.Empty).ToList();
        Require(genre.Count > 0, "genreKeywords must list at least one word.", errors);
        foreach (var word in genre.Where(word => !Phrase().IsMatch(word)))
        {
            errors.Add($"genreKeywords: '{word}' must be lowercase letters/digits separated by single spaces.");
        }

        Require(document?.BrandBonus is >= 0, "brandBonus must be zero or positive.", errors);
        var brand = (document?.BrandKeywords ?? []).Select(word => word?.Trim() ?? string.Empty).ToList();
        foreach (var word in brand.Where(word => !Phrase().IsMatch(word)))
        {
            errors.Add($"brandKeywords: '{word}' must be lowercase letters/digits separated by single spaces.");
        }

        var core = (document?.CoreSources ?? []).Select(id => id?.Trim() ?? string.Empty).ToList();
        foreach (var id in core.Where(id => !SourceId().IsMatch(id)))
        {
            errors.Add($"coreSources: '{id}' must be a kebab-case feed id.");
        }

        var types = new List<StoryTypeRule>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entry, index) in (document?.Types ?? []).Select((entry, index) => (entry, index)))
        {
            var label = string.IsNullOrWhiteSpace(entry?.Name) ? $"type #{index + 1}" : $"type '{entry.Name}'";
            if (string.IsNullOrWhiteSpace(entry?.Name) || !names.Add(entry.Name))
            {
                errors.Add($"{label}: a unique name is required.");
                continue;
            }

            var phrases = (entry.Phrases ?? []).Select(phrase => phrase?.Trim() ?? string.Empty).ToList();
            var exclude = (entry.Exclude ?? []).Select(phrase => phrase?.Trim() ?? string.Empty).ToList();
            Require(phrases.Count > 0, $"{label}: phrases must not be empty.", errors);
            foreach (var phrase in phrases.Concat(exclude).Where(phrase => !Phrase().IsMatch(phrase)))
            {
                errors.Add($"{label}: '{phrase}' must be lowercase letters/digits separated by single spaces.");
            }

            Require(entry.Excluded == true || entry.Bonus is >= 0, $"{label}: bonus must be zero or positive (or set excluded: true).", errors);
            types.Add(new StoryTypeRule(entry.Name, entry.Bonus ?? 0, entry.Excluded == true, phrases, exclude));
        }

        Require(types.Count > 0, "types must list at least one story type.", errors);
        Require(!names.Contains(StoryTypeClassifier.DefaultType), $"'{StoryTypeClassifier.DefaultType}' is the built-in fallback type and cannot be configured.", errors);

        return errors.Count > 0
            ? new RankingOptionsLoadResult(null, errors)
            : new RankingOptionsLoadResult(
                new RankingOptions(
                    document!.Picks!.Value,
                    document.Backups!.Value,
                    document.CoverageWeight!.Value,
                    document.MaxPerType!.Value,
                    document.RelatedThreshold!.Value,
                    genre,
                    document.GenreSwapMinRatio!.Value,
                    document.BrandBonus!.Value,
                    brand,
                    core,
                    types),
                []);
    }

    private static void Require(bool condition, string error, List<string> errors)
    {
        if (!condition)
        {
            errors.Add(error);
        }
    }

    [GeneratedRegex("^[a-z0-9]+( [a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Phrase();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceId();

    private sealed class Document
    {
        public int? Picks { get; set; }

        public int? Backups { get; set; }

        public double? CoverageWeight { get; set; }

        public int? MaxPerType { get; set; }

        public double? RelatedThreshold { get; set; }

        public List<string?>? GenreKeywords { get; set; }

        public double? GenreSwapMinRatio { get; set; }

        public double? BrandBonus { get; set; }

        public List<string?>? BrandKeywords { get; set; }

        public List<string?>? CoreSources { get; set; }

        public List<TypeEntry?>? Types { get; set; }
    }

    private sealed class TypeEntry
    {
        public string? Name { get; set; }

        public double? Bonus { get; set; }

        public bool? Excluded { get; set; }

        public List<string?>? Phrases { get; set; }

        public List<string?>? Exclude { get; set; }
    }
}
