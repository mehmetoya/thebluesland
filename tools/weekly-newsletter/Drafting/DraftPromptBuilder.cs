namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>
/// Builds the one prompt per language (issue-drafting spec, Design section 2): the approved voice
/// guide in full, the language to write in, the rules, and the fact card. Nothing else - no URL,
/// no Spotify data, no subscriber data (ADR-0006 madde 1).
/// </summary>
public static class DraftPromptBuilder
{
    private const string SampleHeading = "## Örnek sayı";

    /// <summary>Characters left for the story hooks once the brand tag is counted into the subject limit.</summary>
    private static readonly int SubjectRoom = DraftLanguage.MaxSubject - DraftLanguage.SubjectTag.Length;

    /// <summary>
    /// The prompt for one language. <paramref name="lite"/> is the variant for the lighter fallback
    /// model: no sample issue (it copies it), the guide's tone and language sections only, and the rules
    /// as a short numbered checklist after the fact card, where a small model still follows them.
    /// </summary>
    public static string Build(DraftLanguage language, string voiceGuide, FactCard card, DateOnly sendDate, bool lite = false) =>
        lite ? BuildLite(language, voiceGuide, card, sendDate) : BuildFull(language, voiceGuide, card, sendDate);

    /// <summary>The guide's sample-issue section ("## Örnek sayı" up to the next "## " heading), or empty.</summary>
    private static (string Without, string Sample) SplitSample(string voiceGuide)
    {
        var marker = voiceGuide.IndexOf(SampleHeading, StringComparison.Ordinal);
        if (marker < 0 || (marker > 0 && voiceGuide[marker - 1] != '\n'))
        {
            return (voiceGuide, string.Empty);
        }

        var next = voiceGuide.IndexOf("\n## ", marker + SampleHeading.Length, StringComparison.Ordinal);
        return next < 0
            ? (voiceGuide[..marker].TrimEnd() + "\n", voiceGuide[marker..])
            : (voiceGuide[..marker] + voiceGuide[(next + 1)..], voiceGuide[marker..next]);
    }

    /// <summary>
    /// The sample issue's own text (its quoted issue lines and subject/preview lines), for the check that the
    /// model did not copy it; the surrounding rules prose is left out.
    /// </summary>
    public static string SampleText(string voiceGuide) =>
        string.Join(
            '\n',
            SplitSample(voiceGuide).Sample
                .Split('\n')
                .Where(line => line.StartsWith('>') || line.StartsWith("Konu:", StringComparison.Ordinal) || line.StartsWith("Önizleme:", StringComparison.Ordinal)
                    || line.StartsWith("Subject:", StringComparison.Ordinal) || line.StartsWith("Preview:", StringComparison.Ordinal))
                .Select(line => line.TrimStart('>').Trim()));

    private static string BuildLite(DraftLanguage language, string voiceGuide, FactCard card, DateOnly sendDate) =>
        $"""
        You write TheBluesland Weekly, a Monday music-news newsletter. Follow the voice guide's tone and
        language sections. Write the issue in {language.Name}, fresh in that language (never a translation).
        Both language versions carry the same facts; only the style differs.

        === VOICE GUIDE (tone and languages) ===
        {SplitSample(voiceGuide).Without}
        === END VOICE GUIDE ===

        === FACT CARD ===
        {card.ToPromptText()}=== END FACT CARD ===

        Check every item before you answer:
        1. Use only facts from the fact card; leave out any detail it does not give. No URLs, Markdown, emoji or "!".
        2. The issue is sent on {sendDate:yyyy-MM-dd}. Events before that date: past tense. An announcement for a date
           before it: "said they would skip" / "katılmayacağını açıkladı", never how it turned out. Something not
           started yet stays future: "will count from January 16" / "16 Ocak'tan itibaren sayılacak".
        3. Never write "this week", a weekday, "a year ago", "last week", "months ago" or "a decade" unless the card says it.
        4. For every story: who (with identity: band, role), what happened, when (date), where (city) and every proper
           name the card's headlines use (songs in double quotes, institutions in full on first mention). Same list in both languages.
        5. Write roles as the card words them ("founder", "inventor", "first", "only" only if a summary says it). If two
           articles disagree on a date or number, leave it out.
        6. headline: at most 70 characters, who + what happened, no institution abbreviations ("Rock Hall" is allowed; the body uses the full name).
        7. body: 2-3 sentences, one verb per event, no sentence copied from a summary, no "X grubundan" / "the band X" filler.
        8. whyItMatters: 1-2 short sentences, no label. It adds context NOT in the body: history, a consequence, or what to
           listen to / watch for - taken only from the fact card. Never restate the headline or body, never say the obvious
           (who appears, what happens), no claim of significance, influence or legacy the card does not state. No stock phrases
           ("pioneering", "geniş kitle", "pek çok müzisyen", "a favorite of"). If the card has nothing more, give the next step (date, place, who is affected).
        9. intro: 1-2 sentences about at most three picks, ending with "{language.IntroTagline}". outro: 1-2 short sentences, no AI, no signature.
        10. subject: at most {DraftLanguage.MaxSubject} characters in total, counting the ending "{DraftLanguage.SubjectTag}" ({DraftLanguage.SubjectTag.Length} characters),
            so the hooks get at most {SubjectRoom} characters. Two short hooks; if it runs over, drop the second hook and never cut a phrase in half.
            preview: at most {DraftLanguage.MaxPreview} characters, adds something, does not repeat the subject.
        11. Write every story in the card, picks and backups, each under its exact story key. A missing backup is an error.
        """;

