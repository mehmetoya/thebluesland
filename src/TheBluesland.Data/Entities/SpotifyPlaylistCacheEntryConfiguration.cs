using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TheBluesland.Data.Entities;

public sealed class SpotifyPlaylistCacheEntryConfiguration : IEntityTypeConfiguration<SpotifyPlaylistCacheEntry>
{
    public void Configure(EntityTypeBuilder<SpotifyPlaylistCacheEntry> builder)
    {
        builder.ToTable("spotify_playlist_cache");

        builder.HasKey(e => e.SpotifyPlaylistId);

        builder.Property(e => e.SpotifyPlaylistId)
            .HasColumnName("spotify_playlist_id")
            .HasColumnType("text");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(e => e.CoverImageUrl)
            .HasColumnName("cover_image_url")
            .HasColumnType("text");

        builder.Property(e => e.TrackCount)
            .HasColumnName("track_count")
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(e => e.Artists)
            .HasColumnName("artists")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(e => e.SpotifySnapshotId)
            .HasColumnName("spotify_snapshot_id")
            .HasColumnType("text");

        builder.Property(e => e.SyncedAt)
            .HasColumnName("synced_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(e => e.IsAvailable)
            .HasColumnName("is_available")
            .HasColumnType("boolean")
            .IsRequired();
    }
}
