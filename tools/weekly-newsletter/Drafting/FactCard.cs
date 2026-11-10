using System.Globalization;
using System.Text;
using TheBluesland.WeeklyNewsletter.Ranking;

namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>One article as the model may see it - no URL (issue-drafting spec, Design section 1).</summary>
public sealed record FactArticle(string SourceName, DateTimeOffset PublishedAt, string Title, string? Summary);

/// <summary>A link the code (never the model) puts under a story.</summary>
public sealed record SourceLink(string Name, string Url);

public sealed record FactStory(
    string Key,
    int Position,
    bool IsBackup,
    string Type,
    string Why,
    IReadOnlyList<FactArticle> Articles,
    IReadOnlyList<SourceLink> Links)
{
    /// <summary>Everything the model saw for this story: source names, headlines and summaries.</summary>
    public string AllText() =>
        string.Join(" \n ", Articles.SelectMany(article => new[] { article.SourceName, article.Title, article.Summary ?? string.Empty }));
}

/// <summary>
/// The week's facts and nothing else (ADR-0006 madde 1): the ranked picks and backups, each with
/// its articles' source, date, headline and the first summary characters. URLs are kept apart in
/// <see cref="FactStory.Links"/> and never rendered into the prompt.
/// </summary>
public sealed record FactCard(IReadOnlyList<FactStory> Stories)
{
    public IEnumerable<FactStory> Picks => Stories.Where(story => !story.IsBackup);

    public IEnumerable<FactStory> Backups => Stories.Where(story => story.IsBackup);

    public static FactCard From(RankingResult ranking, IReadOnlyDictionary<string, string> sourceNames, int summaryCharacters)
    {
        string Name(string source) => sourceNames.GetValueOrDefault(source, source);

        FactStory ToFact(RankedStory ranked, int position, bool isBackup) => new(
            ranked.Story.Key,
            position,
            isBackup,
            ranked.Type,
            RankReportFormatter.Why(ranked),
            ranked.Story.Articles
                .Select(article => new FactArticle(
                    Name(article.Source),
                    article.EffectiveAt,
                    article.Title,
                    article.Summary is { Length: > 0 } summary && summary.Length > summaryCharacters ? summary[..summaryCharacters] : article.Summary))
                .ToList(),
            ranked.Story.Sources
                .Select(source => new SourceLink(Name(source), ranked.Story.Articles.First(article => article.Source == source).Url))
                .ToList());

        return new FactCard(
            ranking.Picks.Select((pick, index) => ToFact(pick, index + 1, false))
                .Concat(ranking.Backups.Select((backup, index) => ToFact(backup, index + 1, true)))
                .ToList());
    }

    /// <summary>The fact card exactly as the prompt shows it.</summary>
    public string ToPromptText()
    {
        var text = new StringBuilder();
        foreach (var story in Stories)
        {
            text.Append(CultureInfo.InvariantCulture, $"Story key: {story.Key} ({(story.IsBackup ? "backup" : "pick")} {story.Position})\n");
            text.Append(CultureInfo.InvariantCulture, $"Type: {story.Type} · chosen because: {story.Why}\n");
            foreach (var article in story.Articles)
            {
                text.Append(CultureInfo.InvariantCulture, $"- {article.SourceName}, {article.PublishedAt.UtcDateTime:yyyy-MM-dd}: \"{article.Title}\"");
                text.Append(string.IsNullOrWhiteSpace(article.Summary) ? "\n" : $" — {article.Summary}\n");
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Everything a draft may draw facts from, for the validator.</summary>
    public string AllText() => string.Join(" \n ", Stories.Select(story => story.AllText()));
}
