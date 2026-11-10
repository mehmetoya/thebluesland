namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>
/// Builds the one prompt per language (issue-drafting spec, Design section 2): the approved voice
/// guide in full, the language to write in, the rules, and the fact card. Nothing else - no URL,
/// no Spotify data, no subscriber data (ADR-0006 madde 1).
/// </summary>
public static class DraftPromptBuilder
{
    public static string Build(DraftLanguage language, string voiceGuide, FactCard card, DateOnly sendDate) =>
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
          a concrete consequence or comparison: who it changes what for, using only facts in the card. It
          must add one fact that the headline and body do not already contain; restating them is wrong. If
          the card offers no such fact, give the concrete next step (date, place, who is affected). No
          claims about influence, attitude, legacy or trends that the card does not state. Avoid stock
          phrases such as "pioneering", "reach well beyond", "proving", "coveted", "öncü bir grup",
          "geniş kitle", "doğrudan belirliyor", "kendine yer buluyor", "pek çok müzisyen", "birçok müzisyen",
          "many musicians", "a favorite of", "yeniden yer alacak".
        - intro: 1-2 sentences referring only to the picks, ending with the sentence "{language.IntroTagline}".
        - outro: 1-2 short sentences. Do not mention AI or write a signature.
        - subject: at most {DraftLanguage.MaxSubject} characters, two story hooks from the picks, ending with
          "{DraftLanguage.SubjectTag}". If the subject runs over, drop the second hook; never abbreviate.
          preview: at most {DraftLanguage.MaxPreview} characters, adds something and
          does not repeat the subject.

        === FACT CARD ===
        {card.ToPromptText()}=== END FACT CARD ===
        """;

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
