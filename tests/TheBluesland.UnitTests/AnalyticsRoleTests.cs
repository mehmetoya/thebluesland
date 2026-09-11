using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using Xunit;

namespace TheBluesland.UnitTests;

/// <summary>
/// docs/specs/visitor-and-playlist-click-analytics.md Testing Strategy: a Testcontainers-backed
/// role-privilege test mirroring SpotifyPlaylistCacheRoleTests.cs exactly - <c>analytics_writer</c>
/// can INSERT into <c>page_view_events</c>, cannot SELECT from it, and cannot touch
/// <c>spotify_playlist_cache</c> at all (neither read nor write). Both schemas and both role scripts
/// are applied to the same disposable Postgres instance so the "completely disjoint from the
/// spotify cache roles" claim (Design section 2) is proven against a real database, not just
/// asserted in a comment.
/// </summary>
public sealed class AnalyticsRoleTests : IAsyncLifetime
{
    private const string AnalyticsWriterRoleName = "analytics_writer";
    private const string AnalyticsWriterRolePassword = "placeholder-analytics-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var analyticsOptionsBuilder = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());
        await using (var analyticsContext = new AnalyticsDbContext(analyticsOptionsBuilder.Options))
        {
            await analyticsContext.Database.MigrateAsync();
        }

        // The cache schema is migrated too, purely so this test can prove analytics_writer cannot
        // touch it - the roles/tables are otherwise unrelated.
        var cacheOptionsBuilder = new DbContextOptionsBuilder<TheBlueslandDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());
        await using (var cacheContext = new TheBlueslandDbContext(cacheOptionsBuilder.Options))
        {
            await cacheContext.Database.MigrateAsync();
        }

        await using var adminConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await adminConnection.OpenAsync();

        await using var analyticsRoleScriptCommand = adminConnection.CreateCommand();
        analyticsRoleScriptCommand.CommandText = ReadEmbeddedScript("create-analytics-role.sql");
        await analyticsRoleScriptCommand.ExecuteNonQueryAsync();

        await using var cacheRoleScriptCommand = adminConnection.CreateCommand();
        cacheRoleScriptCommand.CommandText = ReadEmbeddedScript("create-spotify-cache-roles.sql");
        await cacheRoleScriptCommand.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task AnalyticsWriter_insert_into_page_view_events_succeeds()
    {
        // Regression test (2026-09-11 production incident): a raw INSERT with no RETURNING clause
        // (the old version of this test) passed even when the role lacked SELECT on the id column,
        // because EF Core's Npgsql provider generates INSERT ... RETURNING id for the identity
        // column - which Postgres rejects without column-level SELECT - and that only ever showed
        // up against real production traffic. Going through AnalyticsDbContext/SaveChangesAsync
        // here exercises the actual runtime code path (PageViewRecorder), not a hand-written
        // approximation of it.
        var optionsBuilder = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql(BuildRoleConnectionString());
        await using var context = new AnalyticsDbContext(optionsBuilder.Options);
        context.PageViewEvents.Add(new()
        {
            OccurredAt = DateTimeOffset.UtcNow,
            EventType = "page_view",
            Path = "/",
            PlaylistSlug = null,
            VisitorHash = "test-hash",
        });

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task AnalyticsWriter_select_of_event_content_from_page_view_events_is_rejected()
    {
        // analytics_writer has column-level SELECT on `id` only (required for EF Core's
        // INSERT ... RETURNING id - see create-analytics-role.sql's 2026-09-11 incident note).
        // This is the boundary that actually matters: no event content is ever readable back,
        // even though a narrow, unavoidable id-only exception exists (see the next test).
        await using var connection = new NpgsqlConnection(BuildRoleConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select visitor_hash from page_view_events";

        var exception = await Should.ThrowAsync<PostgresException>(() => command.ExecuteScalarAsync());

        exception.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task AnalyticsWriter_can_count_rows_but_not_read_their_content()
    {
        // Documents an intentional, narrow side effect of the id-only SELECT grant: PostgreSQL
        // allows count(*) once a role has ANY column-level privilege on a table, since counting
        // rows doesn't read column values. This reveals row existence/count only - visitor_hash,
        // path, event_type etc. remain unreadable (previous test).
        await using var connection = new NpgsqlConnection(BuildRoleConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from page_view_events";

        var count = await command.ExecuteScalarAsync();

        count.ShouldNotBeNull();
    }

    [Fact]
    public async Task AnalyticsWriter_select_from_spotify_playlist_cache_is_rejected()
    {
        await using var connection = new NpgsqlConnection(BuildRoleConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from spotify_playlist_cache";

        var exception = await Should.ThrowAsync<PostgresException>(() => command.ExecuteScalarAsync());

        exception.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task AnalyticsWriter_insert_into_spotify_playlist_cache_is_rejected()
    {
        await using var connection = new NpgsqlConnection(BuildRoleConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into spotify_playlist_cache
                (spotify_playlist_id, name, track_count, artists, synced_at, is_available)
            values
                ('analytics-writer-attempt', 'test playlist', 1, array['test artist'], now(), true)
            """;

        var exception = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        exception.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    private string BuildRoleConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = AnalyticsWriterRoleName,
            Password = AnalyticsWriterRolePassword,
        };
        return builder.ConnectionString;
    }

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
