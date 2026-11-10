using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TheBluesland.WeeklyNewsletter.Clustering;

namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>A note for Mehmet: where to look and why. Never blocks the draft.</summary>
/// <param name="Where">"intro", "outro" or a story key.</param>
public sealed record DraftWarning(string Where, string Message);

/// <summary>
/// Deterministic checks on the model's text (issue-drafting spec, Design section 3). Links,
/// brackets, angle brackets, emoji and exclamation marks are removed. Everything else only
/// produces warnings: lengths, numbers and proper names that do not appear in the fact card, and
/// runs of eight or more words copied from a summary. The approved sample issue in the voice guide
/// passes with no warning; that is this validator's calibration.
/// </summary>
public static partial class DraftValidator
{
    public const int MaxHeadline = 70;
    public const int MaxBodySentences = 3;
    public const int MaxBodyCharacters = 450;
    public const int MaxWhySentences = 2;
    public const int MaxWhyCharacters = 260;
    public const int CopiedWords = 8;

    // Capitalized words that are not facts: months, weekdays, common abbreviations and our own name.
    private static readonly HashSet<string> NotNames = new(
        ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
         "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar",
         "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
         "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
         "TheBluesland", "Weekly", "TV", "AI", "DJ"],
        StringComparer.Ordinal);

    public static (DraftResponse Draft, IReadOnlyList<DraftWarning> Warnings) Validate(DraftResponse draft, FactCard card)
    {
        var warnings = new List<DraftWarning>();
        var facts = card.AllText();
        var factNumbers = Number().Matches(facts).Select(match => match.Value.TrimStart('0') is { Length: > 0 } n ? n : "0").ToHashSet(StringComparer.Ordinal);
        var summaryRuns = card.Stories
            .SelectMany(story => story.Articles)
            .SelectMany(article => Runs(article.Summary))
            .ToHashSet(StringComparer.Ordinal);

        string Check(string where, string field, string text, int? maxCharacters, int? maxSentences)
        {
            var clean = Sanitize(text);
            if (clean != text)
            {
                warnings.Add(new DraftWarning(where, $"{field}: removed links, brackets, emoji or exclamation marks."));
            }

            if (maxCharacters is { } limit && clean.Length > limit)
            {
                warnings.Add(new DraftWarning(where, $"{field}: {clean.Length} characters (limit {limit})."));
            }

            if (maxSentences is { } sentences && SentenceEnd().Matches(clean).Count > sentences)
            {
                warnings.Add(new DraftWarning(where, $"{field}: more than {sentences} sentences."));
            }

            foreach (Match number in Number().Matches(clean))
            {
                var value = number.Value.TrimStart('0') is { Length: > 0 } n ? n : "0";
                if (!factNumbers.Contains(value))
                {
                    warnings.Add(new DraftWarning(where, $"{field}: number “{number.Value}” is not in the fact card."));
                }
            }

            foreach (var name in Names(clean).Where(name => !facts.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add(new DraftWarning(where, $"{field}: name “{name}” is not in the fact card."));
            }

            if (Runs(clean).FirstOrDefault(summaryRuns.Contains) is { } copied)
            {
                warnings.Add(new DraftWarning(where, $"{field}: copies a summary (“{copied}…”)."));
            }

            return clean;
        }

        var intro = Check("intro", "intro", draft.Intro, null, 2);
        var outro = Check("outro", "outro", draft.Outro, null, 2);
        var stories = draft.Stories
            .Select(story => new DraftStory(
                story.Key,
                Check(story.Key, "headline", story.Headline, MaxHeadline, null),
                Check(story.Key, "body", story.Body, MaxBodyCharacters, MaxBodySentences),
                Check(story.Key, "why it matters", story.WhyItMatters, MaxWhyCharacters, MaxWhySentences)))
            .ToList();

        return (new DraftResponse(intro, outro, stories), warnings);
    }

    /// <summary>Removes what the code owns (links, Markdown/HTML brackets) and what the voice forbids (emoji, "!").</summary>
    public static string Sanitize(string text)
    {
        var withoutLinks = Url().Replace(HtmlTag().Replace(MarkdownLink().Replace(text, "$1"), string.Empty), string.Empty);
        var builder = new StringBuilder(withoutLinks.Length);
        for (var i = 0; i < withoutLinks.Length; i++)
        {
            var character = withoutLinks[i];
            if (char.IsHighSurrogate(character) && i + 1 < withoutLinks.Length)
            {
                // Astral-plane symbols are emoji in practice; letters there do not occur in this content.
                if (CharUnicodeInfo.GetUnicodeCategory(withoutLinks, i) == UnicodeCategory.OtherSymbol)
                {
                    i++;
                    continue;
                }

                builder.Append(character).Append(withoutLinks[++i]);
                continue;
            }

            if (character is '[' or ']' or '<' or '>' or '!' or '️'
                || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.OtherSymbol)
            {
                continue;
            }

            builder.Append(character);
        }

        return Spaces().Replace(builder.ToString(), " ").Trim();
    }

    /// <summary>Capitalized words that do not start a sentence, without a Turkish suffix after an apostrophe.</summary>
    private static IEnumerable<string> Names(string text)
    {
        foreach (Match word in Word().Matches(text))
        {
            if (!char.IsUpper(word.Value[0]) || word.Value.Length < 2 || StartsSentence(text, word.Index))
            {
                continue;
            }

            var name = Suffix().Replace(word.Value, string.Empty);
            if (name.Length >= 2 && !NotNames.Contains(name))
            {
                yield return name;
            }
        }
    }

    private static bool StartsSentence(string text, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var character = text[i];
            if (char.IsWhiteSpace(character) || character is '"' or '“' or '‘' or '\'' or '(' or '*')
            {
                continue;
            }

            return character is '.' or '?' or ':' or '…';
        }

        return true;
    }

    /// <summary>Every run of <see cref="CopiedWords"/> consecutive words, lowercase and accent-free.</summary>
    private static IEnumerable<string> Runs(string? text)
    {
        var words = TextNormalizer.Flatten(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + CopiedWords <= words.Length; i++)
        {
            yield return string.Join(' ', words, i, CopiedWords);
        }
    }

    [GeneratedRegex(@"https?://\S+|www\.\S+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Url();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex MarkdownLink();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Number();

    [GeneratedRegex(@"[.?…](?=\s|$)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"[\p{L}][\p{L}\p{N}'’]*", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Word();

    [GeneratedRegex(@"['’]\p{L}*$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Suffix();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Spaces();
}
