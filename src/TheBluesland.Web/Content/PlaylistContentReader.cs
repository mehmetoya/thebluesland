using System.Globalization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TheBluesland.Web.Content;

/// <summary>
/// Reads editorial Markdown files under <c>content/playlists</c> into <see cref="PlaylistContent"/>.
/// Only the render-relevant front-matter fields are mapped (see <see cref="PlaylistFrontMatter"/>);
/// full schema/taxonomy validation is US-006's job. A file whose front matter is missing or lacks a
/// slug/spotifyPlaylistId is skipped rather than throwing, so one malformed file cannot take down
/// the whole render surface (spec 16.1: content problems must not turn into a 500).
/// </summary>
public sealed class PlaylistContentReader
{
    private const string PublishedStatus = "published";
    private const string PublishedAtFormat = "yyyy-MM-dd";

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public async Task<IReadOnlyList<PlaylistContent>> ReadAllAsync(
        string contentDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(contentDirectory))
        {
            return [];
        }

        var playlists = new List<PlaylistContent>();
        foreach (var filePath in Directory
                     .EnumerateFiles(contentDirectory, "*.md", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var playlist = await ReadAsync(filePath, cancellationToken);
            if (playlist is not null)
            {
                playlists.Add(playlist);
            }
        }

        return playlists;
    }

    private async Task<PlaylistContent?> ReadAsync(string filePath, CancellationToken cancellationToken)
    {
        var rawContent = await File.ReadAllTextAsync(filePath, cancellationToken);
        var (yaml, body) = FrontMatterSplitter.Split(rawContent);
        if (yaml is null)
        {
            return null;
        }

        var frontMatter = _deserializer.Deserialize<PlaylistFrontMatter?>(yaml);
        if (frontMatter?.Slug is not { Length: > 0 } slug ||
            frontMatter.SpotifyPlaylistId is not { Length: > 0 } spotifyPlaylistId)
        {
            return null;
        }

        return new PlaylistContent(
            slug,
            spotifyPlaylistId,
            frontMatter.Title ?? string.Empty,
            frontMatter.Summary ?? string.Empty,
            frontMatter.Moods ?? [],
            frontMatter.Genres ?? [],
            frontMatter.Occasions ?? [],
            frontMatter.Era ?? string.Empty,
            body.Trim(),
            string.Equals(frontMatter.Status, PublishedStatus, StringComparison.OrdinalIgnoreCase),
            frontMatter.Featured ?? false,
            frontMatter.DisplayOrder ?? 0,
            ParsePublishedAt(frontMatter.PublishedAt));
    }

    // Draft content may omit publishedAt entirely (US-006); an unparsable value degrades to null
    // rather than throwing, matching this reader's "malformed content must not crash render"
    // contract (see the class doc comment).
    private static DateOnly? ParsePublishedAt(string? publishedAt) =>
        publishedAt is { Length: > 0 } &&
        DateOnly.TryParseExact(publishedAt, PublishedAtFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
