using Microsoft.EntityFrameworkCore;
using TheBluesland.Data;
using TheBluesland.Data.Entities;

namespace TheBluesland.Web.Analytics;

/// <summary>
/// Fire-and-forget writer for <c>page_view_events</c> (docs/specs/visitor-and-playlist-click-analytics.md,
/// Design sections 4-5). Mirrors <see cref="TheBluesland.Web.Cache.PlaylistCacheLookup"/>'s
/// never-throws graceful-degradation contract, but for a write instead of a read: an unreachable
/// <c>Analytics</c> connection string must never affect the response already being served.
///
/// <see cref="RecordFireAndForget"/> takes only plain values (never <c>HttpContext</c>) precisely so
/// the detached background task never touches a request scope that may already be disposed by the
/// time it runs. It creates its own <see cref="AnalyticsDbContext"/> via
/// <see cref="IDbContextFactory{TContext}"/> and uses <see cref="CancellationToken.None"/> - a
/// request ending must not cancel a write already in flight.
/// </summary>
public sealed class PageViewRecorder
{
    private readonly IDbContextFactory<AnalyticsDbContext> _dbContextFactory;
    private readonly ILogger<PageViewRecorder> _logger;

    public PageViewRecorder(IDbContextFactory<AnalyticsDbContext> dbContextFactory, ILogger<PageViewRecorder> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public void RecordFireAndForget(string eventType, string path, string? playlistSlug, string visitorHash, DateTimeOffset occurredAt)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var dbContext = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
                dbContext.PageViewEvents.Add(new PageViewEvent
                {
                    OccurredAt = occurredAt,
                    EventType = eventType,
                    Path = path,
                    PlaylistSlug = playlistSlug,
                    VisitorHash = visitorHash,
                });
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to record {EventType} analytics event for {Path}; discarding.", eventType, path);
            }
        });
    }
}
