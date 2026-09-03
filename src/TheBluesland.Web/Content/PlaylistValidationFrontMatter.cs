namespace TheBluesland.Web.Content;

/// <summary>
/// The full editorial front-matter field set validated by <see cref="PlaylistContentValidator"/>
/// (US-006: schema/taxonomy validation, spec section 9.1). Wider than
/// <see cref="PlaylistFrontMatter"/> (US-005's render-only reader), which intentionally omits
/// schemaVersion, era and publishedAt because the render path never needs them - see that type's
/// own doc comment. Kept as a separate model rather than widening the render DTO, so the render
/// path and the validation path stay decoupled.
/// </summary>
public sealed class PlaylistValidationFrontMatter
{
    public int? SchemaVersion { get; set; }
    public string? Slug { get; set; }
    public string? SpotifyPlaylistId { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string[]? Moods { get; set; }
    public string[]? Genres { get; set; }
    public string[]? Occasions { get; set; }
    public string? Era { get; set; }
    public string? PublishedAt { get; set; }
    public string? Status { get; set; }
}
