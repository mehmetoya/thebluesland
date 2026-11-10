using System.Globalization;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace TheBluesland.WeeklyNewsletter.Ingest;

/// <summary>
/// Fetches one RSS 2.0 or Atom 1.0 feed and turns it into <see cref="FeedItem"/>s (news-ingest spec,
/// Design section 4). Failure contract: non-success HTTP status → <see cref="HttpRequestException"/>;
/// slower than the per-feed timeout → <see cref="TimeoutException"/>; unparseable or oversized feed
/// → <see cref="XmlException"/>. Cancellation by the caller surfaces as a plain
/// <see cref="OperationCanceledException"/>. A single bad item (no title, no usable link) is
/// skipped; an unparseable date only nulls that item's <see cref="FeedItem.PublishedAt"/>.
/// Every stored string is cleaned so one hostile item cannot break the batch insert: no control
/// characters (Postgres jsonb rejects \u0000), no lone surrogates, bounded lengths.
/// </summary>
public sealed partial class RssFeedClient(HttpClient httpClient, TimeSpan? timeout = null)
{
    public const string UserAgent = "TheBlueslandWeekly/1.0 (+https://thebluesland.com)";
    public const int MaxSummaryLength = 2000;
    public const int MaxUrlLength = 2000;

    // The largest feed captured on 2026-11-10 was ~160 KB; anything past this is not a news feed.
    public const int MaxResponseBytes = 10 * 1024 * 1024;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    // Bounds regex work per field so a hostile feed cannot make cleaning quadratic.
    private const int MaxFieldInputLength = 20_000;
    private const int RegexTimeoutMilliseconds = 1000;
    private const string Ellipsis = "…";

    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    public async Task<IReadOnlyList<FeedItem>> FetchAsync(FeedConfig feed, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
            using var buffer = await ReadBoundedAsync(feed, stream, timeoutSource.Token);
            return Parse(feed.Id, buffer);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Feed '{feed.Id}' did not respond within {_timeout.TotalSeconds:0.#} s.");
        }
    }

    public static IReadOnlyList<FeedItem> Parse(string source, Stream xml)
    {
        var settings = new XmlReaderSettings
        {
            // Feeds never need a DTD; ignoring it means entities are never expanded or fetched.
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };

        using var reader = XmlReader.Create(xml, settings);
        var feed = LoadFeed(reader);

        var items = new List<FeedItem>();
        foreach (var entry in feed.Items)
        {
            var title = CleanText(entry.Title?.Text);
            var url = FindLink(entry);
            if (title is null || url is null)
            {
                continue;
            }

            var summary = CleanSummary(entry.Summary?.Text ?? (entry.Content as TextSyndicationContent)?.Text, title);
            items.Add(new FeedItem(source, title, url, PublishedAt(entry), summary, Categories(entry)));
        }

        return items;
    }

    private static async Task<MemoryStream> ReadBoundedAsync(FeedConfig feed, Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                await buffer.DisposeAsync();
                throw new XmlException($"Feed '{feed.Id}' is larger than {MaxResponseBytes / 1024 / 1024} MB; not a news feed.");
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        return buffer;
    }

    private static SyndicationFeed LoadFeed(XmlReader reader)
    {
        reader.MoveToContent();
        SyndicationFeedFormatter formatter = new Atom10FeedFormatter();
        if (!formatter.CanRead(reader))
        {
            formatter = new Rss20FeedFormatter();
        }

        if (!formatter.CanRead(reader))
        {
            throw new XmlException($"Unsupported feed root element <{reader.LocalName}>; expected RSS 2.0 or Atom 1.0.");
        }

        // Without this, one item with a non-RFC-822 date makes the whole feed unreadable.
        formatter.DateTimeParser = TryParseFeedDate;
        try
        {
            formatter.ReadFrom(reader);
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or ArgumentException or InvalidOperationException)
        {
            // Syndication reports some malformed feeds (e.g. <rss> without version="2.0") with
            // non-XML exception types; normalize so callers have one "unreadable feed" type.
            throw new XmlException($"Feed is not readable RSS 2.0 / Atom 1.0: {ex.Message}", ex);
        }

        return formatter.Feed;
    }

