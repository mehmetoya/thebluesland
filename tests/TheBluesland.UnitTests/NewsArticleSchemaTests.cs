using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using Xunit;

namespace TheBluesland.UnitTests;

/// <summary>
/// docs/specs/weekly-newsletter-news-ingest.md, Design section 1: the NewsletterDbContext migration
/// creates newsletter.news_article with the spec's columns, a unique normalized_url and the
/// coalesce(published_at, fetched_at) time index - all inside the newsletter schema, including the
/// context's own migrations history table, so nothing lands in public.
/// </summary>
public sealed class NewsArticleSchemaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var context = NewsletterDbContext.Create(_postgres.GetConnectionString());
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_creates_news_article_with_the_spec_columns()
    {
        var columns = await QueryAsync(
            """
            select column_name, data_type, is_nullable
            from information_schema.columns
            where table_schema = 'newsletter' and table_name = 'news_article'
            """,
            reader => (Name: reader.GetString(0), Type: reader.GetString(1), Nullable: reader.GetString(2) == "YES"));

        columns.ShouldBe(
        [
            ("categories", "ARRAY", false),
            ("fetched_at", "timestamp with time zone", false),
            ("id", "bigint", false),
            ("normalized_url", "text", false),
            ("published_at", "timestamp with time zone", true),
            ("source", "text", false),
            ("summary", "text", true),
            ("title", "text", false),
            ("url", "text", false),
        ], ignoreOrder: true);
    }

    [Fact]
    public async Task Migration_keeps_every_newsletter_table_out_of_the_public_schema()
    {
        var publicTables = await QueryAsync(
            "select table_name from information_schema.tables where table_schema = 'public'",
            reader => reader.GetString(0));
        var newsletterTables = await QueryAsync(
            "select table_name from information_schema.tables where table_schema = 'newsletter'",
            reader => reader.GetString(0));

        publicTables.ShouldBeEmpty();
        newsletterTables.ShouldBe(["__ef_migrations_history", "news_article"], ignoreOrder: true);
    }

    [Fact]
    public async Task Duplicate_normalized_url_is_rejected_by_the_database()
    {
        await InsertAsync("https://nme.com/a");

        var exception = await Should.ThrowAsync<PostgresException>(() => InsertAsync("https://nme.com/a"));

        exception.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Categories_default_to_an_empty_array()
    {
        await ExecuteAsync(
            """
            insert into newsletter.news_article (source, title, url, normalized_url, fetched_at)
            values ('nme', 't', 'https://nme.com/b', 'https://nme.com/b', now())
            """);

        var count = await QueryAsync(
            "select cardinality(categories) from newsletter.news_article where normalized_url = 'https://nme.com/b'",
            reader => reader.GetInt32(0));

        count.ShouldBe([0]);
    }

    [Fact]
    public async Task Migration_creates_the_effective_time_index()
    {
        var definitions = await QueryAsync(
            "select indexdef from pg_indexes where schemaname = 'newsletter' and tablename = 'news_article'",
            reader => reader.GetString(0));

        definitions.ShouldContain(definition => definition.Contains("COALESCE(published_at, fetched_at)", StringComparison.Ordinal));
        definitions.ShouldContain(definition => definition.Contains("UNIQUE", StringComparison.Ordinal) && definition.Contains("normalized_url", StringComparison.Ordinal));
    }

    private Task InsertAsync(string normalizedUrl) => ExecuteAsync(
        $"""
        insert into newsletter.news_article (source, title, url, normalized_url, categories, fetched_at)
        values ('nme', 'title', '{normalizedUrl}', '{normalizedUrl}', '{"{}"}', now())
        """);

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> map)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(map(reader));
        }

        return results;
    }
}
