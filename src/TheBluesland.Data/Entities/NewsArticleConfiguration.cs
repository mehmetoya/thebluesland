using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TheBluesland.Data.Entities;

public sealed class NewsArticleConfiguration : IEntityTypeConfiguration<NewsArticle>
{
    public void Configure(EntityTypeBuilder<NewsArticle> builder)
    {
        builder.ToTable("news_article");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasColumnType("bigint")
            .ValueGeneratedOnAdd();

        builder.Property(e => e.Source)
            .HasColumnName("source")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Title)
            .HasColumnName("title")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Url)
            .HasColumnName("url")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.NormalizedUrl)
            .HasColumnName("normalized_url")
            .HasColumnType("text")
            .IsRequired();

        builder.HasIndex(e => e.NormalizedUrl)
            .IsUnique()
            .HasDatabaseName("ux_news_article_normalized_url");

        builder.Property(e => e.Summary)
            .HasColumnName("summary")
            .HasColumnType("text");

        builder.Property(e => e.Categories)
            .HasColumnName("categories")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired();

        builder.Property(e => e.PublishedAt)
            .HasColumnName("published_at")
            .HasColumnType("timestamptz");

        builder.Property(e => e.FetchedAt)
            .HasColumnName("fetched_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // The coalesce(published_at, fetched_at) time index is an expression index, which EF Core
        // cannot model; it is created with raw SQL in the InitialCreate migration.
    }
}
