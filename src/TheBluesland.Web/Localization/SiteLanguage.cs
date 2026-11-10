using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;

namespace TheBluesland.Web.Localization;

/// <summary>
/// The language of the page being rendered (docs/specs/site-i18n.md). It comes from the URL: every
/// page lives under <c>/tr/...</c> or <c>/en/...</c>. The few requests that carry no prefix - the
/// <c>/not-found</c> page a 404 is re-executed to, mostly - fall back to the visitor's saved
/// <c>lang</c> cookie, then to Accept-Language, then to English.
///
/// There is deliberately no <see cref="CultureInfo"/> here. Dates and numbers are formatted by the
/// built-in tables below, so a container without ICU data (or with invariant globalization) renders
/// "16 Kasım 2026" exactly like a developer laptop does - the same reasoning
/// <see cref="Content.WeeklyIssue.FormatDate"/> already followed.
/// </summary>
public sealed class SiteLanguage
{
    public const string Turkish = "tr";
    public const string English = "en";
    public const string CookieName = "lang";

    /// <summary>The hreflang="x-default" target language: English (see docs/specs/site-i18n.md).</summary>
    public const string DefaultCode = English;

    /// <summary>Both site languages, Turkish first (the order of the switcher and of hreflang links).</summary>
    public static readonly IReadOnlyList<string> Supported = [Turkish, English];

    private static readonly string[] TurkishMonths =
        ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];

    private static readonly string[] EnglishMonths =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    private static readonly NumberFormatInfo TurkishNumbers = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };
    private static readonly NumberFormatInfo EnglishNumbers = new() { NumberGroupSeparator = ",", NumberDecimalSeparator = "." };

    public SiteLanguage(string code, string currentPathAndQuery)
    {
        Code = Normalize(code) ?? DefaultCode;
        CurrentPathAndQuery = currentPathAndQuery;
    }

    /// <summary>"tr" or "en".</summary>
    public string Code { get; }

    public bool IsTurkish => Code == Turkish;

    public string OtherCode => IsTurkish ? English : Turkish;

    /// <summary>"tr_TR" / "en_US" for og:locale.</summary>
    public string OpenGraphLocale => OpenGraphLocaleFor(Code);

    public SiteText T => SiteText.For(Code);

    /// <summary>
    /// The <c>lang</c> attribute for text that is English-only today (collection titles and intros):
    /// "en" on a Turkish page so screen readers and translators know, nothing on an English page.
    /// </summary>
    public string? EnglishContentLang => IsTurkish ? English : null;

    /// <summary>The request the visitor actually made (the original one while a 404 page is re-executed).</summary>
    public string CurrentPathAndQuery { get; }

    /// <summary>"/about" becomes "/tr/about", "/" becomes "/tr"; query strings and fragments are kept.</summary>
    public string Url(string path) => UrlFor(Code, path);

    /// <summary>The page the visitor is on, in <paramref name="code"/>; a page without a prefix links to that language's home.</summary>
    public string SwitchUrl(string code)
    {
        var target = Normalize(code) ?? DefaultCode;
        return TrySplit(CurrentPathAndQuery, out _, out var rest) ? UrlFor(target, rest) : UrlFor(target, "/");
    }

    public string FormatDate(DateOnly date) => FormatDate(date, Code);

    public static string FormatDate(DateOnly date, string code) =>
        $"{date.Day} {(code == Turkish ? TurkishMonths : EnglishMonths)[date.Month - 1]} {date.Year}";

    public string FormatNumber(int value) => value.ToString("N0", Code == Turkish ? TurkishNumbers : EnglishNumbers);

    public static string UrlFor(string code, string path)
    {
        var tail = path == "/" || path.StartsWith("/?", StringComparison.Ordinal) || path.StartsWith("/#", StringComparison.Ordinal)
            ? path[1..]
            : path;
        return $"/{code}{tail}";
    }

    public static string OpenGraphLocaleFor(string code) => code == Turkish ? "tr_TR" : "en_US";

    /// <summary>"tr"/"en" (any casing) or null for anything else.</summary>
    public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Turkish => Turkish,
        English => English,
        _ => null,
    };

    /// <summary>
    /// Splits "/tr/about?x=1" into ("tr", "/about?x=1") and "/en" into ("en", "/"). False when the first
    /// segment is not a site language.
    /// </summary>
    public static bool TrySplit(string pathAndQuery, out string code, out string rest)
    {
        code = string.Empty;
        rest = "/";
        if (pathAndQuery.Length < 3 || pathAndQuery[0] != '/')
        {
            return false;
        }

        var end = pathAndQuery.AsSpan(1).IndexOfAny('/', '?', '#');
        var segmentLength = end < 0 ? pathAndQuery.Length - 1 : end;
        if (segmentLength != 2 || Normalize(pathAndQuery.Substring(1, 2)) is not { } language)
        {
            return false;
        }

        code = language;
        var tail = pathAndQuery[3..];
        rest = tail.Length == 0 ? "/" : tail[0] is '?' or '#' ? "/" + tail : tail;
        return true;
    }

    /// <summary>The language a visitor with no prefixed URL should get: saved cookie, then Accept-Language, then English.</summary>
    public static string Preferred(HttpRequest request)
    {
        if (Normalize(request.Cookies[CookieName]) is { } saved)
        {
            return saved;
        }

        // Highest quality first; the first tr/en entry decides, other languages are skipped
        // ("de, tr;q=0.5" is Turkish; "en, tr;q=0.5" is English). Nothing recognisable means English.
        var languages = request.GetTypedHeaders().AcceptLanguage
            .Where(entry => entry.Quality is null or > 0)
            .OrderByDescending(entry => entry.Quality ?? 1.0);
        foreach (var entry in languages)
        {
            var primary = entry.Value.Value?.Split('-')[0];
            if (Normalize(primary) is { } match)
            {
                return match;
            }
        }

        return DefaultCode;
    }

    public static SiteLanguage FromHttpContext(HttpContext? context)
    {
        if (context is null)
        {
            return new SiteLanguage(DefaultCode, "/");
        }

        var original = context.Features.Get<IStatusCodeReExecuteFeature>();
        var pathAndQuery = original is null
            ? context.Request.Path.Value + context.Request.QueryString.Value
            : original.OriginalPath + original.OriginalQueryString;
        return TrySplit(pathAndQuery, out var code, out _)
            ? new SiteLanguage(code, pathAndQuery)
            : new SiteLanguage(Preferred(context.Request), pathAndQuery);
    }
}
