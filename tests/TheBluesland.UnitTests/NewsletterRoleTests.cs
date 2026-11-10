using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using TheBluesland.Data.Entities;
using Xunit;

namespace TheBluesland.UnitTests;

/// <summary>
/// docs/specs/weekly-newsletter-news-ingest.md, Design section 5: <c>newsletter_writer</c> can do
/// exactly what the ingest command needs inside the newsletter schema (insert with RETURNING,
/// insert ... on conflict do nothing, select, delete for retention) and nothing else - no update,
/// no DDL, no migrations-history writes, and no access to spotify_playlist_cache or
/// page_view_events. All three schemas and all three role scripts share one disposable Postgres
/// so "disjoint from the other roles" is proven, not asserted. Writes go through the real
/// NewsletterDbContext where possible (AnalyticsRoleTests' 2026-09-11 RETURNING lesson).
/// </summary>
public sealed class NewsletterRoleTests : IAsyncLifetime
{
    private const string RoleName = "newsletter_writer";
    private const string RolePassword = "placeholder-newsletter-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var adminConnectionString = _postgres.GetConnectionString();

        await using (var newsletter = NewsletterDbContext.Create(adminConnectionString))
        {
            await newsletter.Database.MigrateAsync();
        }

        await using (var cache = new TheBlueslandDbContext(new DbContextOptionsBuilder<TheBlueslandDbContext>().UseNpgsql(adminConnectionString).Options))
        {
            await cache.Database.MigrateAsync();
        }

        await using (var analytics = new AnalyticsDbContext(new DbContextOptionsBuilder<AnalyticsDbContext>().UseNpgsql(adminConnectionString).Options))
        {
            await analytics.Database.MigrateAsync();
        }

        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        foreach (var script in new[] { "create-newsletter-role.sql", "create-spotify-cache-roles.sql", "create-analytics-role.sql" })
        {
            await using var command = admin.CreateCommand();
            command.CommandText = ReadEmbeddedScript(script);
            await command.ExecuteNonQueryAsync();
        }
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task NewsletterWriter_inserts_through_the_real_context_including_returning_id()
    {
        await using var context = NewsletterDbContext.Create(RoleConnectionString());
        var article = NewArticle("https://nme.com/insert-via-context");
        context.NewsArticles.Add(article);

        await context.SaveChangesAsync();

        article.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task NewsletterWriter_can_insert_on_conflict_do_nothing()
    {
        const string sql =
            """
            insert into newsletter.news_article (source, title, url, normalized_url, fetched_at)
            values ('nme', 't', 'https://nme.com/c', 'https://nme.com/c', now())
            on conflict (normalized_url) do nothing
            """;

        (await ExecuteAsRoleAsync(sql)).ShouldBe(1);
        (await ExecuteAsRoleAsync(sql)).ShouldBe(0);
    }

    [Fact]
    public async Task NewsletterWriter_can_read_back_and_delete_articles()
    {
        await using var context = NewsletterDbContext.Create(RoleConnectionString());
        context.NewsArticles.Add(NewArticle("https://nme.com/to-delete"));
        await context.SaveChangesAsync();

        var deleted = await context.NewsArticles
            .Where(a => a.NormalizedUrl == "https://nme.com/to-delete")
            .ExecuteDeleteAsync();

        deleted.ShouldBe(1);
    }

    [Fact]
    public async Task NewsletterWriter_update_is_rejected_because_ingest_never_rewrites_a_row()
    {
        await ExecuteAsRoleAsync(
            """
            insert into newsletter.news_article (source, title, url, normalized_url, fetched_at)
            values ('nme', 't', 'https://nme.com/u', 'https://nme.com/u', now())
            """);

        await ShouldBeDeniedAsync("update newsletter.news_article set title = 'changed'");
    }

    [Theory]
    [InlineData("create table newsletter.sneaky (id int)")]
    [InlineData("drop table newsletter.news_article")]
    [InlineData("alter table newsletter.news_article add column extra text")]
    [InlineData("delete from newsletter.__ef_migrations_history")]
    [InlineData("insert into newsletter.__ef_migrations_history values ('x', 'y')")]
    public async Task NewsletterWriter_cannot_change_the_schema_or_migrations_history(string sql)
    {
        await ShouldBeDeniedAsync(sql);
    }

    [Theory]
    [InlineData("select count(*) from public.spotify_playlist_cache")]
    [InlineData("insert into public.spotify_playlist_cache (spotify_playlist_id, name, track_count, artists, synced_at, is_available) values ('x', 'x', 1, array['x'], now(), true)")]
    [InlineData("select count(*) from public.page_view_events")]
    [InlineData("insert into public.page_view_events (occurred_at, event_type, path, visitor_hash) values (now(), 'page_view', '/', 'h')")]
    public async Task NewsletterWriter_cannot_touch_spotify_or_analytics_tables(string sql)
    {
        await ShouldBeDeniedAsync(sql);
    }

    [Fact]
    public async Task Role_script_can_be_run_twice()
    {
        await using var admin = new NpgsqlConnection(_postgres.GetConnectionString());
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = ReadEmbeddedScript("create-newsletter-role.sql");

        await Should.NotThrowAsync(() => command.ExecuteNonQueryAsync());
    }

    private static NewsArticle NewArticle(string normalizedUrl) => new()
    {
        Source = "nme",
        Title = "Title",
        Url = normalizedUrl,
        NormalizedUrl = normalizedUrl,
        FetchedAt = DateTimeOffset.UtcNow,
    };

    private async Task ShouldBeDeniedAsync(string sql)
    {
        var exception = await Should.ThrowAsync<PostgresException>(() => ExecuteAsRoleAsync(sql));

        exception.SqlState.ShouldBeOneOf(PostgresErrorCodes.InsufficientPrivilege, "42501");
    }

    private async Task<int> ExecuteAsRoleAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(RoleConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }

    private string RoleConnectionString() => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
    {
        Username = RoleName,
        Password = RolePassword,
    }.ConnectionString;

    private static string ReadEmbeddedScript(string fileName)
    {
        var assembly = typeof(TheBlueslandDbContext).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(fileName, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
