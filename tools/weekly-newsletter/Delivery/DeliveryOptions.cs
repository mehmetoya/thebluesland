using System.Globalization;
using TheBluesland.WeeklyNewsletter.Drafting;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TheBluesland.WeeklyNewsletter.Delivery;

/// <summary>
/// One language's list in Kit and the wall-clock time its issue goes out on Monday. Kit's free plan
/// cannot tag form sign-ups itself, so <c>kit-sync-tags</c> copies the form's confirmed subscribers
/// (<see cref="KitFormId"/>) into the tag before each send.
/// </summary>
public sealed record LanguageDelivery(TimeOnly SendTime, long KitTagId, long? KitFormId = null);

/// <summary><c>delivery.yml</c> (issue-delivery spec, Design section 5).</summary>
public sealed record DeliveryOptions(
    TimeZoneInfo TimeZone,
    TimeSpan MinimumLead,
    bool KitPublic,
    IReadOnlyDictionary<string, LanguageDelivery> Languages)
{
    /// <summary>The instant an issue in <paramref name="language"/> goes out on its <paramref name="sendDate"/>.</summary>
    public DateTimeOffset SendAt(DateOnly sendDate, DraftLanguage language)
    {
        var local = sendDate.ToDateTime(Languages[language.Code].SendTime);
        return new DateTimeOffset(local, TimeZone.GetUtcOffset(local));
    }
}

public sealed record DeliveryOptionsLoadResult(DeliveryOptions? Options, IReadOnlyList<string> Errors);

/// <summary>Reads and validates <c>delivery.yml</c>; every problem reported together, unknown keys are errors.</summary>
public static class DeliveryOptionsLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static async Task<DeliveryOptionsLoadResult> LoadAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path)
            ? Parse(await File.ReadAllTextAsync(path, cancellationToken))
            : new DeliveryOptionsLoadResult(null, [$"Delivery configuration file not found: {path}"]);

    public static DeliveryOptionsLoadResult Parse(string yaml)
    {
        Document? document;
        try
        {
            document = Deserializer.Deserialize<Document?>(yaml);
        }
        catch (YamlException ex)
        {
            return new DeliveryOptionsLoadResult(null, [$"delivery.yml is not valid YAML: {ex.Message}"]);
        }

        var errors = new List<string>();
        TimeZoneInfo? zone = null;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(document?.TimeZone ?? string.Empty);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            errors.Add($"timeZone '{document?.TimeZone}' is not a known time zone.");
        }

        if (document?.MinimumLeadMinutes is not >= 0)
        {
            errors.Add("minimumLeadMinutes must be zero or a positive number.");
        }

        var languages = new Dictionary<string, LanguageDelivery>(StringComparer.Ordinal);
        foreach (var language in DraftLanguage.All)
        {
            if (document?.Languages?.GetValueOrDefault(language.Code) is not { } entry)
            {
                errors.Add($"languages.{language.Code} is missing.");
                continue;
            }

            if (!TimeOnly.TryParseExact(entry.SendTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sendTime))
            {
                errors.Add($"languages.{language.Code}.sendTime must be HH:mm.");
            }

            if (entry.KitTagId is not > 0)
            {
                errors.Add($"languages.{language.Code}.kitTagId is not set; it comes from the Kit setup (issue-delivery spec, T0).");
            }

            if (entry.KitFormId is <= 0)
            {
                errors.Add($"languages.{language.Code}.kitFormId must be a positive Kit form id when set.");
            }

            languages[language.Code] = new LanguageDelivery(sendTime, entry.KitTagId ?? 0, entry.KitFormId);
        }

        foreach (var unknown in (document?.Languages?.Keys.ToList() ?? []).Where(code => DraftLanguage.All.All(language => language.Code != code)))
        {
            errors.Add($"languages.{unknown} is not an issue language.");
        }

        return errors.Count > 0
            ? new DeliveryOptionsLoadResult(null, errors)
            : new DeliveryOptionsLoadResult(
                new DeliveryOptions(zone!, TimeSpan.FromMinutes(document!.MinimumLeadMinutes!.Value), document.KitPublic ?? false, languages),
                []);
    }

    private sealed class Document
    {
        public string? TimeZone { get; set; }

        public int? MinimumLeadMinutes { get; set; }

        public bool? KitPublic { get; set; }

        public Dictionary<string, LanguageEntry>? Languages { get; set; }
    }

    private sealed class LanguageEntry
    {
        public string? SendTime { get; set; }

        public long? KitTagId { get; set; }

        public long? KitFormId { get; set; }
    }
}
