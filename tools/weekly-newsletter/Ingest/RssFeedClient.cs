using System.Globalization;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;

namespace TheBluesland.WeeklyNewsletter.Ingest;

/// <summary>
/// Fetches one RSS 2.0 or Atom 1.0 feed and turns it into <see cref="FeedItem"/>s (news-ingest spec,
/// Design section 4). Failure contract, relied on by the ingest service to fail only this feed:
/// non-success HTTP status → <see cref="HttpRequestException"/>; slower than the per-feed timeout →
/// <see cref="TimeoutException"/>; unparseable XML → <see cref="XmlException"/>. Cancellation by the
/// caller surfaces as a plain <see cref="OperationCanceledException"/>.
/// A single bad item (no title, no absolute link) is skipped; an unparseable date only nulls that
/// item's <see cref="FeedItem.PublishedAt"/>.
/// </summary>
public sealed partial class RssFeedClient(HttpClient httpClient, TimeSpan? timeout = null)
{
    public const string UserAgent = "TheBlueslandWeekly/1.0 (+https://thebluesland.com)";
    public const int MaxSummaryLength = 2000;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    // Feeds are small (the largest captured on 2026-11-10 was ~160 KB); this only stops a
    // misbehaving server from streaming an unbounded document into memory.
    private const long MaxDocumentCharacters = 10_000_000;

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
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, timeoutSource.Token);
            buffer.Position = 0;

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
            MaxCharactersInDocument = MaxDocumentCharacters,
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

            var summary = CleanSummary(entry.Summary?.Text ?? (entry.Content as TextSyndicationContent)?.Text);
            items.Add(new FeedItem(source, title, url, PublishedAt(entry), summary, Categories(entry)));
        }

        return items;
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

        // RFC 822 numeric offsets ("+0000") are not understood by DateTimeOffset.TryParse; "+00:00" is.
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
        var date = entry.PublishDate != default && entry.PublishDate != DateTimeOffset.MinValue
            ? entry.PublishDate
            : entry.LastUpdatedTime;

        return date == default || date == DateTimeOffset.MinValue ? null : date.ToUniversalTime();
    }

    private static Uri? FindLink(SyndicationItem entry)
    {
        var link = entry.Links.FirstOrDefault(l => l.RelationshipType is null or "alternate") ?? entry.Links.FirstOrDefault();
        var uri = link?.Uri;
        return uri is { IsAbsoluteUri: true } && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;
    }

    private static List<string> Categories(SyndicationItem entry) =>
        entry.Categories
            .Select(category => CleanText(category.Name ?? category.Label))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string? CleanSummary(string? html)
    {
        var text = CleanText(html);
        if (text is null)
        {
            return null;
        }

        text = WordPressSuffix().Replace(text, string.Empty);
        text = TrailingSourceLink().Replace(text, string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length <= MaxSummaryLength
            ? text
            : text[..(MaxSummaryLength - Ellipsis.Length)].TrimEnd() + Ellipsis;
    }

    /// <summary>Strips tags, decodes entities, collapses whitespace; empty → null.</summary>
    private static string? CleanText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = HtmlTag().Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        text = Whitespace().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex(@"([+-])(\d{2})(\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericOffset();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    // WordPress appends "The post <title> appeared first on <site>." to every excerpt (NME, Stereogum).
    [GeneratedRegex(@"\s*The post .*? appeared first on .*?\.?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WordPressSuffix();

    // Music Business Worldwide ends each excerpt with a bare "Source" link.
    [GeneratedRegex(@"\s+Source$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingSourceLink();
}
