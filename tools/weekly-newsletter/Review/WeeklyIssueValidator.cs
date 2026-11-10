using System.Globalization;
using System.Text.RegularExpressions;
using TheBluesland.WeeklyNewsletter.Drafting;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TheBluesland.WeeklyNewsletter.Review;

/// <summary>
/// Checks <c>content/weekly/</c> before an issue reaches <c>main</c> (issue-review spec, Design
/// section 4): file name and front matter agree, the send date is the issue week's Monday, five
/// stories each with an https source, body headings match the front matter in order, every story
/// shows its source links, the AI note closes the issue, and no backups or review file is present.
/// Deterministic, no network. Messages never quote the issue's prose.
/// </summary>
/// <summary>A valid issue file as issue-delivery reads it.</summary>
public sealed record WeeklyIssue(string Id, DraftLanguage Language, string Title, DateOnly SendDate, string Body, string? Subject = null, string? Preview = null);

public static partial class WeeklyIssueValidator
{
    public const int Stories = 5;

    // Unknown keys are allowed: issue-delivery and weekly-archive may add front matter fields
    // (issue-drafting spec, Design section 4).
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Every problem in the directory as "file: message"; empty when it is valid or does not exist yet.</summary>
    public static IReadOnlyList<string> ValidateDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var problems = new List<string>();
        foreach (var path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Path.GetRelativePath(directory, path).Replace('\\', '/');
            problems.AddRange(Validate(name, File.ReadAllText(path)).Select(problem => $"{name}: {problem}"));
        }

