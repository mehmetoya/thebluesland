using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TheBluesland.WeeklyNewsletter.Clustering;

/// <summary>
/// Turns a headline or summary into comparable tokens (story-clustering spec, Design section 2,
/// step 1): lowercase, configured abbreviations, accents removed ("Céline" → "celine"),
/// possessive 's dropped, everything except a–z/0–9 treated as a separator, English stop words
/// removed. No synonym dictionary.
/// </summary>
public sealed partial class TextNormalizer(IReadOnlyList<Abbreviation> abbreviations)
{
    private static readonly HashSet<string> StopWords = new(
        ["a", "an", "the", "and", "or", "of", "to", "in", "on", "at", "for", "with", "by", "from",
         "is", "are", "was", "be", "as", "it", "its", "his", "her", "their", "this", "that", "new"],
        StringComparer.Ordinal);

    public IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var lower = text.ToLower(CultureInfo.InvariantCulture);
        foreach (var abbreviation in abbreviations)
        {
            lower = lower.Replace(abbreviation.From, abbreviation.To, StringComparison.Ordinal);
        }

        lower = Possessive().Replace(RemoveDiacritics(lower), string.Empty);
        return NonAlphanumeric()
            .Split(lower)
            .Where(token => token.Length > 0 && !StopWords.Contains(token))
            .ToList();
    }

    private static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"['’`]s\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Possessive();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NonAlphanumeric();
}
