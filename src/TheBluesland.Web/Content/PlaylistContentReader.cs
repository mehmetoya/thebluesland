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

        var (englishNote, turkishNote) = SplitTurkishNote(body);
        return new PlaylistContent(
            slug,
            spotifyPlaylistId,
            frontMatter.Title ?? string.Empty,
            frontMatter.Summary ?? string.Empty,
            frontMatter.Moods ?? [],
            frontMatter.Genres ?? [],
            frontMatter.Occasions ?? [],
            frontMatter.Eras ?? [],
            englishNote,
            string.Equals(frontMatter.Status, PublishedStatus, StringComparison.OrdinalIgnoreCase),
            frontMatter.FeaturedOrder,
            frontMatter.DisplayOrder ?? 0,
            ParsePublishedAt(frontMatter.PublishedAt),
            frontMatter.PreviousSlugs ?? [])
        {
            TitleTr = NullIfBlank(frontMatter.TitleTr),
            SummaryTr = NullIfBlank(frontMatter.SummaryTr),
            CuratorNoteTr = turkishNote,
        };
    }

    // Optional Turkish curator note: everything after a line that is exactly the marker. The English
    // note is what comes before it (or the whole body when there is no marker).
    private static (string English, string? Turkish) SplitTurkishNote(string body)
    {
        var lines = body.ReplaceLineEndings("\n").Split('\n');
        var markerIndex = Array.FindIndex(lines, line => line.Trim() == PlaylistContent.TurkishNoteMarker);
        if (markerIndex < 0)
        {
            return (body.Trim(), null);
        }

        return (
            string.Join('\n', lines[..markerIndex]).Trim(),
            NullIfBlank(string.Join('\n', lines[(markerIndex + 1)..]).Trim()));
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Draft content may omit publishedAt entirely (US-006); an unparsable value degrades to null
    // rather than throwing, matching this reader's "malformed content must not crash render"
    // contract (see the class doc comment).
    private static DateOnly? ParsePublishedAt(string? publishedAt) =>
        publishedAt is { Length: > 0 } &&
        DateOnly.TryParseExact(publishedAt, PublishedAtFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
