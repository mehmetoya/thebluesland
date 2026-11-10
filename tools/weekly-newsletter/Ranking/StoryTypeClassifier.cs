using TheBluesland.WeeklyNewsletter.Clustering;

namespace TheBluesland.WeeklyNewsletter.Ranking;

/// <summary>
/// Gives a story its type from the headlines of all its articles (story-ranking spec, Design
/// section 1): rules are tried in ranking.yml order, the first whose phrase appears as whole words
/// wins, after that rule's "exclude" phrases are removed. No match → <see cref="DefaultType"/>.
/// </summary>
public static class StoryTypeClassifier
{
    public const string DefaultType = "release";

    public static StoryTypeRule Classify(Story story, IReadOnlyList<StoryTypeRule> rules)
    {
        var headlines = TextNormalizer.Flatten(string.Join(" . ", story.Articles.Select(article => article.Title)));
        foreach (var rule in rules)
        {
            var text = rule.Exclude.Aggregate(headlines, (current, phrase) => current.Replace($" {phrase} ", " ", StringComparison.Ordinal));
            if (rule.Phrases.Any(phrase => text.Contains($" {phrase} ", StringComparison.Ordinal)))
            {
                return rule;
            }
        }

        return new StoryTypeRule(DefaultType, 0, false, [], []);
    }

    /// <summary>
    /// Brand fit (story-ranking spec, Design section 1): a brand keyword appears in any article's
    /// headline or summary, or a core source carried the story.
    /// </summary>
    public static bool FitsBrand(Story story, IReadOnlyList<string> keywords, IReadOnlyList<string> coreSources) =>
        story.Sources.Any(coreSources.Contains) || MatchesGenre(story, keywords);

    /// <summary>Whether any genre keyword appears in the story's headlines or summaries.</summary>
    public static bool MatchesGenre(Story story, IReadOnlyList<string> keywords)
    {
        var text = TextNormalizer.Flatten(string.Join(" . ", story.Articles.Select(article => $"{article.Title} . {article.Summary}")));
        return keywords.Any(keyword => text.Contains($" {keyword} ", StringComparison.Ordinal));
    }
}
