using Microsoft.EntityFrameworkCore;
using TheBluesland.Data.Entities;

namespace TheBluesland.Data;

/// <summary>
/// Second, narrowly-scoped EF Core model - deliberately separate from <see cref="TheBlueslandDbContext"/>
/// (docs/specs/visitor-and-playlist-click-analytics.md, Design section 1). The production web app's
/// connection to <c>spotify_playlist_cache</c> is read-only by design (ADR-0002); bolting a
/// <c>page_view_events</c> DbSet onto that same context would force a single connection string, and
/// a single Postgres role, to cover both concerns. Instead this context has its own connection
/// string (<c>Analytics</c>) and its own insert-only Postgres role (<c>analytics_writer</c>, see
/// <c>Scripts/create-analytics-role.sql</c>) that cannot read or write <c>spotify_playlist_cache</c>
/// at all.
/// </summary>
public sealed class AnalyticsDbContext : DbContext
{
    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options)
        : base(options)
    {
    }

    public DbSet<PageViewEvent> PageViewEvents => Set<PageViewEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PageViewEventConfiguration());
    }
}
