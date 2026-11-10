using System.Globalization;
using System.Text.RegularExpressions;

namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>
/// Finds the "Month day" dates the articles of one story state and flags the ones the sources disagree on
/// (one source says Jan 27, two others Jan 29). The prompt tells the model to leave such a date out; the
/// pull request lists the conflict so the editor can check the original articles.
/// </summary>
public static partial class SourceDateConflicts
{
    private const string Months = "Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?";

    private static readonly string[] Abbreviations = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    [GeneratedRegex(@"\b(?<m>" + Months + @")\.?\s+(?<d>\d{1,2})(?:st|nd|rd|th)?\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex MonthDay();

    /// <summary>
    /// "Jan 27 (Stereogum) / Jan 29 (Ultimate Classic Rock, Consequence)" when at least two dates are each
    /// stated by sources that do not state the other; null when the sources agree or give one date only.
    /// </summary>
    public static string? Describe(FactStory story)
    {
        var bySource = new Dictionary<(int Month, int Day), List<string>>();
        foreach (var article in story.Articles)
        {
            foreach (Match match in MonthDay().Matches($"{article.Title} {article.Summary}"))
            {
                var month = Array.IndexOf(Abbreviations, match.Groups["m"].Value[..3].ToLowerInvariant()) + 1;
                var day = int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture);
                if (month == 0 || day is < 1 or > 31)
                {
                    continue;
                }

                if (!bySource.TryGetValue((month, day), out var sources))
                {
                    bySource[(month, day)] = sources = [];
                }

                if (!sources.Contains(article.SourceName))
                {
                    sources.Add(article.SourceName);
                }
            }
        }

        var dates = bySource.Keys.Order().ToList();
        var conflict = dates.Any(first => dates.Any(second => first != second && !bySource[first].Intersect(bySource[second]).Any()));
        return conflict
            ? string.Join(" / ", dates.Select(date => $"{CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(date.Month)} {date.Day} ({string.Join(", ", bySource[date])})"))
            : null;
    }
}
