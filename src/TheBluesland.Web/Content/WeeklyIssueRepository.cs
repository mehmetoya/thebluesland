using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TheBluesland.Web.Content;

/// <summary>One TheBluesland Weekly issue in one language, as /weekly shows it.</summary>
public sealed record WeeklyIssue(string Id, string Language, string Title, DateOnly SendDate, string Html)
{
    public string Path => $"/weekly/{Id}/{Language}";

    public string LanguageName => Language == "tr" ? "Türkçe" : "English";
}

/// <summary>
/// The weekly archive (docs/specs/weekly-newsletter.md, module weekly-archive): merged issue files in
/// <c>content/weekly/</c>, read once per deployment like the playlist catalogue. An issue appears
/// only from its send date (Europe/Istanbul), so a Sunday merge is not on the web before the e-mail.
/// The body is rendered with the curator-note sanitizer (raw HTML off, executable URL schemes
/// neutralised). The web app never talks to the e-mail provider.
/// </summary>
public sealed partial class WeeklyIssueRepository
{
    public const string ContentDirectoryConfigKey = "WeeklyContent:Directory";

    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly Lazy<IReadOnlyList<WeeklyIssue>> _all;
    private readonly TimeProvider _clock;

    public WeeklyIssueRepository(IConfiguration configuration, TimeProvider? clock = null)
    {
        var directory = configuration[ContentDirectoryConfigKey]
            ?? System.IO.Path.Combine(Directory.GetCurrentDirectory(), "content", "weekly");
        _clock = clock ?? TimeProvider.System;
        _all = new Lazy<IReadOnlyList<WeeklyIssue>>(() => Load(directory));
    }

    /// <summary>Published issues, newest first; Turkish before English within an issue.</summary>
    public IReadOnlyList<WeeklyIssue> Published()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), Istanbul).DateTime);
        return _all.Value.Where(issue => issue.SendDate <= today).ToList();
    }

    public WeeklyIssue? Find(string id, string language) =>
        Published().FirstOrDefault(issue => issue.Id == id && issue.Language == language);

    private static List<WeeklyIssue> Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var issues = new List<WeeklyIssue>();
        foreach (var path in Directory.GetFiles(directory))
        {
            var name = FileName().Match(System.IO.Path.GetFileName(path));
            if (!name.Success)
            {
                continue;
            }

            var (yaml, body) = FrontMatterSplitter.Split(File.ReadAllText(path));
            FrontMatter? front;
            try
            {
                front = yaml is null ? null : Deserializer.Deserialize<FrontMatter?>(yaml);
            }
            catch (YamlDotNet.Core.YamlException)
            {
                continue; // CI's validate-weekly rejects such a file before it can reach main.
            }

            if (front?.Title is not { Length: > 0 } title
                || !DateOnly.TryParseExact(front.SendDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sendDate))
            {
                continue;
            }

            issues.Add(new WeeklyIssue(name.Groups["id"].Value, name.Groups["lang"].Value, title, sendDate, CuratorNoteRenderer.ToSanitizedHtml(body)));
        }

        return issues
            .OrderByDescending(issue => issue.Id, StringComparer.Ordinal)
            .ThenByDescending(issue => issue.Language, StringComparer.Ordinal)
            .ToList();
    }

    [GeneratedRegex(@"^(?<id>\d{4}-W\d{2})\.(?<lang>tr|en)\.md$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex FileName();

    private sealed class FrontMatter
    {
        public string? Title { get; set; }

        public string? SendDate { get; set; }
    }
}