    /// <summary>
    /// Always succeeds: an unparseable date becomes <see cref="DateTimeOffset.MinValue"/>, which
    /// <see cref="PublishedAt"/> maps to null, so a bad date never costs us the article.
    /// </summary>
    private static bool TryParseFeedDate(XmlDateTimeData data, out DateTimeOffset value)
    {
        var text = data.DateTimeString?.Trim() ?? string.Empty;

        // RFC 822: drop the optional day name (feeds get it wrong, and .NET rejects a mismatch),
        // and turn "+0000" into "+00:00", which DateTimeOffset.TryParse understands.
        text = LeadingDayName().Replace(text, string.Empty);
        text = NumericOffset().Replace(text, "$1$2:$3");
        text = text.Replace(" GMT", " +00:00", StringComparison.OrdinalIgnoreCase)
            .Replace(" UTC", " +00:00", StringComparison.OrdinalIgnoreCase);

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            value = parsed.ToUniversalTime();
            return true;
        }

        value = DateTimeOffset.MinValue;
        return true;
    }

    private static DateTimeOffset? PublishedAt(SyndicationItem entry)
    {
        var date = entry.PublishDate != DateTimeOffset.MinValue ? entry.PublishDate : entry.LastUpdatedTime;
        return date == DateTimeOffset.MinValue ? null : date.ToUniversalTime();
    }

    /// <summary>
    /// Only the article link itself (RSS &lt;link&gt; / Atom rel="alternate") - never an enclosure,
    /// image or "self" link standing in for it.
    /// </summary>
    private static Uri? FindLink(SyndicationItem entry)
    {
        var uri = entry.Links.FirstOrDefault(l => l.RelationshipType is null or "alternate")?.Uri;
        return uri is { IsAbsoluteUri: true }
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.AbsoluteUri.Length <= MaxUrlLength
            ? uri
            : null;
    }

    private static List<string> Categories(SyndicationItem entry) =>
        entry.Categories
            .Select(category => CleanText(category.Name ?? category.Label))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Some feeds start the summary with the headline or end it with the headline and the author's name
    /// ("... ceremony. Oasis to Skip Induction Paolo Ragusa"). Both are noise to the drafting prompt: the
    /// headline is already shown, and a byline reads as a name that belongs to the story.
    /// </summary>
    private static string WithoutHeadlineRepeat(string text, string title)
    {
        if (title.Length < 8)
        {
            return text;
        }

        if (text.StartsWith(title, StringComparison.OrdinalIgnoreCase))
        {
            text = text[title.Length..].TrimStart(' ', ':', '-', '–', '—', '.', '|');
        }

        var index = text.LastIndexOf(title, StringComparison.OrdinalIgnoreCase);
        if (index > 0)
        {
            var words = text[(index + title.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 4 && words.All(word => char.IsUpper(word[0])))
            {
                text = text[..index].TrimEnd();
            }
        }

        return text;
    }

    private static string? CleanSummary(string? html, string title)
    {
        var text = CleanText(html);
        if (text is null)
        {
            return null;
        }

        text = WordPressSuffix().Replace(text, string.Empty);
        text = TrailingSourceLink().Replace(text, string.Empty).Trim();
        text = WithoutHeadlineRepeat(text, title);
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length <= MaxSummaryLength)
        {
            return text;
        }

        var cut = MaxSummaryLength - Ellipsis.Length;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut].TrimEnd() + Ellipsis;
    }

    /// <summary>
    /// Strips tags, decodes entities, removes control characters and lone surrogates, collapses
    /// whitespace; empty → null.
    /// </summary>
    private static string? CleanText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = html.Length > MaxFieldInputLength ? html[..MaxFieldInputLength] : html;
        text = HtmlTag().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = ControlCharacter().Replace(text, " ");
        text = RemoveLoneSurrogates(text);
        text = Whitespace().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static string RemoveLoneSurrogates(string text)
    {
        if (!text.Any(char.IsSurrogate))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(text[i]).Append(text[i + 1]);
                i++;
            }
            else if (!char.IsSurrogate(text[i]))
            {
                builder.Append(text[i]);
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^[A-Za-z]{3,9},\s*", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex LeadingDayName();

    [GeneratedRegex(@"([+-])(\d{2})(\d{2})$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex NumericOffset();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\p{Cc}", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex ControlCharacter();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex Whitespace();

    // WordPress appends "The post <title> appeared first on <site>." to every excerpt (NME,
    // Stereogum). Bounded so it cannot swallow real text or backtrack over a whole document.
    [GeneratedRegex(@"\s*The post .{1,500}? appeared first on [^.]{1,100}\.?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeoutMilliseconds)]
    private static partial Regex WordPressSuffix();

    // Music Business Worldwide ends each excerpt with a bare "Source" link.
    [GeneratedRegex(@"\s+Source$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex TrailingSourceLink();
}
