using System.Text;
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
    private const string FrontMatterDelimiter = "---";
    private const string PublishedStatus = "published";

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
        var (yaml, body) = SplitFrontMatter(rawContent);
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
            body.Trim(),
            string.Equals(frontMatter.Status, PublishedStatus, StringComparison.OrdinalIgnoreCase));
    }

    private static (string? Yaml, string Body) SplitFrontMatter(string content)
    {
        using var reader = new StringReader(content);
        if (reader.ReadLine()?.Trim() != FrontMatterDelimiter)
        {
            return (null, string.Empty);
        }

        var yamlBuilder = new StringBuilder();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim() == FrontMatterDelimiter)
            {
                return (yamlBuilder.ToString(), reader.ReadToEnd());
            }

            yamlBuilder.AppendLine(line);
        }

        return (null, string.Empty); // no closing delimiter - not valid front matter
    }
}
