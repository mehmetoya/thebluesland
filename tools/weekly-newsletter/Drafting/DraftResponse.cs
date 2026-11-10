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

        // Every pick must be written exactly once and nothing outside the card may appear. A backup
        // the model skipped (weaker fallback models do) is filled from the card itself and marked,
        // so one missing backup no longer throws the whole draft away.
        var known = card.Stories.Select(story => story.Key).ToHashSet(StringComparer.Ordinal);
        var written = stories.Select(story => story.Key).ToList();
        var picks = card.Picks.Select(story => story.Key).ToList();
        if (written.Count != written.Distinct(StringComparer.Ordinal).Count()
            || written.Any(key => !known.Contains(key))
            || picks.Any(key => !written.Contains(key, StringComparer.Ordinal)))
        {
            var expected = card.Stories.Select(story => story.Key).Order(StringComparer.Ordinal);
            var actual = written.Order(StringComparer.Ordinal);
            throw new DraftFormatException(
                $"The model's answer must cover each pick once and only card stories. Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }

        foreach (var backup in card.Backups.Where(story => !written.Contains(story.Key, StringComparer.Ordinal)))
        {
            var lead = backup.Articles[0];
            stories.Add(new DraftStory(backup.Key, lead.Title, string.IsNullOrWhiteSpace(lead.Summary) ? lead.Title : lead.Summary.Trim(), MissingBackupNote));
        }

        return new DraftResponse(document.Intro.Trim(), document.Outro.Trim(), stories, Blank(document.Subject), Blank(document.Preview));
    }

    /// <summary>Marks a backup the model did not write; the editor sees the card's own text instead.</summary>
    public const string MissingBackupNote = "(Model bu yedek haberi yazmadı / the model skipped this backup; card text shown.)";

    public DraftStory For(string key) => Stories.Single(story => story.Key == key);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Document(string? Intro, string? Outro, List<DocumentStory?>? Stories, string? Subject = null, string? Preview = null);

    private sealed record DocumentStory(string? Key, string? Headline, string? Body, string? WhyItMatters);
}

public sealed class DraftFormatException(string message) : Exception(message);
