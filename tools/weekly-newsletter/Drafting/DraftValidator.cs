using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TheBluesland.WeeklyNewsletter.Clustering;

namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>A note for Mehmet: where to look and why. Never blocks the draft.</summary>
/// <param name="Where">"intro", "outro", "subject", "preview" or a story key.</param>
public sealed record DraftWarning(string Where, string Message);

/// <summary>
/// Deterministic checks on the model's text (issue-drafting spec, Design section 3). Links,
/// brackets, angle brackets, emoji and exclamation marks are removed, and any outro sentence about AI
/// is dropped (the code adds the AI note). Everything else only produces warnings: lengths, numbers
/// and proper names that are not in the card of the story they sit in, numbers and names from a
/// story's card headlines that the text leaves out, TR/EN differences, time or quantity wording the
/// card does not state, events already past written in future tense, stock "why it matters" phrases,
/// runs of eight or more words copied from a summary and the hype words the voice guide forbids.
/// The approved sample issue in the voice guide passes with no warning; that is this validator's calibration.
/// </summary>
public static partial class DraftValidator
{
    public const int MaxHeadline = 70;
    public const int MaxBodySentences = 3;
    public const int MaxBodyCharacters = 450;
    public const int MaxWhySentences = 2;
    public const int MaxWhyCharacters = 260;
    public const int CopiedWords = 8;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    // A date this far before the send date is read as next year's, not as a past event.
    private static readonly TimeSpan PastLookback = TimeSpan.FromDays(60);

    // A date in the card this close before the send date is an event readers will find over (the card
    // is written in the window; the issue goes out a week later).
    private static readonly TimeSpan CardPastLookback = TimeSpan.FromDays(14);

