using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using TheBluesland.Data;
using TheBluesland.Data.Entities;
using TheBluesland.SpotifyFetcher.CuratorNote;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// US-016 AC1/AC3: the AI client must never be called for a missing or unavailable row - the fake
/// client below throws if invoked, so any regression that lets a bad lookup fall through to a
/// real AI call fails these tests immediately. Uses a disposable Testcontainers Postgres instance,
/// same pattern as <see cref="PlaylistCacheSyncServiceTests"/>.
/// </summary>
public sealed class CuratorNoteSuggestionServiceTests : IAsyncLifetime
{
    private const string AvailablePlaylistId = "0iJt9LMebhOY0KSHSJw3cS";
    private const string UnavailablePlaylistId = "2m8X8fsMWor8A5AnmOHwzy";
    private const string MissingPlaylistId = "does-not-exist-in-cache";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task SuggestAsync_throws_and_never_calls_the_ai_client_when_no_row_exists()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var anthropicClient = new NeverCalledAnthropicClient();
        var service = new CuratorNoteSuggestionService(dbContext, anthropicClient);

        await Should.ThrowAsync<InvalidOperationException>(
            () => service.SuggestAsync(MissingPlaylistId, CancellationToken.None));
    }

    [Fact]
    public async Task SuggestAsync_throws_and_never_calls_the_ai_client_when_the_row_is_unavailable()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        dbContext.SpotifyPlaylistCache.Add(BuildEntry(UnavailablePlaylistId, isAvailable: false));
        await dbContext.SaveChangesAsync();

        var anthropicClient = new NeverCalledAnthropicClient();
        var service = new CuratorNoteSuggestionService(dbContext, anthropicClient);

        await Should.ThrowAsync<InvalidOperationException>(
            () => service.SuggestAsync(UnavailablePlaylistId, CancellationToken.None));
    }

    [Fact]
    public async Task SuggestAsync_returns_the_ai_client_s_suggestion_for_an_available_row()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        dbContext.SpotifyPlaylistCache.Add(BuildEntry(AvailablePlaylistId, isAvailable: true));
        await dbContext.SaveChangesAsync();

        var anthropicClient = new StubAnthropicClient("A drafted curator note.");
        var service = new CuratorNoteSuggestionService(dbContext, anthropicClient);

        var suggestion = await service.SuggestAsync(AvailablePlaylistId, CancellationToken.None);

        suggestion.ShouldBe("A drafted curator note.");
        anthropicClient.CallCount.ShouldBe(1);
        anthropicClient.LastPrompt.ShouldNotBeNull().ShouldContain("Masterpieces of Erkin the Father");
    }

    private static SpotifyPlaylistCacheEntry BuildEntry(string spotifyPlaylistId, bool isAvailable) => new()
    {
        SpotifyPlaylistId = spotifyPlaylistId,
        Name = "Masterpieces of Erkin the Father",
        Description = "Anadolu rock essentials.",
        CoverImageUrl = "https://i.scdn.co/image/cover.jpg",
        TrackCount = 44,
        Artists = ["Erkin Koray"],
        SpotifySnapshotId = "snapshot",
        SyncedAt = DateTimeOffset.UtcNow,
        IsAvailable = isAvailable,
    };

    private async Task<TheBlueslandDbContext> CreateMigratedDbContextAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TheBlueslandDbContext>()
            .UseNpgsql(_postgres.GetConnectionString());
        var dbContext = new TheBlueslandDbContext(optionsBuilder.Options);
        await dbContext.Database.MigrateAsync();
        return dbContext;
    }

    private sealed class NeverCalledAnthropicClient : IAnthropicClient
    {
        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The AI client must not be called for a missing/unavailable playlist.");
    }

    private sealed class StubAnthropicClient(string response) : IAnthropicClient
    {
        public int CallCount { get; private set; }
        public string? LastPrompt { get; private set; }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
        {
            CallCount++;
            LastPrompt = prompt;
            return Task.FromResult(response);
        }
    }
}