        return problems;
    }

    /// <summary>The problems in one file, named relative to content/weekly.</summary>
    public static IReadOnlyList<string> Validate(string name, string markdown) => Check(name, markdown, out _);

    /// <summary>The issue in a valid file; null when <see cref="Validate"/> reports any problem.</summary>
    public static WeeklyIssue? Read(string name, string markdown) =>
        Check(name, markdown, out var issue).Count == 0 ? issue : null;

    private static IReadOnlyList<string> Check(string name, string markdown, out WeeklyIssue? issue)
    {
        issue = null;
        var fileName = FileName().Match(name);
        if (!fileName.Success)
        {
            return ["not an issue file (<year>-W<week>.<tr|en>.md); backups and review files are never merged."];
        }

        var problems = new List<string>();
        var issueId = $"{fileName.Groups[1].Value}-W{fileName.Groups[2].Value}";
        var year = int.Parse(fileName.Groups[1].Value, CultureInfo.InvariantCulture);
        var week = int.Parse(fileName.Groups[2].Value, CultureInfo.InvariantCulture);
        var language = DraftLanguage.All.Single(candidate => candidate.Code == fileName.Groups[3].Value);
        if (week < 1 || week > ISOWeek.GetWeeksInYear(year))
        {
            return [$"{issueId} is not an ISO week."];
        }

        var text = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var end = text.StartsWith("---\n", StringComparison.Ordinal) ? text.IndexOf("\n---\n", 3, StringComparison.Ordinal) : -1;
        if (end < 0)
        {
            return ["missing front matter (--- ... ---)."];
        }

        FrontMatter? front;
        try
        {
            front = Deserializer.Deserialize<FrontMatter?>(text[4..(end + 1)]);
        }
        catch (YamlException ex)
        {
            return [$"front matter is not valid YAML (line {ex.Start.Line})."];
        }

        if (front is null)
        {
            return ["front matter is empty."];
        }

        var monday = DateOnly.FromDateTime(ISOWeek.ToDateTime(year, week, DayOfWeek.Monday)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Require(problems, front.Issue == issueId, $"issue must be {issueId}, like the file name.");
        Require(problems, front.Language == language.Code, $"language must be {language.Code}, like the file name.");
        Require(problems, !string.IsNullOrWhiteSpace(front.Title), "title is missing.");
        Require(problems, front.SendDate == monday, $"sendDate must be {monday}, the Monday of {issueId}.");
        Require(problems, front.AiAssisted == true, "aiAssisted must be true (ADR-0006 madde 4).");
        Require(problems, front.Subject is null || (front.Subject.Trim().Length > 0 && front.Subject.Trim().Length <= DraftLanguage.MaxSubject), $"subject must be 1-{DraftLanguage.MaxSubject} characters when present.");
        Require(problems, front.Preview is null || (front.Preview.Trim().Length > 0 && front.Preview.Trim().Length <= DraftLanguage.MaxPreview), $"preview must be 1-{DraftLanguage.MaxPreview} characters when present.");

        var stories = front.Stories ?? [];
        Require(problems, stories.Count == Stories, $"stories lists {stories.Count} stories; an issue has {Stories}.");
        foreach (var (story, index) in stories.Select((story, index) => (story, index + 1)))
        {
            Require(problems, !string.IsNullOrWhiteSpace(story.Key), $"story {index} has no key.");
            Require(problems, !string.IsNullOrWhiteSpace(story.Headline), $"story {index} has no headline.");
            var sources = story.Sources ?? [];
            Require(problems, sources.Count > 0, $"story {index} has no source (ADR-0006 madde 3).");
            foreach (var source in sources)
            {
                Require(
                    problems,
                    !string.IsNullOrWhiteSpace(source.Name) && Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                    $"story {index} has a source without a name or an https URL.");
            }
        }

        var body = text[(end + 5)..];
        var matches = Heading().Matches(body);
        var headings = matches.Select(match => match.Groups[1].Value.Trim()).ToList();
        if (headings.Count != stories.Count)
        {
            problems.Add($"the body has {headings.Count} story headings (## ) but the front matter lists {stories.Count} stories.");
        }
        else
        {
            for (var i = 0; i < headings.Count; i++)
            {
                Require(problems, headings[i] == stories[i].Headline?.Trim(), $"heading {i + 1} differs from stories[{i + 1}].headline; change both together.");
                var section = body[matches[i].Index..(i + 1 < matches.Count ? matches[i + 1].Index : body.Length)];
                Require(
                    problems,
                    section.Split('\n').Any(line => line.StartsWith(language.SourcesLabel, StringComparison.Ordinal) && line.Contains("](https://", StringComparison.Ordinal)),
                    $"story {i + 1} has no \"{language.SourcesLabel}\" line with a link.");
            }
        }

        // The AI note is no longer required (ADR-0006 madde 4, 2026-11-10); older issues that end
        // with it stay valid.
        if (problems.Count == 0)
        {
            issue = new WeeklyIssue(issueId, language, front.Title!.Trim(), DateOnly.ParseExact(monday, "yyyy-MM-dd", CultureInfo.InvariantCulture), body.TrimStart('\n'), Clean(front.Subject), Clean(front.Preview));
        }

        return problems;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Require(List<string> problems, bool condition, string problem)
    {
        if (!condition)
        {
            problems.Add(problem);
        }
    }

    [GeneratedRegex(@"^(\d{4})-W(\d{2})\.(tr|en)\.md$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex FileName();

    [GeneratedRegex(@"^## (.*)$", RegexOptions.CultureInvariant | RegexOptions.Multiline, 1000)]
    private static partial Regex Heading();

    private sealed class FrontMatter
    {
        public string? Issue { get; set; }

        public string? Language { get; set; }

        public string? Title { get; set; }

        public string? Subject { get; set; }

        public string? Preview { get; set; }

        public string? SendDate { get; set; }

        public bool? AiAssisted { get; set; }

        public List<StoryEntry>? Stories { get; set; }
    }

    private sealed class StoryEntry
    {
        public string? Key { get; set; }

        public string? Headline { get; set; }

        public List<SourceEntry>? Sources { get; set; }
    }

    private sealed class SourceEntry
    {
        public string? Name { get; set; }

        public string? Url { get; set; }
    }
}
