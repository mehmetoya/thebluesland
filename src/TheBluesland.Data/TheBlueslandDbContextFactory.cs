using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TheBluesland.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c> tooling. TheBluesland.Data has no
/// startup project of its own (it is a shared library consumed by TheBluesland.Web and
/// tools/spotify-playlist-fetcher), so EF Core tooling needs this factory to create migrations.
/// The connection string here is only used to generate migration files; it is never used to
/// actually connect to a database at design time.
/// </summary>
public sealed class TheBlueslandDbContextFactory : IDesignTimeDbContextFactory<TheBlueslandDbContext>
{
    public TheBlueslandDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TheBlueslandDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=thebluesland;Username=postgres;Password=postgres");

        return new TheBlueslandDbContext(optionsBuilder.Options);
    }
}
