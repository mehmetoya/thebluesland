using Microsoft.EntityFrameworkCore;
using TheBluesland.Data.Entities;

namespace TheBluesland.Data;

/// <summary>
/// Shared EF Core model for <c>spotify_playlist_cache</c>, referenced by both
/// <c>TheBluesland.Web</c> (read-only) and <c>tools/spotify-playlist-fetcher</c> (read/write).
/// See docs/adr/0002-spotify-veri-mimarisi.md and docs/adr/0003-mimari-kapsam.md for why this is
/// the only shared project in the repository.
/// </summary>
public sealed class TheBlueslandDbContext : DbContext
{
    public TheBlueslandDbContext(DbContextOptions<TheBlueslandDbContext> options)
        : base(options)
    {
    }

    public DbSet<SpotifyPlaylistCacheEntry> SpotifyPlaylistCache => Set<SpotifyPlaylistCacheEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SpotifyPlaylistCacheEntryConfiguration());
    }
}