    private static string BuildFull(DraftLanguage language, string voiceGuide, FactCard card, DateOnly sendDate) =>
        $"""
        You write TheBluesland Weekly, a Monday music-news newsletter. Follow the voice guide below
        exactly; its sample issue is fictional and a reference for tone and length only, never a source of
        facts: nothing in it (no phrase, sentence part or fact) goes into the issue.

        === VOICE GUIDE ===
        {voiceGuide}
        === END VOICE GUIDE ===

        Write the issue in {language.Name}, fresh in that language (never a translation).
        Both language versions carry the same facts; only the style differs.

        Time:
        - The issue is sent on {sendDate:yyyy-MM-dd}. Events before that date are past: use past tense.
        - Never say "this week" / "bu hafta"; the issue covers the previous week.
        - Do not write a weekday, "a year ago", "last week", "months ago" or "a decade" unless the fact
          card says exactly that.
        - An event the card dates between the window and the send date ({sendDate:yyyy-MM-dd}) will be over when
          readers open the issue. Report the announcement in past tense ("said they would skip" /
          "katılmayacağını açıkladı") and never claim how it turned out. Never use future tense ("will",
          "-ecek/-acak") for any event dated before the send date.
        - Something that has not started yet is not past: "will count from January 16" / "16 Ocak'tan
          itibaren sayılacak", never "started" / "başladı".

        Facts:
        - Use only facts from the fact card below. If a detail is not in the card, leave it out.
          No dates, numbers, ages, places, quotes, background or claims that are not in the card.
        - Do not copy sentences from the summaries; rewrite everything in the guide's voice.
        - For every story include, when the card gives them: who (with identity: band, role, "two surviving
          Beatles"), what happened, when (date), where (city), and every proper name the card's headlines use
          for an event, song (in double quotes) or institution (full name on first mention). Both languages
          must carry this same list.
        - Write roles exactly as the card words them: "founder", "inventor", "first", "only" only when a
          summary says it; "co-invented" stays "co-inventor" / "mucitlerinden".
        - If two articles in the card disagree on a date or number, leave that detail out.
        - No URLs, no links, no Markdown, no emoji, no exclamation marks.
        - Write every story in the card, picks and backups, each under its exact story key.
        - Write every backup too; a missing backup is an error.

        Shape:
        - headline: at most 70 characters. It says who + what happened; add identity for names readers
          may not know (band, role). No abbreviations of institutions in headlines, except that headlines may use the short form
          "Rock Hall"; the body names the full institution first.
        - body: 2-3 sentences. One verb per event (a death is "died" / "öldü" or "hayatını kaybetti", never
          a second phrase for the same event). Subject at the start of the sentence. Two sentences must
          not repeat each other. No filler like "X grubundan" / "the band X".
        - whyItMatters: 1-2 short sentences, without a "Why it matters" / "Neden önemli" label. It states
          a concrete consequence or comparison and must add context that is NOT in the headline or body:
          history, a consequence, or what to listen to / watch for, taken only from the fact card. Never
          restate the headline or body in other words, and never say the obvious (who appears, what
          happens). If the card offers no such context, give the concrete next step (date, place, who is
          affected). No claims about influence, attitude, legacy, importance or trends that the card does
          not state. Avoid stock phrases such as "pioneering", "reach well beyond", "proving", "coveted",
          "öncü bir grup", "geniş kitle", "doğrudan belirliyor", "kendine yer buluyor", "pek çok müzisyen",
          "birçok müzisyen", "many musicians", "a favorite of", "yeniden yer alacak".
        - intro: 1-2 sentences referring only to the picks, ending with the sentence "{language.IntroTagline}".
        - outro: 1-2 short sentences. Do not mention AI or write a signature.
        - subject: at most {DraftLanguage.MaxSubject} characters in total, counting the ending
          "{DraftLanguage.SubjectTag}" ({DraftLanguage.SubjectTag.Length} characters), so the story hooks get at most {SubjectRoom}
          characters. Two story hooks from the picks. If it runs over, drop the second hook; never abbreviate
          and never end in the middle of a phrase.
          preview: at most {DraftLanguage.MaxPreview} characters, adds something and
          does not repeat the subject.

        === FACT CARD ===
        {card.ToPromptText()}=== END FACT CARD ===
        """;

