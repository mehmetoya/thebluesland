using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using Xunit;

namespace TheBluesland.UnitTests;

/// <summary>
/// US-002, acceptance criterion 1: a connection using the read-only Postgres role must be able to
/// SELECT from spotify_playlist_cache but must have every write (INSERT/UPDATE) rejected by
/// Postgres itself, while a connection using the read/write role can write. This proves the role
/// separation mechanism in src/TheBluesland.Data/Scripts/create-spotify-cache-roles.sql against a
/// real Postgres instance (Testcontainers), consistent with spec section 13 (SEC-001): the
/// production web app holds only a read-only connection string, the read/write connection string
/// is a GitHub Actions secret used only by the sync tool.
/// </summary>
public sealed class SpotifyPlaylistCacheRoleTests : IAsyncLifetime
{
    private const string ReadonlyRoleName = "spotify_cache_readonly";
    private const string ReadonlyRolePassword = "placeholder-readonly-password";
    private const string ReadwriteRoleName = "spotify_cache_readwrite";
    private const string ReadwriteRolePassword = "placeholder-readwrite-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var optionsBuilder = new DbContextOptionsBuilder<TheBlueslandDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());
        await using (var context = new TheBlueslandDbContext(optionsBuilder.Options))
        {
            await context.Database.MigrateAsync();
        }

        await using var adminConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await adminConnection.OpenAsync();
        await using var roleScriptCommand = adminConnection.CreateCommand();
        roleScriptCommand.CommandText = ReadRoleScript();
        await roleScriptCommand.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Readonly_role_insert_is_rejected()
    {
        await using var connection = new NpgsqlConnection(BuildRoleConnectionString(ReadonlyRoleName, ReadonlyRolePassword));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = InsertRowSql("readonly-insert-attempt");

        var exception = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        exception.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Readonly_role_update_is_rejected()
    {
        await SeedOneRowAsReadWriteRoleAsync("seed-for-update");

        await using var connection = new NpgsqlConnection(BuildRoleConnectionString(ReadonlyRoleName, ReadonlyRolePassword));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "update spotify_playlist_cache set track_count = 99 where spotify_playlist_id = 'seed-for-update'";

        var exception = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        exception.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Readonly_role_select_succeeds()
    {
        await SeedOneRowAsReadWriteRoleAsync("seed-for-select");

        await using var connection = new NpgsqlConnection(BuildRoleConnectionString(ReadonlyRoleName, ReadonlyRolePassword));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from spotify_playlist_cache where spotify_playlist_id = 'seed-for-select'";

        var rowCount = (long)(await command.ExecuteScalarAsync())!;

        rowCount.ShouldBe(1);
    }

    [Fact]
    public async Task Readwrite_role_insert_succeeds()
    {
        await using var connection = new NpgsqlConnection(BuildRoleConnectionString(ReadwriteRoleName, ReadwriteRolePassword));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = InsertRowSql("readwrite-insert");

        var affectedRows = await command.ExecuteNonQueryAsync();

        affectedRows.ShouldBe(1);
    }

    private async Task SeedOneRowAsReadWriteRoleAsync(string playlistId)
    {
        await using var connection = new NpgsqlConnection(BuildRoleConnectionString(ReadwriteRoleName, ReadwriteRolePassword));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = InsertRowSql(playlistId);
        await command.ExecuteNonQueryAsync();
    }

    private string BuildRoleConnectionString(string roleName, string rolePassword)
    {
        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = roleName,
            Password = rolePassword,
        };
        return builder.ConnectionString;
    }

    private static string ReadRoleScript()
    {
        var assembly = typeof(TheBlueslandDbContext).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("create-spotify-cache-roles.sql", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string InsertRowSql(string playlistId) =>
        $"""
        insert into spotify_playlist_cache
            (spotify_playlist_id, name, track_count, artists, synced_at, is_available)
        values
            ('{playlistId}', 'test playlist', 1, array['test artist'], now(), true)
        """;
}
