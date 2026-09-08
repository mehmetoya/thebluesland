using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using TheBluesland.SpotifyFetcher.Content;
using TheBluesland.SpotifyFetcher.EraReport;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// US-023/US-026: <see cref="PlaylistEraReportService"/> builds its Markdown report from the
/// per-bucket counts the sync stored in <c>spotify_playlist_cache</c>, and from nothing else. It
/// holds no Spotify client at all, so the "no track-level data is ever output" limit (ADR-0002,
/// spec section 9.4/11.2) is now structural rather than something a test has to police: four
/// integers per playlist are the only measurement it can see. What these tests do police is that
/// the arithmetic and the three shapes of missing data behave as the report's readers expect.
/// </summary>
public sealed class PlaylistEraReportServiceTests : IAsyncLifetime
{
    private const string PlaylistId = "2m8X8fsMWor8A5AnmOHwzy";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task BuildReportAsync_derives_percentages_and_suggestions_from_the_stored_counts()
    {
        // 7 of 10 dated tracks in the 1970s (>=60% majority - mixed-era must not be suggested);
        // the other 3 in 2000s-present (>=20% - suggested alongside the 1970s).
        await SeedAsync([0, 7, 0, 3]);
        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistEraReportService(dbContext);

        var report = await service.BuildReportAsync(
            [new PlaylistFrontMatterEntry(PlaylistId, "dear-mr-fantasy", ["mixed-era"])],
            CancellationToken.None);

        report.ShouldContain("dear-mr-fantasy");
        report.ShouldContain("Dated tracks: 10");
        report.ShouldContain($"{EraBucketMapper.Seventies}: 70% (7)");
        report.ShouldContain($"{EraBucketMapper.TwoThousandsPresent}: 30% (3)");
        // Exactly these two, and no mixed-era: the 1970s bucket clears the 60% majority. Asserting
        // the whole line matters - "mixed-era" appears elsewhere in the section as the playlist's
        // current editorial era, so its mere presence in the report proves nothing.
        report.ShouldContain($"Suggested eras: {EraBucketMapper.Seventies}, {EraBucketMapper.TwoThousandsPresent}\n");
        report.ShouldContain("Change proposed: yes");
    }

    [Fact]
    public async Task BuildReportAsync_reports_insufficient_data_and_no_suggestion_below_ten_dated_tracks()
    {
        await SeedAsync([2, 3, 2, 2]);
        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistEraReportService(dbContext);

        var report = await service.BuildReportAsync(
            [new PlaylistFrontMatterEntry(PlaylistId, "one-track-wonder", [])],
            CancellationToken.None);

        report.ShouldContain("one-track-wonder");
        report.ShouldContain("Insufficient data");
        report.ShouldNotContain("Suggested eras:");
    }

    [Fact]
    public async Task BuildReportAsync_says_not_measured_yet_when_the_row_carries_no_counts()
    {
        await SeedAsync(eraBucketCounts: null);
        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistEraReportService(dbContext);

        var report = await service.BuildReportAsync(
            [new PlaylistFrontMatterEntry(PlaylistId, "never-synced", ["mixed-era"])],
            CancellationToken.None);

        report.ShouldContain("Not measured yet");
        report.ShouldNotContain("Suggested eras:");
    }

    [Fact]
    public async Task BuildReportAsync_says_not_measured_yet_when_the_playlist_has_no_cache_row_at_all()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var service = new PlaylistEraReportService(dbContext);

        var report = await service.BuildReportAsync(
            [new PlaylistFrontMatterEntry(PlaylistId, "brand-new", [])],
            CancellationToken.None);

        report.ShouldContain("Not measured yet");
    }

    private async Task SeedAsync(int[]? eraBucketCounts)
    {
        await using var seedContext = await CreateMigratedDbContextAsync();
        seedContext.SpotifyPlaylistCache.Add(new()
        {
            SpotifyPlaylistId = PlaylistId,
            Name = "Dear Mr. Fantasy",
            TrackCount = 12,
            Artists = ["Traffic"],
            EraBucketCounts = eraBucketCounts,
            SyncedAt = DateTimeOffset.UtcNow,
            IsAvailable = true,
        });
        await seedContext.SaveChangesAsync();
    }

    private async Task<TheBlueslandDbContext> CreateMigratedDbContextAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TheBlueslandDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());
        var dbContext = new TheBlueslandDbContext(optionsBuilder.Options);
        await dbContext.Database.MigrateAsync();
        return dbContext;
    }
}