    /// <summary>
    /// The one repair request per language (issue-drafting spec, Design section 3a): the model's own JSON
    /// answer, the fact card and the plain list of warnings to fix. No voice-guide sample, so nothing
    /// is there to copy. Same data boundary as <see cref="Build"/> (ADR-0006 madde 1).
    /// </summary>
    public static string BuildRepair(DraftLanguage language, FactCard card, DateOnly sendDate, string currentJson, IReadOnlyList<DraftWarning> warnings)
    {
        var list = string.Join('\n', warnings.Select(warning => $"- [{warning.Where}] {warning.Message}"));
        return $"""
            You edit a draft of TheBluesland Weekly, a Monday music-news newsletter, written in {language.Name}.
            An automatic check found the problems listed below in the draft answer. Return the corrected full answer
            as JSON in the same schema. Change only the fields the problems point to; copy every other field word for word.

            PROBLEMS:
            {list}

            Rules for the rewrite:
            - Use only facts from the fact card. Keep every number and proper name the text already has; add no URLs, Markdown, emoji or "!".
            - whyItMatters (1-2 short sentences, no label) must add context NOT in the headline or body: history, a consequence, or what to
              listen to / watch for, taken only from the fact card. Never restate the body, never say the obvious, no stock phrases and no claim of
              significance the card does not state. If the card has nothing more, give the next step (date, place, who is affected).
            - The issue is sent on {sendDate:yyyy-MM-dd}. Events before that date are past: past tense. Never "this week", weekdays or time spans the card does not state.
            - subject: at most {DraftLanguage.MaxSubject} characters in total, counting the ending "{DraftLanguage.SubjectTag}" ({DraftLanguage.SubjectTag.Length} characters),
              so the hooks get at most {SubjectRoom} characters; one or two short hooks, never cut in the middle of a phrase.
              preview: at most {DraftLanguage.MaxPreview} characters, does not repeat the subject.
            - Keep every story key and the same stories.

            === DRAFT ANSWER (JSON) ===
            {currentJson}
            === END DRAFT ANSWER ===

            === FACT CARD ===
            {card.ToPromptText()}=== END FACT CARD ===
            """;
    }

    /// <summary>Gemini <c>responseSchema</c> for <see cref="DraftResponse"/>.</summary>
    public static object ResponseSchema { get; } = new
    {
        type = "OBJECT",
        properties = new
        {
            intro = new { type = "STRING" },
            outro = new { type = "STRING" },
            subject = new { type = "STRING" },
            preview = new { type = "STRING" },
            stories = new
            {
                type = "ARRAY",
                items = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        key = new { type = "STRING" },
                        headline = new { type = "STRING" },
                        body = new { type = "STRING" },
                        whyItMatters = new { type = "STRING" },
                    },
                    required = new[] { "key", "headline", "body", "whyItMatters" },
                },
            },
        },
        required = new[] { "intro", "outro", "subject", "preview", "stories" },
    };
}
