namespace TheBluesland.Web.Localization;

/// <summary>
/// Display labels for the editorial taxonomy values (mood/genre/occasion/era). The values
/// themselves - the filter query strings, the validator's allow-lists, the front matter - never
/// change with the language; only what the visitor reads does. English keeps showing the raw value,
/// exactly as the site did before localization; Turkish has a label per value, and a value without
/// one (a newly approved tag nobody translated yet) falls back to the raw value rather than failing.
/// </summary>
public static class TaxonomyLabels
{
    private static readonly IReadOnlyDictionary<string, string> TurkishLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Moods.
        ["melancholic"] = "melankolik",
        ["warm"] = "sıcak",
        ["energetic"] = "enerjik",
        ["raw"] = "ham",
        ["nostalgic"] = "nostaljik",

        // Genres.
        ["blues"] = "blues",
        ["blues-rock"] = "blues rock",
        ["rock"] = "rock",
        ["soul"] = "soul",
        ["jazz"] = "caz",
        ["anadolu-rock"] = "Anadolu rock",
        ["punk"] = "punk",
        ["metal"] = "metal",
        ["indie"] = "indie",
        ["folk"] = "folk",
        ["funk"] = "funk",
        ["country"] = "country",
        ["classical"] = "klasik",
        ["electronic"] = "elektronik",
        ["world"] = "dünya müziği",
        ["pop"] = "pop",

        // Occasions.
        ["late-night"] = "gece geç saat",
        ["night-drive"] = "gece yolculuğu",
        ["road-trip"] = "uzun yol",
        ["slow-evening"] = "sakin akşam",
        ["headphones"] = "kulaklıkla",
        ["focus"] = "odaklanma",
        ["dancing"] = "dans",
        ["deep-listen"] = "derin dinleme",

        // Eras.
        ["pre-1970"] = "1970 öncesi",
        ["1970s"] = "1970'ler",
        ["1980s-1990s"] = "1980'ler-1990'lar",
        ["2000s-present"] = "2000'ler-günümüz",
        ["mixed-era"] = "karma dönem",
    };

    public static bool HasTurkishLabel(string value) => TurkishLabels.ContainsKey(value);

    /// <summary>The label with its first letter upper-cased (Turkish i becomes İ), for use at the start of a title.</summary>
    public static string CapitalizedLabel(string value, string languageCode)
    {
        var label = Label(value, languageCode);
        if (label.Length == 0)
        {
            return label;
        }

        var first = label[0] == 'i' && languageCode == SiteLanguage.Turkish ? 'İ' : char.ToUpperInvariant(label[0]);
        return first + label[1..];
    }

    public static string Label(string value, string languageCode) =>
        languageCode == SiteLanguage.Turkish && TurkishLabels.TryGetValue(value, out var label) ? label : value;
}
