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
        exactly; its sample issue is a reference for tone and length only, never a source of facts.

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

        Facts:
        - Use only facts from the fact card below. If a detail is not in the card, leave it out.
          No dates, numbers, ages, places, quotes, background or claims that are not in the card.
        - Do not copy sentences from the summaries; rewrite everything in the guide's voice.
        - Cover every number and name that the card's headlines for a story share (for example an age,
          a show name, a title number), unless it adds nothing.
        - No URLs, no links, no Markdown, no emoji, no exclamation marks.
        - Write every story in the card, picks and backups, each under its exact story key.

        Shape:
        - headline: at most 70 characters. It says who + what happened; add identity for names readers
          may not know (band, role). No abbreviations of institutions in headlines.
        - body: 2-3 sentences. One verb per event (a death is "died" / "öldü" or "hayatını kaybetti", never
          a second phrase for the same event). Subject at the start of the sentence. Two sentences must
          not repeat each other. No filler like "X grubundan" / "the band X".
        - whyItMatters: 1-2 short sentences, without a "Why it matters" / "Neden önemli" label. It states
          a concrete consequence or comparison: who it changes what for, using only facts in the card. No
          claims about influence, attitude, legacy or trends that the card does not state. Avoid stock
          phrases such as "pioneering", "reach well beyond", "proving", "coveted", "öncü bir grup",
          "geniş kitle", "doğrudan belirliyor", "kendine yer buluyor".
        - intro: 1-2 sentences referring only to the picks, ending with the sentence "{language.IntroTagline}".
        - outro: 1-2 short sentences. Do not write the AI note or a signature; the code adds it.
        - subject: at most {DraftLanguage.MaxSubject} characters, two story hooks from the picks, ending with
          "{DraftLanguage.SubjectTag}". preview: at most {DraftLanguage.MaxPreview} characters, adds something and
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
