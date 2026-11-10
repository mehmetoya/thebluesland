namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>
/// Builds the one prompt per language (issue-drafting spec, Design section 2): the approved voice
/// guide in full, the language to write in, the rules, and the fact card. Nothing else - no URL,
/// no Spotify data, no subscriber data (ADR-0006 madde 1).
/// </summary>
public static class DraftPromptBuilder
{
    public static string Build(DraftLanguage language, string voiceGuide, FactCard card) =>
        $"""
        You write TheBluesland Weekly, a Monday music-news newsletter. Follow the voice guide below
        exactly; its sample issue is the reference for tone and length.

        === VOICE GUIDE ===
        {voiceGuide}
        === END VOICE GUIDE ===

        Write the issue in {language.Name}, fresh in that language (never a translation).

        Rules:
        - Use only facts from the fact card below. If a detail is not in the card, leave it out.
          No dates, numbers, ages, places, quotes or claims that are not in the card.
        - Do not copy sentences from the summaries; rewrite everything in the guide's voice.
        - No URLs, no links, no Markdown, no emoji, no exclamation marks.
        - Write every story in the card, picks and backups, each under its exact story key.
        - headline: at most 70 characters. body: 2-3 sentences. whyItMatters: 1-2 short sentences,
          without a "Why it matters" / "Neden önemli" label.
        - intro: 1-2 sentences about the week, referring only to the picks. outro: 1-2 short sentences.

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
        required = new[] { "intro", "outro", "stories" },
    };
}
