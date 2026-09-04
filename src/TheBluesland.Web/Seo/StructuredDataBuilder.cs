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
    };

    public static string BuildWebSite(string siteUrl) =>
        JsonSerializer.Serialize(
            new WebSiteSchema("https://schema.org", "WebSite", "TheBluesland", siteUrl),
            Options);

    public static string BuildCollectionPage(PlaylistContent content, string canonicalUrl) =>
        JsonSerializer.Serialize(
            new CollectionPageSchema("https://schema.org", "CollectionPage", content.Title, content.Summary, canonicalUrl),
            Options);

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
        string Url);

    private sealed record CollectionPageSchema(
        [property: JsonPropertyName("@context")] string Context,
        [property: JsonPropertyName("@type")] string Type,
        string Name,
        string Description,
        string Url);

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