    // Capitalized words that are not facts: months, weekdays, common abbreviations and our own name.
    private static readonly HashSet<string> NotNames = new(
        ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
         "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar",
         "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
         "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
         "TheBluesland", "Weekly", "TBL", "TV", "AI", "DJ"],
        StringComparer.Ordinal);

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"] = 1,
        ["january"] = 1,
        ["ocak"] = 1,
        ["feb"] = 2,
        ["february"] = 2,
        ["şubat"] = 2,
        ["mar"] = 3,
        ["march"] = 3,
        ["mart"] = 3,
        ["apr"] = 4,
        ["april"] = 4,
        ["nisan"] = 4,
        ["may"] = 5,
        ["mayıs"] = 5,
        ["jun"] = 6,
        ["june"] = 6,
        ["haziran"] = 6,
        ["jul"] = 7,
        ["july"] = 7,
        ["temmuz"] = 7,
        ["aug"] = 8,
        ["august"] = 8,
        ["ağustos"] = 8,
        ["sep"] = 9,
        ["sept"] = 9,
        ["september"] = 9,
        ["eylül"] = 9,
        ["oct"] = 10,
        ["october"] = 10,
        ["ekim"] = 10,
        ["nov"] = 11,
        ["november"] = 11,
        ["kasım"] = 11,
        ["dec"] = 12,
        ["december"] = 12,
        ["aralık"] = 12,
    };

    // Time and quantity wording the card must state before a draft may use it: one Turkish and one
    // English pattern per idea, so a Turkish "pazartesi" is backed by an English "Monday" in the card.
    private static readonly (Regex Turkish, Regex English)[] TimeWords =
    [
        (Pattern(@"(?<!\p{L})bir yıl(?!\p{L}{4,})"), Pattern(@"\b(?:a|one) year\b(?! (?:old|-old))")),
        (Pattern(@"(?<!\p{L})(?:on yıl|onlarca yıl|on yıllar)\p{L}*"), Pattern(@"\bdecades?\b")),
        (Pattern(@"(?<!\p{L})(?:geçen ay|aylar önce|bir ay önce)"), Pattern(@"\b(?:last month|months ago|a month ago)\b")),
        (Pattern(@"(?<!\p{L})(?:geçen hafta|haftalar önce)"), Pattern(@"\b(?:last week|weeks ago)\b")),
        (Pattern(@"(?<!\p{L})(?:geçen yıl|yıllar önce)"), Pattern(@"\b(?:last year|years ago)\b")),
        (Pattern(@"(?<!\p{L})pazartesi\p{L}{0,3}(?!\p{L})"), Pattern(@"\bMonday\b")),
        (Pattern(@"(?<!\p{L})salı\p{L}{0,2}(?!\p{L})"), Pattern(@"\bTuesday\b")),
        (Pattern(@"(?<!\p{L})çarşamba\p{L}{0,3}(?!\p{L})"), Pattern(@"\bWednesday\b")),
        (Pattern(@"(?<!\p{L})perşembe\p{L}{0,3}(?!\p{L})"), Pattern(@"\bThursday\b")),
        (Pattern(@"(?<!\p{L})cuma\p{L}{0,3}(?!\p{L})"), Pattern(@"\bFriday\b")),
        (Pattern(@"(?<!\p{L})cumartesi\p{L}{0,3}(?!\p{L})"), Pattern(@"\bSaturday\b")),
        (Pattern(@"(?<!\p{L})pazar\p{L}{0,2}(?!\p{L})"), Pattern(@"\bSunday\b")),
    ];

    public static (DraftResponse Draft, IReadOnlyList<DraftWarning> Warnings) Validate(
        DraftResponse draft,
        FactCard card,
        DraftLanguage? language = null,
        DateOnly? sendDate = null,
        string? sampleText = null)
    {
        var warnings = new List<DraftWarning>();
        var all = FactsOf(card.Stories);
        var perStory = card.Stories.ToDictionary(story => story.Key, story => FactsOf([story]), StringComparer.Ordinal);
        var summaryRuns = card.Stories
            .SelectMany(story => story.Articles)
            .SelectMany(article => Runs(article.Summary))
            .ToHashSet(StringComparer.Ordinal);
        var sampleRuns = Runs(sampleText).ToHashSet(StringComparer.Ordinal);
        var titleRuns = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var article in card.Stories.SelectMany(story => story.Articles))
        {
            foreach (var run in Runs(article.Title))
            {
                titleRuns.TryAdd(run, article.SourceName);
            }
        }

        string Check(string where, string field, string text, Facts facts, int? maxCharacters, int? maxSentences)
        {
            var clean = Sanitize(text);
            if (clean != Spaces().Replace(text, " ").Trim())
            {
                warnings.Add(new DraftWarning(where, $"{field}: removed links, addresses, brackets, emoji or exclamation marks."));
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
                if (!facts.Numbers.Contains(Normalize(number.Value)))
                {
                    warnings.Add(new DraftWarning(where, $"{field}: number “{number.Value}” is not in the fact card."));
                }
            }

            foreach (var name in Names(clean, exemptFirstWord: true).Where(name => !facts.Names.Contains(name)))
            {
                warnings.Add(new DraftWarning(where, $"{field}: name “{name}” is not in the fact card."));
            }

            foreach (var hype in HypeWords(clean))
            {
                warnings.Add(new DraftWarning(where, $"{field}: hype word “{hype}” (voice guide: no superlatives)."));
            }

            if (ThisWeek().Match(clean) is { Success: true } thisWeek)
            {
                warnings.Add(new DraftWarning(where, $"{field}: “{thisWeek.Value}”; the issue is sent on Monday and covers the previous week."));
            }

            var copies = Runs(clean).Where(summaryRuns.Contains).ToList();
            if (field == "headline" && copies.Count > 0 && copies.All(titleRuns.ContainsKey))
            {
                // Feeds repeat their headline inside the summary, and a headline is a short title, not prose:
                // one note, not a copy warning (body and why-it-matters copies stay warnings).
                warnings.Add(new DraftWarning(where, $"headline matches {titleRuns[copies[0]]}'s headline."));
            }
            else if (copies.Count > 0)
            {
                warnings.Add(new DraftWarning(where, $"{field}: copies a summary (“{copies[0]}…”)."));
            }

            if (Runs(clean).FirstOrDefault(sampleRuns.Contains) is { } sampled)
            {
                warnings.Add(new DraftWarning(where, $"{field}: copies the voice guide's sample issue (“{sampled}…”)."));
            }

            return clean;
        }

        var intro = Check("intro", "intro", draft.Intro, all, null, 2);
        if (language is not null && !intro.Contains(language.IntroTagline, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new DraftWarning("intro", $"intro: does not include “{language.IntroTagline}”."));
        }

        var outro = Check("outro", "outro", WithoutAiSentences(draft.Outro, language, warnings), all, null, 2);

        var subject = draft.Subject is null
            ? null
            : Check("subject", "subject", draft.Subject, all, null, null);
        if (subject is { } rawSubject && rawSubject.Length > DraftLanguage.MaxSubject)
        {
            warnings.Add(new DraftWarning("subject", $"subject: {rawSubject.Length} characters (limit {DraftLanguage.MaxSubject}); published as “{IssueMarkdownWriter.FitSubject(rawSubject)}”."));
        }
        var preview = draft.Preview is null
            ? null
            : Check("preview", "preview", draft.Preview, all, DraftLanguage.MaxPreview, null);
        if (subject is null || preview is null)
        {
            warnings.Add(new DraftWarning(subject is null ? "subject" : "preview", $"{(subject is null ? "subject" : "preview")}: missing; the e-mail falls back to the issue title or the intro."));
        }

        if (subject is not null && !subject.EndsWith(DraftLanguage.SubjectTag, StringComparison.Ordinal))
        {
            warnings.Add(new DraftWarning("subject", $"subject: does not end with “{DraftLanguage.SubjectTag}”."));
        }

        if (subject is not null && preview is not null && RepeatsSubject(subject, preview))
        {
            warnings.Add(new DraftWarning("preview", "preview: repeats the subject."));
        }

        var stories = new List<DraftStory>();
        foreach (var story in draft.Stories)
        {
            var cardStory = card.Stories.FirstOrDefault(candidate => candidate.Key == story.Key);
            var facts = perStory.GetValueOrDefault(story.Key) ?? all;
            if (story.WhyItMatters == DraftResponse.MissingBackupNote)
            {
                // The code filled this backup from the card (untranslated English): checking card text
                // against itself only makes noise.
                stories.Add(new DraftStory(story.Key, Sanitize(story.Headline), Sanitize(story.Body), story.WhyItMatters));
                warnings.Add(new DraftWarning(story.Key, "backup not written by the model; card text shown untranslated."));
                continue;
            }

            var headline = Check(story.Key, "headline", story.Headline, facts, MaxHeadline, null);
            var body = Check(story.Key, "body", story.Body, facts, MaxBodyCharacters, MaxBodySentences);
            var why = Check(story.Key, "why it matters", story.WhyItMatters, facts, MaxWhyCharacters, MaxWhySentences);
            stories.Add(new DraftStory(story.Key, headline, body, why));
            StoryChecks(story.Key, $"{headline} {body} {why}", cardStory, facts, sendDate, warnings);
            if (RepeatsBody(why, $"{headline} {body}"))
            {
                warnings.Add(new DraftWarning(story.Key, "why it matters repeats the headline/body; add a concrete fact from the card."));
            }
        }

        var introNames = CapitalWords(intro);
        var mentioned = stories.Count(story => card.Picks.Any(pick => pick.Key == story.Key)
            && CapitalWords(story.Headline).Any(introNames.Contains));
        if (mentioned > 3)
        {
            warnings.Add(new DraftWarning("intro", "intro names more than three stories."));
        }

        return (new DraftResponse(intro, outro, stories, subject, preview), warnings);
    }

    /// <summary>
    /// Adds the TR/EN parity warnings: a number or proper name that one language's story carries and
    /// the other's does not (the two languages carry the same facts; only the style differs). One
    /// warning per story and direction lists everything missing. Backups the model did not write and
    /// items already reported as omitted are left out.
    /// </summary>
    public static List<LanguageDraft> AddParity(IReadOnlyList<LanguageDraft> drafts, FactCard card)
    {
        var result = drafts.ToList();
        if (drafts.Count != 2)
        {
            return result;
        }

        for (var i = 0; i < 2; i++)
        {
            var (mine, other) = (drafts[i], drafts[1 - i]);
            var extra = new List<DraftWarning>();
            foreach (var story in card.Stories)
            {
                var theirs = other.Text.Stories.FirstOrDefault(candidate => candidate.Key == story.Key);
                var ours = mine.Text.Stories.FirstOrDefault(candidate => candidate.Key == story.Key);
                if (theirs is null || ours is null
                    || theirs.WhyItMatters == DraftResponse.MissingBackupNote || ours.WhyItMatters == DraftResponse.MissingBackupNote)
                {
                    continue;
                }

                var tokens = Tokens(StoryText(ours));
                var numbers = Number().Matches(StoryText(ours)).Select(match => Normalize(match.Value)).ToHashSet(StringComparer.Ordinal);
                var omitted = mine.Warnings.Concat(other.Warnings)
                    .Where(warning => warning.Where == story.Key && warning.Message.StartsWith("omitted:", StringComparison.Ordinal))
                    .SelectMany(warning => OmittedItem().Matches(warning.Message).Select(match => match.Groups[1].Value))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missing = ParityItems(theirs, story)
                    .Where(item => item.IsNumber ? !numbers.Contains(item.Words[0]) : !item.Words.All(tokens.Contains))
                    .Where(item => !item.Words.Any(omitted.Contains))
                    .Select(item => item.Display)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (missing.Count > 0)
                {
                    var (a, b) = (mine.Language.Code.ToUpperInvariant(), other.Language.Code.ToUpperInvariant());
                    var list = string.Join(", ", missing.Select(item => $"“{item}”"));
                    extra.Add(new DraftWarning(story.Key, $"{a}/{b} parity: {list} {(missing.Count == 1 ? "is" : "are")} in the {b} story but not in the {a} story."));
                }
            }

            result[i] = mine with { Warnings = [.. mine.Warnings, .. extra] };
        }

        return result;
    }

    /// <summary>Removes what the code owns (links, Markdown/HTML brackets) and what the voice forbids (emoji, "!").</summary>
    public static string Sanitize(string text)
    {
        var withoutLinks = BareDomain().Replace(Email().Replace(Url().Replace(HtmlTag().Replace(MarkdownLink().Replace(text, "$1"), string.Empty), string.Empty), string.Empty), string.Empty);
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

            if (char.IsWhiteSpace(character))
            {
                // Newlines and Unicode line separators must not survive: they would end a YAML
                // scalar or a Markdown heading in the written file.
                builder.Append(' ');
                continue;
            }

            if (character is '[' or ']' or '<' or '>' or '!'
                || CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol
                    or UnicodeCategory.EnclosingMark or UnicodeCategory.Control or UnicodeCategory.Format)
            {
                continue;
            }

            builder.Append(character);
        }

        return Spaces().Replace(builder.ToString(), " ").Trim();
    }

    private sealed record Facts(HashSet<string> Numbers, HashSet<string> Names, string Text);

    /// <summary>The numbers, words and text one or more card stories offer. The prompt shows each article's date, so its day, month and year are facts too.</summary>
    private static Facts FactsOf(IReadOnlyCollection<FactStory> stories)
    {
        var text = string.Join(" \n ", stories.Select(story => story.AllText()));
        var numbers = Number().Matches(text).Select(match => Normalize(match.Value)).ToHashSet(StringComparer.Ordinal);
        foreach (var date in stories.SelectMany(story => story.Articles).Select(article => article.PublishedAt.UtcDateTime))
        {
            numbers.UnionWith([date.Day.ToString(CultureInfo.InvariantCulture), date.Month.ToString(CultureInfo.InvariantCulture), date.Year.ToString(CultureInfo.InvariantCulture)]);
        }

        return new Facts(numbers, Tokens(text), text);
    }

    private static string Normalize(string number) => number.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0";

    private static HashSet<string> Tokens(string text) =>
        Word().Matches(text).Select(word => Suffix().Replace(word.Value, string.Empty)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string StoryText(DraftStory story) => $"{story.Headline} {story.Body} {story.WhyItMatters}";

    private static void StoryChecks(string key, string text, FactStory? cardStory, Facts facts, DateOnly? sendDate, List<DraftWarning> warnings)
    {
        if (cardStory is not null)
        {
            foreach (var omitted in Omissions(cardStory, text))
            {
                warnings.Add(new DraftWarning(key, $"omitted: {omitted} is in the card's headlines for this story but not in the text."));
            }
        }

        foreach (var (turkish, english) in TimeWords)
        {
            var match = turkish.Match(text) is { Success: true } tr ? tr : english.Match(text);
            if (match.Success && !turkish.IsMatch(facts.Text) && !english.IsMatch(facts.Text))
            {
                warnings.Add(new DraftWarning(key, $"time wording “{match.Value}” is not in the fact card; use only dates and spans the card states."));
            }
        }

        foreach (var phrase in GenericPhrase().Matches(text).Select(match => match.Value.ToLowerInvariant()).Distinct(StringComparer.Ordinal))
        {
            warnings.Add(new DraftWarning(key, $"generic phrase “{phrase}”: why it matters should state a concrete consequence from the card."));
        }

        if (sendDate is { } send && FutureMarker().Match(text) is { Success: true } future)
        {
            if (PastDates(text, send, PastLookback.TotalDays).FirstOrDefault() is { Length: > 0 } past)
            {
                warnings.Add(new DraftWarning(key, $"“{past}” is before the send date ({send:yyyy-MM-dd}) but the text uses future tense (“{future.Value}”); write past events in past tense."));
            }
            else if (PastDates(facts.Text, send, CardPastLookback.TotalDays).FirstOrDefault() is { Length: > 0 } cardPast)
            {
                warnings.Add(new DraftWarning(key, $"the card dates “{cardPast}” before the send date; write it as already announced/happened, not future tense."));
            }
        }
    }

    /// <summary>
    /// Numbers, acronyms and proper names that most of a story's card headlines share and the text
    /// lacks. A capitalized headline word counts as a name only when a summary of the same story
    /// also capitalizes it mid-sentence (headlines are Title Case, so case alone proves nothing), and a
    /// multi-word name is covered by its initials ("Grand Theft Auto" by "GTA").
    /// </summary>
    private static IEnumerable<string> Omissions(FactStory story, string text)
    {
        var headlines = story.Articles.Select(article => article.Title).ToList();
        if (headlines.Count == 0)
        {
            yield break;
        }

        // Some feeds repeat the headline inside the summary; that Title Case proves nothing, so it is cut out.
        var attested = story.Articles
            .SelectMany(article => CapitalizedMidSentence(article.Summary?.Replace(article.Title, " ", StringComparison.OrdinalIgnoreCase) ?? string.Empty))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lowercase = Word().Matches(story.AllText()).Where(word => char.IsLower(word.Value[0])).Select(word => Suffix().Replace(word.Value, string.Empty)).ToHashSet(StringComparer.Ordinal);
        attested.ExceptWith(lowercase.Where(word => attested.Contains(word)));
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var initials = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var headline in headlines)
        {
            var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match number in Number().Matches(headline))
            {
                items.Add($"n:{Normalize(number.Value)}");
            }

            var run = new List<string>();
            var previousEnd = -1;
            void Flush()
            {
                if (run.Count >= 2)
                {
                    var letters = new string(run.Select(word => char.ToUpperInvariant(word[0])).ToArray());
                    foreach (var word in run)
                    {
                        initials.TryAdd(word, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                        initials[word].Add(letters);
                    }
                }

                run.Clear();
            }

            foreach (Match match in Word().Matches(headline))
            {
                var word = Suffix().Replace(match.Value, string.Empty);
                if (word.Length >= 2 && word.All(char.IsUpper) && !NotNames.Contains(word))
                {
                    items.Add($"a:{word}");
                }

                var adjacent = previousEnd >= 0 && headline[previousEnd..match.Index].All(char.IsWhiteSpace);
                if (!adjacent)
                {
                    Flush();
                }

                if (char.IsUpper(word[0]) && word.Length >= 2 && attested.Contains(word) && !NotNames.Contains(word))
                {
                    items.Add($"w:{word}");
                    run.Add(word);
                }
                else
                {
                    Flush();
                }

                previousEnd = match.Index + match.Length;
            }

            Flush();
            foreach (var item in items)
            {
                counts[item] = counts.GetValueOrDefault(item) + 1;
            }
        }

        var words = Tokens(text);
        var numbers = Number().Matches(text).Select(match => Normalize(match.Value)).ToHashSet(StringComparer.Ordinal);
        foreach (var (item, count) in counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (count * 2 <= headlines.Count)
            {
                continue;
            }

            var value = item[2..];
            var covered = item[0] switch
            {
                'n' => numbers.Contains(value),
                'a' => words.Contains(value),
                _ => words.Contains(value) || (initials.TryGetValue(value, out var letters) && letters.Any(words.Contains)),
            };
            if (!covered)
            {
                yield return item[0] == 'n' ? $"number “{value}”" : $"“{value}”";
            }
        }
    }

    private static IEnumerable<string> CapitalizedMidSentence(string text)
    {
        foreach (Match word in Word().Matches(text))
        {
            if (char.IsUpper(word.Value[0]) && word.Value.Length >= 2 && !StartsSentence(text, word.Index))
            {
                yield return Suffix().Replace(word.Value, string.Empty);
            }
        }
    }

    private sealed record ParityItem(string Display, string[] Words, bool IsNumber);

    // Title and abbreviation particles that begin or sit inside a name without being part of one.
    private static readonly HashSet<string> NameParticles = new(["The", "St", "Mt", "Dr", "Mr", "Mrs", "Ms", "Of", "And"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Numbers and proper names of one story, for comparing the two languages. Adjacent capitalized
    /// words form one name. Headlines are Title Case, so a headline word counts only when the story's
    /// text or a card summary also capitalizes it mid-sentence and the card never writes it lowercase
    /// (the same proof <see cref="Omissions"/> asks for).
    /// </summary>
    private static List<ParityItem> ParityItems(DraftStory story, FactStory cardStory)
    {
        var items = new List<ParityItem>();
        foreach (var field in new[] { story.Headline, story.Body, story.WhyItMatters })
        {
            items.AddRange(Number().Matches(field).Select(match => Normalize(match.Value)).Distinct(StringComparer.Ordinal).Select(number => new ParityItem(number, [number], true)));
        }

        var attested = cardStory.Articles
            .SelectMany(article => CapitalizedMidSentence(article.Summary?.Replace(article.Title, " ", StringComparison.OrdinalIgnoreCase) ?? string.Empty))
            .Concat(CapitalizedMidSentence($"{story.Body} {story.WhyItMatters}"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lowercase = Word().Matches(cardStory.AllText()).Where(word => char.IsLower(word.Value[0])).Select(word => Suffix().Replace(word.Value, string.Empty)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        items.AddRange(NamePhrases(story.Headline, name => attested.Contains(name) && !lowercase.Contains(name)));
        items.AddRange(NamePhrases(story.Body, _ => true));
        items.AddRange(NamePhrases(story.WhyItMatters, _ => true));
        return items;
    }

    /// <summary>Runs of adjacent capitalized words (see <see cref="Names"/>), each a single name as written.</summary>
    private static List<ParityItem> NamePhrases(string text, Func<string, bool> accept)
    {
        var run = new List<Match>();
        var previousEnd = -1;
        var first = Word().Match(text);
        var phrases = new List<ParityItem>();

        void Flush()
        {
            if (run.Count > 0)
            {
                var words = run.Select(word => Suffix().Replace(word.Value, string.Empty)).ToArray();
                phrases.Add(new ParityItem(string.Join(' ', run.Select(word => word.Value.TrimEnd('\'', '’'))), words, false));
                run.Clear();
            }
        }

        foreach (Match word in Word().Matches(text))
        {
            var name = Suffix().Replace(word.Value, string.Empty);
            var isName = char.IsUpper(word.Value[0]) && name.Length >= 2 && !NotNames.Contains(name) && !NameParticles.Contains(name)
                && !(StartsSentence(text, word.Index) && !LooksLikeName(word, name))
                && accept(name);
            var adjacent = previousEnd >= 0 && text[previousEnd..word.Index].All(char.IsWhiteSpace);
            if (!isName || !adjacent)
            {
                Flush();
            }

            if (isName)
            {
                run.Add(word);
            }

            previousEnd = word.Index + word.Length;
        }

        Flush();
        return phrases;
    }

    /// <summary>The sentences of an outro minus any that talk about AI: the code adds the AI note itself (ADR-0006 madde 4).</summary>
    private static string WithoutAiSentences(string outro, DraftLanguage? language, List<DraftWarning> warnings)
    {
        var sentences = SentenceBreak().Split(outro.Trim());
        var kept = sentences.Where(sentence => !AiMention().IsMatch(sentence)).ToList();
        if (kept.Count == sentences.Length)
        {
            return outro;
        }

        warnings.Add(new DraftWarning("outro", "outro: removed a sentence about AI; the code adds the AI note."));
        return kept.Count > 0 ? string.Join(' ', kept) : language?.FallbackOutro ?? string.Empty;
    }

    private static readonly HashSet<string> StopWords = new(
        ["that", "this", "with", "from", "have", "were", "their", "them", "they", "also", "than", "then", "will", "would", "been", "into",
         "için", "gibi", "daha", "olan", "ancak", "çünkü", "kadar", "sonra", "önce", "bile", "veya", "olarak"],
        StringComparer.Ordinal);

    /// <summary>
    /// True when most of the content words (four letters or more) of "why it matters" already occur in the
    /// headline and body, compared on a five-letter stem so Turkish suffixes and English endings match.
    /// </summary>
    private static bool RepeatsBody(string why, string headlineAndBody)
    {
        static HashSet<string> Stems(string text) => Word().Matches(text)
            .Select(word => Suffix().Replace(word.Value, string.Empty).ToLowerInvariant())
            .Where(word => word.Length >= 4 && !StopWords.Contains(word))
            .Select(word => word.Length > 5 ? word[..5] : word)
            .ToHashSet(StringComparer.Ordinal);

        var words = Stems(why);
        var known = Stems(headlineAndBody);
        return words.Count >= 3 && words.Count(known.Contains) * 10 >= words.Count * 7;
    }

    private static bool RepeatsSubject(string subject, string preview)
    {
        var core = subject.EndsWith(DraftLanguage.SubjectTag, StringComparison.Ordinal) ? subject[..^DraftLanguage.SubjectTag.Length] : subject;
        var (left, right) = (TextNormalizer.Flatten(core), TextNormalizer.Flatten(preview));
        return left.Length > 0 && (right.Contains(left, StringComparison.Ordinal) || left.Contains(right, StringComparison.Ordinal));
    }

    /// <summary>Every "Month day" / "day Month" in the text that falls shortly before the send date, as written.</summary>
    private static IEnumerable<string> PastDates(string text, DateOnly sendDate, double maxDays)
    {
        foreach (var match in MonthDay().Matches(text).Concat(DayMonth().Matches(text)))
        {
            if (Months.TryGetValue(match.Groups["m"].Value.TrimEnd('.'), out var month)
                && int.TryParse(match.Groups["d"].Value, CultureInfo.InvariantCulture, out var day)
                && day is >= 1 and <= 31
                && month is >= 1 and <= 12
                && day <= DateTime.DaysInMonth(sendDate.Year, month))
            {
                var date = new DateOnly(sendDate.Year, month, day);
                if (date < sendDate && sendDate.DayNumber - date.DayNumber <= maxDays)
                {
                    yield return match.Value;
                }
            }
        }
    }

    /// <summary>
    /// The voice guide's banned superlatives ("efsanevi", "tarihi", "inanılmaz" and their English
    /// kin), lowercased. Turkish "tarihi" alone is also "its date", so only the hype phrases count.
    /// </summary>
    private static IEnumerable<string> HypeWords(string text) =>
        // Invariant lowering leaves Turkish "İ" as is; map it by hand so "İnanılmaz" is caught too.
        Hype().Matches(text.Replace('İ', 'i').ToLowerInvariant())
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Capitalized words, without a Turkish suffix after an apostrophe, that are not the field's first
    /// word. A word that opens a later sentence counts only when it looks like a name (an acronym or
    /// a name with a Turkish suffix): Turkish and English sentences open with plenty of ordinary
    /// capitalized words.
    /// </summary>
    private static IEnumerable<string> Names(string text, bool exemptFirstWord)
    {
        var first = Word().Match(text);
        foreach (Match word in Word().Matches(text))
        {
            if (!char.IsUpper(word.Value[0]) || word.Value.Length < 2 || (exemptFirstWord && word.Index == first.Index))
            {
                continue;
            }

            var name = Suffix().Replace(word.Value, string.Empty);
            if (name.Length < 2 || NotNames.Contains(name) || (StartsSentence(text, word.Index) && !LooksLikeName(word, name)))
            {
                continue;
            }

            yield return name;
        }
    }

    /// <summary>Every capitalized word of two letters or more (a sentence's first word included), without its Turkish suffix.</summary>
    private static HashSet<string> CapitalWords(string text) =>
        Word().Matches(text)
            .Select(word => Suffix().Replace(word.Value, string.Empty))
            .Where(word => word.Length >= 2 && char.IsUpper(word[0]) && !NotNames.Contains(word) && !NameParticles.Contains(word))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool LooksLikeName(Match word, string name)
    {
        return name.Length != word.Value.Length || name.All(char.IsUpper);
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

    private static Regex Pattern(string pattern) =>
        new(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout);

    [GeneratedRegex(@"https?://\S+|www\.\S+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Url();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Email();

    // Bare domains that mail clients turn into links ("example.com/x").
    [GeneratedRegex(@"\b[a-z0-9-]+(\.[a-z0-9-]+)*\.(com|net|org|io|co|tv|fm|me|app|dev|info|biz|xyz|ly|gg)\b(/\S*)?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex BareDomain();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"(?<!\w)(efsane\w*|inanılmaz\w*|inanilmaz\w*|muhteşem\w*|müthiş\w*|tarihi bir|tarihe geç\w*|tarih yazd\w*|legend\w*|historic\w*|incredibl\w*|unbelievabl\w*|iconic\w*|amazing\w*|epic)(?!\w)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Hype();

    // Stock "why it matters" phrases that say nothing the card states (content review FC-2/ED-6).
    [GeneratedRegex(@"öncü bir grup|geniş (?:bir )?kitle\w*|doğrudan belirliyor|kendine yer buluyor|h[âa]l[âa] bu mirasla|pek çok müzisyen\w*|birçok müzisyen\w*|yeniden yer alacak|\bmany musicians\b|\ba favorite of\b|\bproving\b|reach well beyond|\bpioneering\b|\bcoveted\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex GenericPhrase();

    [GeneratedRegex(@"\bthis week\b|(?<!\p{L})bu hafta(?!\p{L})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex ThisWeek();

    // Turkish future tense (-acak/-ecek after a stem of three letters or more, so "bacak" is not one) and English "will".
    [GeneratedRegex(@"(?<!\p{L})\p{L}{3,}(?:acak|ecek)\p{L}{0,6}(?!\p{L})|\b(?:will|won't|won’t)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex FutureMarker();

    private const string MonthNames = "Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?|Ocak|Şubat|Mart|Nisan|Mayıs|Haziran|Temmuz|Ağustos|Eylül|Ekim|Kasım|Aralık";

    [GeneratedRegex(@"\b(?<m>" + MonthNames + @")\.?\s+(?<d>\d{1,2})(?:st|nd|rd|th)?\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex MonthDay();

    [GeneratedRegex(@"\b(?<d>\d{1,2})(?:st|nd|rd|th)?\s+(?<m>" + MonthNames + @")(?!\p{L})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex DayMonth();

    // "AI", "yapay zekâ/zeka", "artificial intelligence" - the disclosure's own words.
    [GeneratedRegex(@"\bAI\b|yapay zek[aâ]|artificial intelligence|language model|dil modeli|\bGemini\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex AiMention();

    [GeneratedRegex(@"(?<=[.?…])\s+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Number();

    // The quoted item of an "omitted: ... is in the card's headlines" warning.
    [GeneratedRegex(@"“([^”]+)”", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex OmittedItem();

    [GeneratedRegex(@"[.?…](?=\s|$)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"[\p{L}][\p{L}\p{N}'’]*", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Word();

    [GeneratedRegex(@"['’]\p{L}*$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Suffix();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Spaces();
}
