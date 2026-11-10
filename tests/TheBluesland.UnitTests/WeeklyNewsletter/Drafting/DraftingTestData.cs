using System.Text.Json;
using System.Text.RegularExpressions;
using TheBluesland.UnitTests.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Drafting;
using TheBluesland.WeeklyNewsletter.Ingest;
using TheBluesland.WeeklyNewsletter.Ranking;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

internal static partial class DraftingTestData
{
    public static readonly IssueWeek Week46 = IssueWeek.ForWindow(
        new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 8, 7, 0, 0, TimeSpan.Zero));

    public static IReadOnlyList<ClusterInput> W46Articles()
    {
        using var week = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "newsletter-clustering", "2026-W46.json")));
        return week.RootElement.GetProperty("articles").EnumerateArray()
            .Select(article => new ClusterInput(
                article.GetProperty("id").GetInt64(),
                article.GetProperty("source").GetString()!,
                article.GetProperty("title").GetString()!,
                article.GetProperty("summary").GetString(),
                article.GetProperty("url").GetString()!,
                article.GetProperty("effectiveAt").GetDateTimeOffset()))
            .ToList();
    }

    /// <summary>The real W46 fact card: fixture articles, repository clustering/ranking settings and feed names.</summary>
    public static FactCard W46Card()
    {
        var clustering = ClusteringOptionsLoader.Parse(File.ReadAllText(RepoFiles.Find("tools/weekly-newsletter/clustering.yml"))).Options!;
        // The approved sample issue (voice guide) is mapped onto the picks in their W46 order, which
        // predates the brand bonus; ranking without it keeps that calibration stable.
        var ranking = RankingOptionsLoader.Parse(File.ReadAllText(RepoFiles.Find("tools/weekly-newsletter/ranking.yml"))).Options! with { BrandBonus = 0 };
        var names = FeedConfigLoader.Parse(File.ReadAllText(RepoFiles.Find("tools/weekly-newsletter/feeds.yml"))).Feeds.ToDictionary(feed => feed.Id, feed => feed.Name);
        var result = StoryRanker.Rank(StoryClusterer.Cluster(W46Articles(), clustering), ranking, clustering);
        return FactCard.From(result, names, clustering.SummaryCharacters);
    }

    public static string VoiceGuide() => File.ReadAllText(RepoFiles.Find("docs/weekly/ses-rehberi.md"));

    /// <summary>
    /// The approved sample issue from the voice guide (section "Örnek sayı"), parsed into the model's
    /// answer shape and mapped onto the W46 picks in order. The validator must pass it untouched.
    /// </summary>
    public static DraftResponse SampleIssue(string languageHeading, FactCard card)
    {
        var guide = VoiceGuide();
        var start = guide.IndexOf($"### {languageHeading}", StringComparison.Ordinal);
        var next = guide.IndexOf("\n### ", start + 4, StringComparison.Ordinal);
        var section = next < 0 ? guide[start..] : guide[start..next];
        var paragraphs = section
            .Split('\n')
            .Where(line => line.StartsWith('>'))
            .Select(line => line.TrimStart('>').Trim())
            .Aggregate(new List<List<string>> { new() }, (groups, line) =>
            {
                if (line.Length == 0)
                {
                    groups.Add([]);
                }
                else
                {
                    groups[^1].Add(line);
                }

                return groups;
            })
            .Where(group => group.Count > 0)
            .Select(group => string.Join(' ', group))
            .ToList();

        // [0] title, [1] intro, [2..6] stories, [7] outro, [8] AI note.
        var picks = card.Picks.ToList();
        var stories = paragraphs.Skip(2).Take(5).Select((paragraph, index) =>
        {
            var story = StoryParagraph().Match(paragraph);
            return new DraftStory(picks[index].Key, story.Groups["headline"].Value.Trim(), story.Groups["body"].Value.Trim(), story.Groups["why"].Value.Trim());
        }).ToList();
        var subject = SampleLine(section, label: languageHeading == "English" ? "Subject" : "Konu");
        var preview = SampleLine(section, label: languageHeading == "English" ? "Preview" : "Önizleme");
        return new DraftResponse(paragraphs[1], paragraphs[7], stories, subject, preview);
    }

    private static string SampleLine(string section, string label) =>
        section.Split('\n').Single(line => line.StartsWith($"{label}: `", StringComparison.Ordinal))[(label.Length + 3)..].TrimEnd('`');

    [GeneratedRegex(@"^\*\*(?<headline>[^*]+)\*\*\s*(?<body>.+?)\s*\*[^*]+:\*\s*(?<why>.+)$")]
    private static partial Regex StoryParagraph();

    /// <summary>A model answer covering every story in the card with plain, card-safe text.</summary>
    public static string FakeAnswer(IEnumerable<string> keys) =>
        JsonSerializer.Serialize(new
        {
            intro = "Five stories, five minutes.",
            outro = "See you next Monday.",
            subject = "Two hooks · TBL Weekly",
            preview = "A different preview line.",
            stories = keys.Select(key => new { key, headline = $"Headline {key}", body = "Body text.", whyItMatters = "It matters." }),
        });

    /// <summary>A Gemini HTTP response wrapping <paramref name="text"/> as the first candidate.</summary>
    public static string GeminiEnvelope(string text) =>
        JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text } } } } } });
}

/// <summary>Answers each request with the next queued response and records what was sent.</summary>
internal sealed class QueueHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
{
    private int _next;

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        var respond = responses[Math.Min(_next++, responses.Length - 1)];
        return respond(request);
    }
}
