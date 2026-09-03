using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using TheBluesland.Data;
using TheBluesland.Web.Cache;
using TheBluesland.Web.Components;
using TheBluesland.Web.Content;
using TheBluesland.Web.HealthChecks;

namespace TheBluesland.Web;

/// <summary>
/// Builds the ASP.NET Core host. Extracted out of <c>Program.cs</c> so integration tests can start
/// the real pipeline (Kestrel, health checks, Razor component endpoints) in-process on an ephemeral
/// port with overridden configuration - e.g. an unreachable database connection string for
/// US-005's DB-unreachable scenarios - without adding a WebApplicationFactory/TestHost package.
/// </summary>
public static class WebHostFactory
{
    public const string ConnectionStringName = "SpotifyPlaylistCache";

    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configureForTests = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configureForTests?.Invoke(builder);

        builder.Services.AddRazorComponents();

        builder.Services.AddDbContextFactory<TheBlueslandDbContext>(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString(ConnectionStringName)
                ?? "Host=localhost;Database=thebluesland;Username=postgres;Password=postgres";
            options.UseNpgsql(connectionString);
        });

        builder.Services.AddSingleton<PlaylistContentReader>();
        builder.Services.AddSingleton<PlaylistContentRepository>();
        builder.Services.AddScoped<PlaylistCacheLookup>();

        builder.Services
            .AddHealthChecks()
            .AddCheck<PlaylistContentHealthCheck>("playlist-content", tags: ["ready"]);

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseAntiforgery();

        // FR-024 / spec 16.2: readiness depends only on editorial content, never on DB reachability.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
        });

        // Process liveness only; never touches content or the database.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
        });

        app.MapRazorComponents<App>();

        return app;
    }
}
