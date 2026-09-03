using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using Xunit;

namespace TheBluesland.UnitTests;

/// <summary>
/// US-001, acceptance criterion 2: the InitialCreate migration must apply cleanly to a real
/// Postgres instance and produce the exact <c>spotify_playlist_cache</c> columns from spec
/// section 9.4. This spins up a disposable Postgres container (Testcontainers), consistent with
/// spec section 17.2/17.4 (Testcontainers-backed cache tests, never a real Neon database).
/// </summary>
public sealed class SpotifyPlaylistCacheMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_creates_spotify_playlist_cache_table_with_expected_columns()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TheBlueslandDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());

        await using (var context = new TheBlueslandDbContext(optionsBuilder.Options))
        {
            await context.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select column_name, data_type, is_nullable
            from information_schema.columns
            where table_name = 'spotify_playlist_cache'
            order by column_name
            """;

        var actualColumns = new Dictionary<string, (string DataType, bool IsNullable)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                actualColumns[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2) == "YES");
            }
        }

        actualColumns.Keys.ShouldBe(
            [
                "artists",
                "cover_image_url",
                "description",
                "is_available",
                "name",
                "spotify_playlist_id",
                "spotify_snapshot_id",
                "synced_at",
                "track_count",
            ],
            ignoreOrder: true);

        actualColumns["spotify_playlist_id"].IsNullable.ShouldBeFalse();
        actualColumns["name"].IsNullable.ShouldBeFalse();
        actualColumns["description"].IsNullable.ShouldBeTrue();
        actualColumns["cover_image_url"].IsNullable.ShouldBeTrue();
        actualColumns["track_count"].DataType.ShouldBe("integer");
        actualColumns["track_count"].IsNullable.ShouldBeFalse();
        actualColumns["artists"].DataType.ShouldBe("ARRAY");
        actualColumns["artists"].IsNullable.ShouldBeFalse();
        actualColumns["spotify_snapshot_id"].IsNullable.ShouldBeTrue();
        actualColumns["synced_at"].DataType.ShouldBe("timestamp with time zone");
        actualColumns["synced_at"].IsNullable.ShouldBeFalse();
        actualColumns["is_available"].DataType.ShouldBe("boolean");
        actualColumns["is_available"].IsNullable.ShouldBeFalse();
    }
}
