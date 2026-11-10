using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TheBluesland.Web.Content;

/// <summary>One TheBluesland Weekly issue in one language, as /weekly shows it.</summary>
public sealed record WeeklyIssue(
    string Id,
    string Language,
    string Title,
    DateOnly SendDate,
    string Html,
    string Lede,
    IReadOnlyList<string> Headlines,
    int ReadMinutes)
{
    private const string TitlePrefix = "TheBluesland Weekly · ";

    private static readonly string[] TurkishMonths =
        ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];

    private static readonly string[] EnglishMonths =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    /// <summary>"/{lang}/weekly/{id}" - the language lives in the path prefix (docs/specs/site-i18n.md).</summary>
    public string Path => $"/{Language}/weekly/{Id}";

    /// <summary>The language-neutral part of <see cref="Path"/> ("/weekly/{id}"), for PageMetadata.</summary>
    public string NeutralPath => $"/weekly/{Id}";

    public string LanguageName => Language == "tr" ? "Türkçe" : "English";

    public int StoryCount => Headlines.Count;

    /// <summary>The ISO week number from the id ("2026-W47" is 47); 0 if the id is not in that shape.</summary>
    public int Number => Id.Length >= 7 && int.TryParse(Id.AsSpan(6), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var week) ? week : 0;

    /// <summary>The front-matter title without the repeated "TheBluesland Weekly · " prefix.</summary>
    public string DisplayTitle => Title.StartsWith(TitlePrefix, StringComparison.Ordinal) && Title.Length > TitlePrefix.Length
        ? Title[TitlePrefix.Length..]
        : Title;

    /// <summary>"16 Kasım 2026" / "16 November 2026"; month names are built in, so no ICU data is needed.</summary>
    public string FormattedDate => FormatDate(SendDate, Language);

    public static string FormatDate(DateOnly date, string language) =>
        $"{date.Day} {(language == "tr" ? TurkishMonths : EnglishMonths)[date.Month - 1]} {date.Year}";
}

/// <summary>
/// The weekly archive (docs/specs/weekly-newsletter.md, module weekly-archive): merged issue files in
/// <c>content/weekly/</c>, read once per deployment like the playlist catalogue. An issue appears
/// only from its send time in its language (Weekly:SendTimeTr / Weekly:SendTimeEn on the send date,
/// Europe/Istanbul; defaults 08:00 and 15:00 as in delivery.yml), so it is never on the web before
/// its e-mail. The same rule feeds the hub, the issue pages and previous/next.
/// The body is rendered by WeeklyIssueLayout with the curator-note sanitizer's guarantees (raw HTML
/// off, executable URL schemes neutralised). The web app never talks to the e-mail provider.
/// </summary>
public sealed partial class WeeklyIssueRepository
{
    public const string ContentDirectoryConfigKey = "WeeklyContent:Directory";
    public const string SendTimeTrConfigKey = "Weekly:SendTimeTr";
    public const string SendTimeEnConfigKey = "Weekly:SendTimeEn";

    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly Lazy<IReadOnlyList<WeeklyIssue>> _all;
    private readonly TimeProvider _clock;
    private readonly TimeOnly _sendTimeTr;
    private readonly TimeOnly _sendTimeEn;

    public WeeklyIssueRepository(IConfiguration configuration, TimeProvider? clock = null)
    {
        var directory = configuration[ContentDirectoryConfigKey]
            ?? System.IO.Path.Combine(Directory.GetCurrentDirectory(), "content", "weekly");
        _clock = clock ?? TimeProvider.System;
        _sendTimeTr = SendTime(configuration[SendTimeTrConfigKey], new TimeOnly(8, 0));
        _sendTimeEn = SendTime(configuration[SendTimeEnConfigKey], new TimeOnly(15, 0));
        _all = new Lazy<IReadOnlyList<WeeklyIssue>>(() => Load(directory));
    }

    /// <summary>Published issues, newest first; Turkish before English within an issue. Each language appears at its own send time.</summary>
    public IReadOnlyList<WeeklyIssue> Published()
    {
        var now = _clock.GetUtcNow();
        return _all.Value.Where(issue => PublishAt(issue) <= now).ToList();
    }

    /// <summary>The instant the issue goes out by e-mail, and so appears on the web.</summary>
    private DateTimeOffset PublishAt(WeeklyIssue issue)
    {
        var local = issue.SendDate.ToDateTime(issue.Language == "tr" ? _sendTimeTr : _sendTimeEn);
        return new DateTimeOffset(local, Istanbul.GetUtcOffset(local));
    }

    private static TimeOnly SendTime(string? configured, TimeOnly fallback) =>
        TimeOnly.TryParseExact(configured, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : fallback;

    public WeeklyIssue? Find(string id, string language) =>
        Published().FirstOrDefault(issue => issue.Id == id && issue.Language == language);

    /// <summary>The next-older and next-newer published issue in the same language (either may be null).</summary>
    public (WeeklyIssue? Previous, WeeklyIssue? Next) Neighbours(WeeklyIssue issue)
    {
        var sameLanguage = Published().Where(candidate => candidate.Language == issue.Language).ToList();
        var index = sameLanguage.FindIndex(candidate => candidate.Id == issue.Id);
        if (index < 0)
        {
            return (null, null);
        }

        return (index + 1 < sameLanguage.Count ? sameLanguage[index + 1] : null, index > 0 ? sameLanguage[index - 1] : null);
    }

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

            var layout = WeeklyIssueLayout.Build(body);
            issues.Add(new WeeklyIssue(
                name.Groups["id"].Value, name.Groups["lang"].Value, title, sendDate,
                layout.Html, layout.Lede, layout.Headlines, layout.ReadMinutes));
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
