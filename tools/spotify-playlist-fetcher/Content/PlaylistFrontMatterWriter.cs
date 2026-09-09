namespace TheBluesland.SpotifyFetcher.Content;

/// <summary>
/// Rewrites a single <c>content/playlists/*.md</c> file's front matter to move it from
/// <c>status: published</c> to <c>status: draft</c> when <see cref="PlaylistCacheSyncService"/>
/// finds Spotify now reports the playlist private (auto-unpublish-private-playlists spec). A
/// targeted line replace/removal, never a full YAML round-trip re-serialization - re-serializing
/// risks reordering keys or reformatting the human-authored body/comments unpredictably. Every
/// other line, including the Markdown body, is preserved exactly as read.
///
/// One-directional only: this type has no method that could ever move a file from draft to
/// published - unpublishing stays the only write-back path this tool has.
/// </summary>
public sealed class PlaylistFrontMatterWriter
{
    private const string PublishedStatusLine = "status: published";
    private const string DraftStatusLine = "status: draft";
    private const string PublishedAtPrefix = "publishedAt:";

    public async Task UnpublishAsync(string filePath, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var rewrittenLines = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            if (line.TrimEnd() == PublishedStatusLine)
            {
                rewrittenLines.Add(DraftStatusLine);
                continue;
            }

            if (line.TrimStart().StartsWith(PublishedAtPrefix, StringComparison.Ordinal))
            {
                continue; // dropped entirely - matching the manual my-shazam-tracks.md edit
            }

            rewrittenLines.Add(line);
        }

        await File.WriteAllLinesAsync(filePath, rewrittenLines, cancellationToken);
    }
}
