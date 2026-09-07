using System.Text;
using TheBluesland.SpotifyFetcher.Content;
using TheBluesland.SpotifyFetcher.Spotify;

namespace TheBluesland.SpotifyFetcher.EraReport;

/// <summary>
/// US-023: builds a Markdown suggestion report for the era filter, one section per playlist in
/// <c>content/playlists</c>. Reads each playlist's track release years from Spotify one playlist
/// at a time, computes its distribution, and immediately discards the raw years - only
/// <see cref="PlaylistEraDistributionResult"/>'s aggregate numbers ever reach the report text.
/// Mirrors <c>CuratorNote.CuratorNoteSuggestionService</c>'s pattern (ADR-0005): this service
/// never writes to a database or to <c>content/playlists/*.md</c> - its return value is written
/// only to stdout/job summary/artifact by the caller.
/// </summary>
public sealed class PlaylistEraReportService
{
    private readonly SpotifyPlaylistClient _playlistClient;
    private readonly PlaylistEraDistributionCalculator _calculator = new();

    public PlaylistEraReportService(SpotifyPlaylistClient playlistClient)
    {
        _playlistClient = playlistClient;
    }

    public async Task<string> BuildReportAsync(
        IReadOnlyList<PlaylistFrontMatterEntry> playlists,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var report = new StringBuilder();
        report.AppendLine("# Era distribution report (US-023)");
        report.AppendLine();
        report.AppendLine(
            "Suggestions only - Mehmet decides which, if any, to apply through a normal content pull request.");
        report.AppendLine();

        var processed = 0;
        foreach (var playlist in playlists.OrderBy(playlist => playlist.Slug, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Console.Error.WriteLine($"Era report [{++processed}/{playlists.Count}]: {playlist.Slug}");
            var sample = await _playlistClient.GetTrackReleaseYearsAsync(
                playlist.SpotifyPlaylistId, accessToken, cancellationToken);
            var distribution = _calculator.Calculate(sample.ReleaseYears);

            AppendSection(report, playlist, distribution, sample.WasSampled);
        }

        return report.ToString();
    }

    private static void AppendSection(
        StringBuilder report,
        PlaylistFrontMatterEntry playlist,
        PlaylistEraDistributionResult distribution,
        bool wasSampled)
    {
        report.AppendLine($"## {playlist.Slug}");
        report.AppendLine();
        report.AppendLine($"- Dated tracks read: {distribution.DatedTrackCount}");

        if (wasSampled)
        {
            // Says outright that this playlist is longer than the read - a proportion from a few
            // hundred tracks is sound, but the reader should know it is an estimate, not a census.
            report.AppendLine(
                "- Sampled: yes - the playlist is longer than the page cap, so these percentages " +
                "estimate the distribution from the tracks read above.");
        }
        report.AppendLine(
            $"- Current eras: {(playlist.Eras.Count > 0 ? string.Join(", ", playlist.Eras) : "(none)")}");

        if (distribution.HasInsufficientData)
        {
            report.AppendLine(
                "- Insufficient data: fewer than 10 tracks had a readable release year - no suggestion.");
            report.AppendLine();
            return;
        }

        foreach (var bucket in EraBucketMapper.ConcreteBuckets)
        {
            report.AppendLine($"  - {bucket}: {distribution.BucketPercentages[bucket]:P0}");
        }

        report.AppendLine($"- Suggested eras: {string.Join(", ", distribution.SuggestedEras)}");

        var suggestionMatchesCurrent = distribution.SuggestedEras
            .OrderBy(era => era, StringComparer.Ordinal)
            .SequenceEqual(playlist.Eras.OrderBy(era => era, StringComparer.Ordinal));

        if (suggestionMatchesCurrent)
        {
            report.AppendLine("- Change proposed: no");
        }
        else
        {
            report.AppendLine("- Change proposed: yes");
            report.AppendLine("  ```yaml");
            report.AppendLine("  eras:");
            foreach (var era in distribution.SuggestedEras)
            {
                report.AppendLine($"    - {era}");
            }
            report.AppendLine("  ```");
        }

        report.AppendLine();
    }
}
