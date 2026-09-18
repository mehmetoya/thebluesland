using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheBluesland.Web.Cache;
using TheBluesland.Web.Content;

namespace TheBluesland.Web.Feed;

public sealed record PlaylistsFeed(string GeneratedAt, IReadOnlyList<PlaylistsFeedItem> Playlists);

/// <summary>
/// Property order is the contract's field order. Optional members are nullable and omitted from the
/// JSON when null (never emitted as <c>null</c> or an empty string).
/// </summary>
public sealed record PlaylistsFeedItem(
    string Slug,
    string Title,
    string Url,
    string? Description,
    int? TrackCount,
    string? Image,
    IReadOnlyList<string>? Collections,
    string AddedAt);

/// <summary>
/// Builds the <c>GET /playlists.json</c> body (docs/specs/playlists-json-feed.md). Callers pass the
/// result of <see cref="PlaylistContentRepository.FindAllPublishedAsync"/> - the same list the home
/// grid, every collection page and the sitemap read - so the feed's set of playlists and its
/// collection membership can never disagree with the site. Optional fields mirror what the playlist
/// cards render: cover and track count only when the cache snapshot is playable.
/// </summary>
public static class PlaylistsFeedBuilder
{
    public const int DescriptionMaxLength = 300;

    private const string DateFormat = "yyyy-MM-dd";
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    // Relaxed escaping keeps apostrophes, '&' and non-ASCII (Turkish) titles readable as written;
    // safe here because the response is application/json and is never embedded in HTML.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static PlaylistsFeed Build(
        IReadOnlyList<PlaylistContent> published,
        IReadOnlyDictionary<string, PlaylistCacheSnapshot> snapshots,
        Func<string, string> buildPlaylistUrl,
        DateTimeOffset generatedAt)
    {
        var collectionsBySlug = BuildCollectionMembership(published);

        // A published entry without publishedAt cannot pass content validation (CI-gated), so it
        // never reaches production; if one did, skipping it is the only honest option - the contract
        // forbids inventing a date.
        var items = published
            .Where(playlist => playlist.PublishedAt is not null)
            .OrderByDescending(playlist => playlist.PublishedAt)
            .ThenBy(playlist => playlist.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(playlist => playlist.Slug, StringComparer.Ordinal)
            .Select(playlist => ToItem(playlist, snapshots, collectionsBySlug, buildPlaylistUrl))
            .ToList();

        return new PlaylistsFeed(generatedAt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture), items);
    }

    public static string Serialize(PlaylistsFeed feed) => JsonSerializer.Serialize(feed, Options);

    private static PlaylistsFeedItem ToItem(
        PlaylistContent playlist,
        IReadOnlyDictionary<string, PlaylistCacheSnapshot> snapshots,
        IReadOnlyDictionary<string, List<string>> collectionsBySlug,
        Func<string, string> buildPlaylistUrl)
    {
        var snapshot = snapshots.GetValueOrDefault(playlist.SpotifyPlaylistId, PlaylistCacheSnapshot.Unavailable);

        return new PlaylistsFeedItem(
            playlist.Slug,
            playlist.Title,
            buildPlaylistUrl(playlist.Slug),
            ToDescription(playlist.Summary),
            snapshot is { IsPlayable: true, TrackCount: { } trackCount and >= 0 } ? trackCount : null,
            ToHttpsImage(snapshot),
            collectionsBySlug.TryGetValue(playlist.Slug, out var collections) ? collections : null,
            playlist.PublishedAt!.Value.ToString(DateFormat, CultureInfo.InvariantCulture));
    }

    private static Dictionary<string, List<string>> BuildCollectionMembership(IReadOnlyList<PlaylistContent> published)
    {
        var membership = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var collection in PlaylistCollections.All)
        {
            foreach (var member in PlaylistFilter.Apply(published, collection.Criteria))
            {
                if (!membership.TryGetValue(member.Slug, out var slugs))
                {
                    slugs = [];
                    membership[member.Slug] = slugs;
                }

                slugs.Add(collection.Slug);
            }
        }

        return membership;
    }

    private static string? ToDescription(string summary)
    {
        var trimmed = summary.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        return trimmed.Length <= DescriptionMaxLength
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, DescriptionMaxLength - 1).TrimEnd(), "…");
    }

    private static string? ToHttpsImage(PlaylistCacheSnapshot snapshot) =>
        snapshot is { IsPlayable: true, CoverImageUrl: { Length: > 0 } url }
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
            ? url
            : null;
}
