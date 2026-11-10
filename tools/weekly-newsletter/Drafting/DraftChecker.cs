using System.Text;
using System.Text.Json;
using TheBluesland.WeeklyNewsletter.Reporting;

namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>The final checker's verdict for one language (issue-drafting spec, Design section 3b).</summary>
/// <param name="Passed">True only when the checker answered <c>pass: true</c> with no problems; a checker that could not answer never passes.</param>
/// <param name="Problems">What the checker found (model text, to be escaped before it reaches Markdown).</param>
/// <param name="Failure">Why the checker call itself failed (one line); null when it answered.</param>
public sealed record CheckVerdict(bool Passed, IReadOnlyList<string> Problems, string? Failure = null)
{
    public static CheckVerdict Unavailable(string failure) => new(false, [], failure);
}

/// <summary>
/// The final checker ("son kontrolcü", issue-drafting spec, Design section 3b): one more Gemini request per
/// language, in a reviewer role. It sees the fact card and the issue's published text and says whether every
/// claim is grounded, nothing is invented, "why it matters" adds something, the language is natural and the
/// subject and preview fit. There is no human review after it (ADR-0006 madde 2), so a checker that fails
/// to answer counts as not passed.
/// </summary>
public static class DraftChecker
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Gemini <c>responseSchema</c> for the verdict.</summary>
    public static object ResponseSchema { get; } = new
    {
        type = "OBJECT",
        properties = new
        {
            pass = new { type = "BOOLEAN" },
            problems = new { type = "ARRAY", items = new { type = "STRING" } },
        },
        required = new[] { "pass", "problems" },
    };

    /// <summary>
    /// The text a subscriber reads, as plain lines: subject, preview, intro, each pick, outro. Backups are
    /// not published and source links belong to the code, so neither is shown (no URL reaches a prompt, ADR-0006 madde 1).
    /// </summary>
    public static string IssueText(DraftResponse draft, FactCard card, DraftLanguage language)
    {
        var text = new StringBuilder();
        text.Append("Subject: ").Append(draft.Subject ?? "(none)").Append('\n');
        text.Append("Preview: ").Append(draft.Preview ?? "(none)").Append("\n\n");
        text.Append(draft.Intro).Append("\n\n");
        foreach (var pick in card.Picks)
        {
            var story = draft.For(pick.Key);
            text.Append("Story ").Append(pick.Key).Append(" - headline: ").Append(story.Headline).Append('\n');
            text.Append("Body: ").Append(story.Body).Append('\n');
            text.Append(language.WhyLabel).Append(' ').Append(story.WhyItMatters).Append("\n\n");
        }

        text.Append(draft.Outro).Append('\n');
        return text.ToString();
    }

    /// <summary>Makes exactly one request through <paramref name="generate"/>; every failure except the caller's cancellation is a not-passed verdict.</summary>
    public static async Task<CheckVerdict> RunAsync(
        Func<string, CancellationToken, Task<string>> generate,
        DraftResponse finalDraft,
        FactCard card,
        DraftLanguage language,
        DateOnly sendDate,
        CancellationToken cancellationToken)
    {
        try
        {
            var prompt = DraftPromptBuilder.BuildCheck(language, card, sendDate, IssueText(finalDraft, card, language));
            return Parse(await generate(prompt, cancellationToken));
        }
        catch (Exception ex) when ((ex is HttpRequestException or DraftFormatException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return CheckVerdict.Unavailable(OneLine(ex.Message));
        }
    }

    /// <summary>Reads <c>{ "pass": bool, "problems": [string] }</c>; anything else is a <see cref="DraftFormatException"/>.</summary>
    public static CheckVerdict Parse(string json)
    {
        Document? document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new DraftFormatException($"The checker's answer is not valid JSON: {ex.Message}");
        }

        if (document is null || document.Pass is null || document.Problems is null)
        {
            throw new DraftFormatException("The checker's answer is missing pass or problems.");
        }

        var problems = document.Problems.Where(problem => !string.IsNullOrWhiteSpace(problem)).Select(problem => OneLine(problem!)).ToList();
        // "pass: true" with problems listed contradicts itself; with no human after the checker, doubt means not passed.
        return new CheckVerdict(document.Pass.Value && problems.Count == 0, problems);
    }

    /// <summary>The verdict as Markdown lines for the PR body (Turkish, for Mehmet); the model's text is escaped.</summary>
    public static string ToMarkdown(CheckVerdict? check)
    {
        if (check is null)
        {
            return string.Empty;
        }

        if (check.Failure is not null)
        {
            return $"Son kontrol: **geçmedi** (kontrolcü yanıt veremedi: {MarkdownText.Cell(check.Failure)})\n\n";
        }

        if (check.Passed)
        {
            return "Son kontrol: **geçti**\n\n";
        }

        var list = string.Concat(check.Problems.Select(problem => $"- {MarkdownText.Cell(problem)}\n"));
        return $"Son kontrol: **geçmedi**\n\n{list}\n";
    }

    private static string OneLine(string message)
    {
        var flat = string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > 300 ? flat[..299] + "…" : flat;
    }

    private sealed record Document(bool? Pass, List<string?>? Problems);
}
