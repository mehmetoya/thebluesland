using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TheBluesland.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations --context AnalyticsDbContext</c> tooling.
/// Same rationale as <see cref="TheBlueslandDbContextFactory"/>: TheBluesland.Data has no startup
/// project of its own. The connection string here is only used to generate migration files; it is
/// never used to actually connect to a database at design time.
/// </summary>
public sealed class AnalyticsDbContextFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AnalyticsDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=thebluesland;Username=postgres;Password=postgres");

        return new AnalyticsDbContext(optionsBuilder.Options);
    }
}
