using Microsoft.EntityFrameworkCore;
using TheBluesland.Data.Entities;

namespace TheBluesland.Data;

/// <summary>
/// Third, narrowly-scoped EF Core model (docs/specs/weekly-newsletter-news-ingest.md, Design
/// section 1/5) - same reasoning as <see cref="AnalyticsDbContext"/>: its own connection string,
/// its own Postgres role (<c>newsletter_writer</c>, <c>Scripts/create-newsletter-role.sql</c>) and
/// its own <c>newsletter</c> schema, including the migrations history table, so the role never
/// needs anything in <c>public</c>. Used only by tools/weekly-newsletter; TheBluesland.Web never
/// registers it.
/// </summary>
public sealed class NewsletterDbContext : DbContext
{
    public const string Schema = "newsletter";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public NewsletterDbContext(DbContextOptions<NewsletterDbContext> options)
        : base(options)
    {
    }

    public DbSet<NewsArticle> NewsArticles => Set<NewsArticle>();

    /// <summary>
    /// The one way to build this context, so runtime, design-time and tests all agree on the
    /// history table location.
    /// </summary>
    public static NewsletterDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<NewsletterDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema))
            .Options;

        return new NewsletterDbContext(options);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new NewsArticleConfiguration());
    }
}
