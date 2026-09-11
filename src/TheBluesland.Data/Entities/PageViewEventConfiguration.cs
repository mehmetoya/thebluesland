using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TheBluesland.Data.Entities;

public sealed class PageViewEventConfiguration : IEntityTypeConfiguration<PageViewEvent>
{
    public void Configure(EntityTypeBuilder<PageViewEvent> builder)
    {
        builder.ToTable("page_view_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasColumnType("bigint")
            .ValueGeneratedOnAdd();

        builder.Property(e => e.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Path)
            .HasColumnName("path")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.PlaylistSlug)
            .HasColumnName("playlist_slug")
            .HasColumnType("text");

        builder.Property(e => e.VisitorHash)
            .HasColumnName("visitor_hash")
            .HasColumnType("text")
            .IsRequired();
    }
}
