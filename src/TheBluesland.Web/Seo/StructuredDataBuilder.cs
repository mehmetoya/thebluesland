using System.Text.Json;
using System.Text.Json.Serialization;
using TheBluesland.Web.Content;

namespace TheBluesland.Web.Seo;

/// <summary>
/// Builds JSON-LD structured data (US-011 AC4, FR-032): <c>WebSite</c> for the home page,
/// <c>CollectionPage</c> + <c>BreadcrumbList</c> for a playlist detail page. Built from typed
/// records and <see cref="JsonSerializer"/> - never string concatenation - so title/summary text is
/// always correctly JSON-escaped. There is deliberately no per-track field anywhere here:
/// <see cref="PlaylistContent"/> itself carries no track-level data to copy from (spec 9.4/11.2), so
/// there is nothing to accidentally serialize; <c>StructuredDataBuilderTests</c> pins this down with
/// a regression test so a future field addition to <see cref="PlaylistContent"/> cannot silently
/// leak a track-shaped value into this output.
/// </summary>
public static class StructuredDataBuilder
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string BuildWebSite(string siteUrl) =>
        JsonSerializer.Serialize(
            new WebSiteSchema("https://schema.org", "WebSite", "TheBluesland", siteUrl)
            {
                Id = siteUrl + "#website",
                Description = "Mehmet's personal collection of Spotify playlists, rooted in blues and spanning rock, folk, jazz, soul and electronic music. Each playlist has a short listening note.",
            },
            Options);

    public static string BuildCollectionPage(PlaylistContent content, string canonicalUrl) =>
        JsonSerializer.Serialize(
            new CollectionPageSchema("https://schema.org", "CollectionPage", content.Title, content.Summary, canonicalUrl)
            {
                Id = canonicalUrl + "#webpage",
                DatePublished = content.PublishedAt,
                Keywords = PlaylistTags.All(content),
                Image = canonicalUrl + "/og-image.png",
                MainEntity = new MusicPlaylistSchema("MusicPlaylist", content.Title, content.Summary, canonicalUrl + "#playlist")
                {
                    Genre = content.Genres,
                    Url = GetSpotifyUrl(content),
                },
            },
            Options);

    private static string? GetSpotifyUrl(PlaylistContent content) =>
        SpotifyPlaylistIdFormat.IsValid(content.SpotifyPlaylistId)
            ? $"https://open.spotify.com/playlist/{content.SpotifyPlaylistId}"
            : null;

    public static string BuildBreadcrumbList(string homeUrl, string playlistTitle, string canonicalUrl) =>
        JsonSerializer.Serialize(
            new BreadcrumbListSchema("https://schema.org", "BreadcrumbList",
            [
                new BreadcrumbItem("ListItem", 1, "Home", homeUrl),
                new BreadcrumbItem("ListItem", 2, playlistTitle, canonicalUrl),
            ]),
            Options);

    private sealed record WebSiteSchema(
        [property: JsonPropertyName("@context")] string Context,
        [property: JsonPropertyName("@type")] string Type,
        string Name,
        string Url)
    {
        [JsonPropertyName("@id")]
        public string? Id { get; init; }
        public string? Description { get; init; }
    }

    private sealed record CollectionPageSchema(
        [property: JsonPropertyName("@context")] string Context,
        [property: JsonPropertyName("@type")] string Type,
        string Name,
        string Description,
        string Url)
    {
        [JsonPropertyName("@id")]
        public string? Id { get; init; }
        public DateOnly? DatePublished { get; init; }
        public IReadOnlyList<string>? Keywords { get; init; }
        public string? Image { get; init; }
        public MusicPlaylistSchema? MainEntity { get; init; }
    }

    private sealed record MusicPlaylistSchema(
        [property: JsonPropertyName("@type")] string Type,
        string Name,
        string Description,
        [property: JsonPropertyName("@id")] string Id)
    {
        public IReadOnlyList<string>? Genre { get; init; }
        public string? Url { get; init; }
    }

    private sealed record BreadcrumbListSchema(
        [property: JsonPropertyName("@context")] string Context,
        [property: JsonPropertyName("@type")] string Type,
        IReadOnlyList<BreadcrumbItem> ItemListElement);

    private sealed record BreadcrumbItem(
        [property: JsonPropertyName("@type")] string Type,
        int Position,
        string Name,
        string Item);
}
