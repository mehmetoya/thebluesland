using System.Text.Json;
using System.Text.Json.Serialization;
using TheBluesland.Web.Cache;
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

    // inLanguage (BCP 47, "tr"/"en") is optional everywhere: null omits the property, which is what
    // the pre-localization callers and tests get. Pages pass their own language for site chrome
    // (WebSite/AboutPage/FAQPage) and the content language ("en" until a Turkish field exists, see
    // docs/specs/site-i18n.md) for playlist and collection pages.
    public static string BuildWebSite(string siteUrl, string? inLanguage = null) =>
        JsonSerializer.Serialize(
            new WebSiteSchema("https://schema.org", "WebSite", "TheBluesland", siteUrl)
            {
                Id = siteUrl + "#website",
                InLanguage = inLanguage,
                Description = "Mehmet's personal collection of Spotify playlists, rooted in blues and spanning rock, folk, jazz, soul and electronic music. Each playlist has a short listening note.",
            },
            Options);

    /// <param name="cacheSnapshot">
    /// Supplies <c>numTracks</c> and the playlist's own cover image - both already rendered
    /// visibly on the same page (the "N tracks" line and the cover <c>&lt;img&gt;</c>), so
    /// reflecting them here is not new disclosure, just structured restatement. Neither is a
    /// track-level field (spec 9.4/11.2): a count and a playlist-level image, the same class of
    /// aggregate as <see cref="PlaylistCacheSnapshot.TrackCount"/> itself. Omitted entirely
    /// (<see cref="JsonIgnoreCondition.WhenWritingNull"/>) when the cache is unavailable, matching
    /// this page's own graceful-degradation rule (spec 16.1) - never a stale or zero placeholder.
    /// </param>
    /// <param name="imageUrl">
    /// The social card URL. Defaults to <c>canonicalUrl + "/og-image.png"</c> (the unprefixed
    /// pre-localization shape); localized pages must pass the language-neutral card URL because
    /// <c>/tr/playlists/{slug}/og-image.png</c> does not exist.
    /// </param>
    public static string BuildCollectionPage(
        PlaylistContent content,
        PlaylistCacheSnapshot cacheSnapshot,
        string canonicalUrl,
        string? inLanguage = null,
        string? imageUrl = null) =>
        JsonSerializer.Serialize(
            new CollectionPageSchema("https://schema.org", "CollectionPage", content.Title, content.Summary, canonicalUrl)
            {
                Id = canonicalUrl + "#webpage",
                InLanguage = inLanguage,
                DatePublished = content.PublishedAt,
                Keywords = PlaylistTags.All(content),
                Image = imageUrl ?? canonicalUrl + "/og-image.png",
                MainEntity = new MusicPlaylistSchema("MusicPlaylist", content.Title, content.Summary, canonicalUrl + "#playlist")
                {
                    Genre = content.Genres,
                    Url = GetSpotifyUrl(content),
                    NumTracks = cacheSnapshot.IsPlayable ? cacheSnapshot.TrackCount : null,
                    Image = cacheSnapshot.IsPlayable ? cacheSnapshot.CoverImageUrl : null,
                },
            },
            Options);

    /// <summary>
    /// Distinct from <see cref="BuildCollectionPage(PlaylistContent, PlaylistCacheSnapshot, string)"/>:
    /// that method describes a single playlist detail page; this one describes a
    /// <c>/collections/{slug}</c> page listing several playlists, as a <c>CollectionPage</c> +
    /// nested <c>ItemList</c> whose entries point at each playlist's own canonical URL - no
    /// track-level data here either (spec 9.4/11.2), just titles and URLs already visible on the page.
    /// </summary>
    public static string BuildCollectionPageForPlaylists(
        PlaylistCollection collection,
        IReadOnlyList<PlaylistContent> playlists,
        string canonicalUrl,
        string siteUrl,
        string? inLanguage = null) =>
        JsonSerializer.Serialize(
            new CollectionListPageSchema("https://schema.org", "CollectionPage", collection.Title, collection.Description, canonicalUrl)
            {
                Id = canonicalUrl + "#webpage",
                InLanguage = inLanguage,
                MainEntity = new ItemListSchema(
                    "ItemList",
                    [.. playlists.Select((playlist, index) => new ListItemSchema(
                        "ListItem",
                        index + 1,
                        new PlaylistListItemSchema("MusicPlaylist", playlist.Title, siteUrl + $"/playlists/{playlist.Slug}")))]),
            },
            Options);

    private static string? GetSpotifyUrl(PlaylistContent content) =>
        SpotifyPlaylistIdFormat.IsValid(content.SpotifyPlaylistId)
            ? $"https://open.spotify.com/playlist/{content.SpotifyPlaylistId}"
            : null;

    /// <summary>
    /// AEO/GEO: a Markdown-question -&gt; short-answer FAQ, marked up so answer engines (ChatGPT,
    /// Gemini, Perplexity) and Google's own rich results can lift a whole answer verbatim rather
    /// than guessing at one from prose. Every question/answer pair must match the page's own
    /// visible text exactly (Google's FAQPage guidance) - <see cref="AboutPage"/>'s markup and this
    /// method's caller share one array literal for that reason, so the two cannot drift apart.
    /// </summary>
    public static string BuildFaqPage(IReadOnlyList<(string Question, string Answer)> items, string? inLanguage = null) =>
        JsonSerializer.Serialize(
            new FaqPageSchema("https://schema.org", "FAQPage",
                [.. items.Select(item => new QuestionSchema(
                    "Question",
                    item.Question,
                    new AnswerSchema("Answer", item.Answer)))])
            {
                InLanguage = inLanguage,
            },
            Options);

    /// <summary>
    /// GEO's "who is the source" signal (E-E-A-T by another name): names the collection's curator
    /// as a distinct entity rather than leaving the site anonymous. <paramref name="personName"/>/
    /// <paramref name="personDescription"/> restate exactly what <see cref="AboutPage"/> already
    /// says in visible prose - no new personal disclosure, no <c>sameAs</c> profile links (that is
    /// a separate, deliberate choice left to Mehmet).
    /// </summary>
    public static string BuildAboutPage(string personName, string personDescription, string canonicalUrl, string? inLanguage = null) =>
        JsonSerializer.Serialize(
            new AboutPageSchema("https://schema.org", "AboutPage", canonicalUrl)
            {
                Id = canonicalUrl + "#webpage",
                InLanguage = inLanguage,
                MainEntity = new PersonSchema("Person", personName, personDescription),
            },
            Options);

    public static string BuildBreadcrumbList(string homeUrl, string playlistTitle, string canonicalUrl, string homeName = "Home") =>
        JsonSerializer.Serialize(
            new BreadcrumbListSchema("https://schema.org", "BreadcrumbList",
            [
                new BreadcrumbItem("ListItem", 1, homeName, homeUrl),
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
        public string? InLanguage { get; init; }
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
        public string? InLanguage { get; init; }
        public DateOnly? DatePublished { get; init; }
        public IReadOnlyList<string>? Keywords { get; init; }
        public string? Image { get; init; }
        public MusicPlaylistSchema? MainEntity { get; init; }
    }

    private sealed record CollectionListPageSchema(
        [property: JsonPropertyName("@context")] string Context,
        [property: JsonPropertyName("@type")] string Type,
        string Name,
        string Description,
        string Url)
    {
        [JsonPropertyName("@id")]
        public string? Id { get; init; }
        public string? InLanguage { get; init; }
        public ItemListSchema? MainEntity { get; init; }
    }

    private sealed record ItemListSchema(
        [property: JsonPropertyName("@type")] string Type,
        IReadOnlyList<ListItemSchema> ItemListElement);

    private sealed record ListItemSchema(
        [property: JsonPropertyName("@type")] string Type,
        int Position,
        PlaylistListItemSchema Item);

    private sealed record PlaylistListItemSchema(
        [property: JsonPropertyName("@type")] string Type,
        string Name,
        string Url);

    private sealed record MusicPlaylistSchema(
        [property: JsonPropertyName("@type")] string Type,
        string Name,
        string Description,
        [property: JsonPropertyName("@id")] string Id)
    {
        public IReadOnlyList<string>? Genre { get; init; }
        public string? Url { get; init; }
        public int? NumTracks { get; init; }
        public string? Image { get; init; }
    }

    private sealed record FaqPageSchema(
        [property: JsonPropertyName("@context")] string Context,
        [property: JsonPropertyName("@type")] string Type,
        IReadOnlyList<QuestionSchema> MainEntity)
    {
        public string? InLanguage { get; init; }
    }

    private sealed record QuestionSchema(
        [property: JsonPropertyName("@type")] string Type,
        string Name,
        AnswerSchema AcceptedAnswer);

    private sealed record AnswerSchema(
        [property: JsonPropertyName("@type")] string Type,
        string Text);

    private sealed record AboutPageSchema(
        [property: JsonPropertyName("@context")] string Context,
        [property: JsonPropertyName("@type")] string Type,
        string Url)
    {
        [JsonPropertyName("@id")]
        public string? Id { get; init; }
        public string? InLanguage { get; init; }
        public PersonSchema? MainEntity { get; init; }
    }

    private sealed record PersonSchema(
        [property: JsonPropertyName("@type")] string Type,
        string Name,
        string Description);

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
