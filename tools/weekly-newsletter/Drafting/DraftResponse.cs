using System.Text.Json;

namespace TheBluesland.WeeklyNewsletter.Drafting;

public sealed record DraftStory(string Key, string Headline, string Body, string WhyItMatters);

/// <summary>What the model writes for one language: text only, no structure the code owns.</summary>
/// <param name="Subject">E-mail subject line (at most 60 characters); null when the model left it out.</param>
/// <param name="Preview">Inbox preview line (at most 90 characters); null when the model left it out.</param>
public sealed record DraftResponse(string Intro, string Outro, IReadOnlyList<DraftStory> Stories, string? Subject = null, string? Preview = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Parses the model's JSON and checks it covers every fact-card story exactly once with
    /// non-empty text. Anything else is a <see cref="DraftFormatException"/> - the command asks once
    /// more, then fails.
    /// </summary>
    public static DraftResponse Parse(string json, FactCard card)
    {
        Document? document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new DraftFormatException($"The model's answer is not valid JSON: {ex.Message}");
        }

        if (document is null || string.IsNullOrWhiteSpace(document.Intro) || string.IsNullOrWhiteSpace(document.Outro) || document.Stories is null)
        {
            throw new DraftFormatException("The model's answer is missing intro, outro or stories.");
        }

        var stories = new List<DraftStory>();
        foreach (var story in document.Stories)
        {
            if (story is null || string.IsNullOrWhiteSpace(story.Key) || string.IsNullOrWhiteSpace(story.Headline)
                || string.IsNullOrWhiteSpace(story.Body) || string.IsNullOrWhiteSpace(story.WhyItMatters))
            {
                throw new DraftFormatException("A story in the model's answer has an empty key or text field.");
            }

            stories.Add(new DraftStory(story.Key.Trim(), story.Headline.Trim(), story.Body.Trim(), story.WhyItMatters.Trim()));
        }

        var expected = card.Stories.Select(story => story.Key).Order(StringComparer.Ordinal).ToList();
        var actual = stories.Select(story => story.Key).Order(StringComparer.Ordinal).ToList();
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            throw new DraftFormatException(
                $"The model's answer must cover each story key once. Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }

        return new DraftResponse(document.Intro.Trim(), document.Outro.Trim(), stories, Blank(document.Subject), Blank(document.Preview));
    }

    public DraftStory For(string key) => Stories.Single(story => story.Key == key);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Document(string? Intro, string? Outro, List<DocumentStory?>? Stories, string? Subject = null, string? Preview = null);

    private sealed record DocumentStory(string? Key, string? Headline, string? Body, string? WhyItMatters);
}

public sealed class DraftFormatException(string message) : Exception(message);
