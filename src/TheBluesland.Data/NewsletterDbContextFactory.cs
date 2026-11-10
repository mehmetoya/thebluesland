using Microsoft.EntityFrameworkCore.Design;

namespace TheBluesland.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations add --context NewsletterDbContext</c>. The
/// connection string is a local placeholder: migrations are generated here but applied to Neon
/// only by Mehmet himself (news-ingest spec, Assumptions 3).
/// </summary>
public sealed class NewsletterDbContextFactory : IDesignTimeDbContextFactory<NewsletterDbContext>
{
    public NewsletterDbContext CreateDbContext(string[] args) =>
        NewsletterDbContext.Create("Host=localhost;Database=thebluesland;Username=postgres;Password=postgres");
}
