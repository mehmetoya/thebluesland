using Shouldly;
using TheBluesland.SpotifyFetcher.Content;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// Auto-unpublish-private-playlists spec: <see cref="PlaylistFrontMatterWriter.UnpublishAsync"/>
/// must be a targeted line replace/removal, never a full YAML round-trip - every other line,
/// including the Markdown body and any front-matter field around <c>status</c>/<c>publishedAt</c>,
/// must survive unchanged.
/// </summary>
public sealed class PlaylistFrontMatterWriterTests : IDisposable
{
    private readonly string _filePath = Path.GetTempFileName();
    private readonly PlaylistFrontMatterWriter _writer = new();

    public void Dispose() => File.Delete(_filePath);

    [Fact]
    public async Task UnpublishAsync_flips_status_to_draft_and_drops_publishedAt_leaving_every_other_line_unchanged()
    {
        var original =
            """
            ---
            schemaVersion: 1
            slug: my-shazam-tracks
            spotifyPlaylistId: 46ZyMuOkFZ0A4WkLwnMNh9
            title: "My Shazam Tracks"
            status: published
            publishedAt: 2026-09-05
            featuredOrder: 1
            ---

            A collection of musical discoveries across genres.
            """;
        await File.WriteAllTextAsync(_filePath, original);

        await _writer.UnpublishAsync(_filePath, CancellationToken.None);

        var rewritten = await File.ReadAllLinesAsync(_filePath);
        rewritten.ShouldBe(
            [
                "---",
                "schemaVersion: 1",
                "slug: my-shazam-tracks",
                "spotifyPlaylistId: 46ZyMuOkFZ0A4WkLwnMNh9",
                "title: \"My Shazam Tracks\"",
                "status: draft",
                "featuredOrder: 1",
                "---",
                "",
                "A collection of musical discoveries across genres.",
            ]);
    }

    [Fact]
    public async Task UnpublishAsync_leaves_an_already_draft_file_s_body_untouched_when_no_publishedAt_line_exists()
    {
        var original =
            """
            ---
            slug: already-draft
            status: draft
            ---

            Body text.
            """;
        await File.WriteAllTextAsync(_filePath, original);

        await _writer.UnpublishAsync(_filePath, CancellationToken.None);

        var rewritten = await File.ReadAllLinesAsync(_filePath);
        rewritten.ShouldBe(
            [
                "---",
                "slug: already-draft",
                "status: draft",
                "---",
                "",
                "Body text.",
            ]);
    }
}
