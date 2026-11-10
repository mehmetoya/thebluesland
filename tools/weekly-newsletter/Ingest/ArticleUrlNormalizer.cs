namespace TheBluesland.WeeklyNewsletter.Ingest;

/// <summary>
/// Produces <c>news_article.normalized_url</c>, the de-duplication key (news-ingest spec, Design
/// section 2): https scheme, lowercase host without a leading <c>www.</c>, no fragment, no trailing
/// slash (except the root), tracking parameters removed and remaining parameters sorted by name.
/// The path keeps its case. Recognizing that two different outlets cover the same story is not
/// this class's job - that is story-clustering.
/// </summary>
public static class ArticleUrlNormalizer
{
    private const string WwwPrefix = "www.";

    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "mc_cid", "mc_eid", "ref", "cmpid",
    };

    public static string Normalize(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri)
        {
            throw new ArgumentException("Only absolute URLs can be normalized.", nameof(url));
        }

        var host = url.IdnHost.ToLowerInvariant();
        if (host.StartsWith(WwwPrefix, StringComparison.Ordinal))
        {
            host = host[WwwPrefix.Length..];
        }

        // An explicit port that is the default for the original scheme is dropped; any other port is kept.
        var port = url.IsDefaultPort ? string.Empty : $":{url.Port}";

        var path = url.AbsolutePath;
        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        var query = NormalizeQuery(url.Query);
        return $"https://{host}{port}{path}{query}";
    }

    private static string NormalizeQuery(string query)
    {
        if (query.Length <= 1)
        {
            return string.Empty;
        }

        var kept = query[1..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => (Pair: pair, Name: pair.Split('=', 2)[0]))
            .Where(parameter => !IsTracking(parameter.Name))
            .OrderBy(parameter => parameter.Name, StringComparer.Ordinal)
            .Select(parameter => parameter.Pair)
            .ToList();

        return kept.Count == 0 ? string.Empty : "?" + string.Join('&', kept);
    }

    private static bool IsTracking(string name) =>
        name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || TrackingParameters.Contains(name);
}
