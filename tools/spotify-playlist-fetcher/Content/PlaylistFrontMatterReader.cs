using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TheBluesland.SpotifyFetcher.Content;

/// <summary>
/// Reads the <c>spotifyPlaylistId</c> front-matter field out of every <c>content/playlists/*.md</c>
/// file. This is the sync tool's only way of discovering which playlists exist (spec section
/// 12.4): the tool never adds or discovers a playlist on its own. Only the YAML front-matter block
/// is parsed - the Markdown body (curator note) is never read by this tool.
/// </summary>
public sealed class PlaylistFrontMatterReader
{
    private const string FrontMatterDelimiter = "---";

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Returns the distinct <c>spotifyPlaylistId</c> values found across every <c>*.md</c> file in
    /// <paramref name="contentDirectory"/>. Returns an empty list, without error, if the directory
    /// does not exist or contains no Markdown files - <c>content/playlists</c> may not exist yet
    /// (US-015 has not landed) and the tool must still complete successfully.
    /// </summary>
    public async Task<IReadOnlyList<string>> ReadDistinctSpotifyPlaylistIdsAsync(
        string contentDirectory,
        CancellationToken cancellationToken)
    {
        var entries = await ReadAllAsync(contentDirectory, cancellationToken);
        return entries.Select(entry => entry.SpotifyPlaylistId).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// US-023: like <see cref="ReadDistinctSpotifyPlaylistIdsAsync"/>, but also returns each
    /// file's <c>slug</c> and current <c>eras</c> so the era report can label its output and show
    /// a current-vs-suggested comparison. Still read-only - never writes to
    /// <paramref name="contentDirectory"/>.
    /// </summary>
    public async Task<IReadOnlyList<PlaylistFrontMatterEntry>> ReadAllAsync(
        string contentDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(contentDirectory))
        {
            return [];
        }

        var entries = new List<PlaylistFrontMatterEntry>();
        foreach (var filePath in Directory
                     .EnumerateFiles(contentDirectory, "*.md", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var frontMatter = await ReadFrontMatterAsync(filePath, cancellationToken);
            if (frontMatter?.SpotifyPlaylistId is not { Length: > 0 } spotifyPlaylistId)
            {
                continue;
            }

            entries.Add(new PlaylistFrontMatterEntry(
                spotifyPlaylistId,
                frontMatter.Slug is { Length: > 0 } slug ? slug : Path.GetFileNameWithoutExtension(filePath),
                frontMatter.Eras ?? [],
                frontMatter.Status,
                filePath));
        }

        return entries;
    }

    private async Task<PlaylistFrontMatter?> ReadFrontMatterAsync(string filePath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var yaml = ExtractFrontMatterYaml(content);
        return yaml is null ? null : _deserializer.Deserialize<PlaylistFrontMatter?>(yaml);
    }

    private static string? ExtractFrontMatterYaml(string content)
    {
        using var reader = new StringReader(content);
        if (reader.ReadLine()?.Trim() != FrontMatterDelimiter)
        {
            return null;
        }

        var yamlBuilder = new StringBuilder();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim() == FrontMatterDelimiter)
            {
                return yamlBuilder.ToString();
            }

            yamlBuilder.AppendLine(line);
        }

        return null; // no closing delimiter - not valid front matter, nothing to read
    }
}
