using System.Text;
using Microsoft.EntityFrameworkCore;
using TheBluesland.Data;
using TheBluesland.SpotifyFetcher.Content;

namespace TheBluesland.SpotifyFetcher.EraReport;

/// <summary>
/// US-023/US-026: builds a Markdown era report, one section per playlist in
/// <c>content/playlists</c>, from the per-bucket counts the monthly sync already measured and
/// stored in <c>spotify_playlist_cache</c>.
///
/// <para>This service makes no Spotify call at all. It used to crawl every playlist's tracks a
/// second time, and on 2026-09-08 that duplicate crawl - run hours after a sync that had read the
/// very same release dates - locked the whole account out for ~23.8 hours. Reading tracks is by far
/// the most expensive thing this project does, so it is now done once, by the sync, and the numbers
/// are kept. As a side effect the report is also more accurate than it was: the sync reads every
/// page, whereas the old report sampled the first few hundred tracks.</para>
///
/// <para>Mirrors <c>CuratorNote.CuratorNoteSuggestionService</c>'s output boundary (ADR-0005): it
/// never writes to the database or to <c>content/playlists/*.md</c> - its return value is written
/// only to stdout/job summary/artifact by the caller, and the connection it reads through is the
/// read-only <c>spotify_cache_readonly</c> role.</para>
/// </summary>
public sealed class PlaylistEraReportService
{
    private readonly TheBlueslandDbContext _dbContext;
    private readonly PlaylistEraDistributionCalculator _calculator = new();

    public PlaylistEraReportService(TheBlueslandDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> BuildReportAsync(
        IReadOnlyList<PlaylistFrontMatterEntry> playlists,
        CancellationToken cancellationToken)
    {
        // One query for the whole catalogue - 120 rows of aggregates, no per-playlist round trip.
        var measurements = await _dbContext.SpotifyPlaylistCache
            .AsNoTracking()
            .Select(row => new { row.SpotifyPlaylistId, row.EraBucketCounts, row.SyncedAt, row.IsAvailable })
            .ToDictionaryAsync(row => row.SpotifyPlaylistId, cancellationToken);

        var report = new StringBuilder();
        report.AppendLine("# Era distribution report (US-023)");
        report.AppendLine();
        report.AppendLine(
            "Suggestions only - Mehmet decides which, if any, to apply through a normal content pull request.");
        report.AppendLine();
        report.AppendLine(
            "Measured by the monthly sync and read here from spotify_playlist_cache; no Spotify call is made "
            + "to produce this report, so it can be re-run at any time.");
        report.AppendLine();

        foreach (var playlist in playlists.OrderBy(playlist => playlist.Slug, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            report.AppendLine($"## {playlist.Slug}");
            report.AppendLine();
            report.AppendLine(
                $"- Current eras: {(playlist.Eras.Count > 0 ? string.Join(", ", playlist.Eras) : "(none)")}");

            if (!measurements.TryGetValue(playlist.SpotifyPlaylistId, out var measurement)
                || measurement.EraBucketCounts is null)
            {
                report.AppendLine(
                    "- Not measured yet: no cache row carries era bucket counts for this playlist. "
                    + "It gets them on the next sync.");
                report.AppendLine();
                continue;
            }

            if (!measurement.IsAvailable)
            {
                report.AppendLine("- Unavailable on Spotify at the last sync; the counts below may be stale.");
            }

            report.AppendLine($"- Measured: {measurement.SyncedAt:yyyy-MM-dd}");
            AppendDistribution(report, playlist, measurement.EraBucketCounts);
        }

        return report.ToString();
    }

    private void AppendDistribution(
        StringBuilder report,
        PlaylistFrontMatterEntry playlist,
        int[] storedCounts)
    {
        // Stored in EraBucketMapper.ConcreteBuckets order. A row written by an older tool version
        // could be shorter than the bucket list, so read defensively rather than throwing.
        var bucketCounts = EraBucketMapper.ConcreteBuckets
            .Select((bucket, index) => (bucket, count: index < storedCounts.Length ? storedCounts[index] : 0))
            .ToDictionary(entry => entry.bucket, entry => entry.count);

        var distribution = _calculator.CalculateFromBucketCounts(bucketCounts);

        report.AppendLine($"- Dated tracks: {distribution.DatedTrackCount}");

        if (distribution.HasInsufficientData)
        {
            report.AppendLine(
                "- Insufficient data: fewer than 10 tracks had a readable release year - no suggestion.");
            report.AppendLine();
            return;
        }

        foreach (var bucket in EraBucketMapper.ConcreteBuckets)
        {
            report.AppendLine(
                $"  - {bucket}: {distribution.BucketPercentages[bucket] * 100:0}% ({bucketCounts[bucket]})");
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
