using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using TheBluesland.Data;
using TheBluesland.Web.Cache;
using TheBluesland.Web.Components;
using TheBluesland.Web.Content;
using TheBluesland.Web.HealthChecks;
using TheBluesland.Web.Seo;

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
        // US-011 AC3: stateless (each call builds its own Image<T> locally), safe as a singleton.
        builder.Services.AddSingleton<SocialCardGenerator>();

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

        // US-011 AC2/spec 14: server-generated from published content only, never a static file, so
        // it always reflects the current content and always uses this request's own scheme/host
        // (see Seo/SiteUrl's doc comment for why that beats a configured base-URL setting here).
        app.MapGet("/sitemap.xml", async (
            HttpContext context,
            PlaylistContentRepository repository,
            CancellationToken cancellationToken) =>
        {
            var published = await repository.FindAllPublishedAsync(cancellationToken);
            var xml = SitemapGenerator.Generate(context, published);
            return Results.Text(xml, "application/xml");
        });

        // Spec section 5's route table; not part of US-011's AC bullets but nearly free once
        // /sitemap.xml exists.
        app.MapGet("/robots.txt", (HttpContext context) =>
        {
            var sitemapUrl = SiteUrl.BuildAbsolute(context, "/sitemap.xml");
            return Results.Text($"User-agent: *\nAllow: /\nSitemap: {sitemapUrl}\n", "text/plain");
        });

        // US-011 AC3/FR-031: site-wide default social card for pages with no dedicated playlist
        // (home/about/privacy/terms) - a real generated image, never Spotify cover art.
        app.MapGet("/og-image.png", (SocialCardGenerator generator) => Results.File(
            generator.Generate("TheBluesland", "Curated Spotify playlists, one curator note at a time."),
            "image/png"));

        // US-011 AC3/FR-031: per-playlist card, built from that playlist's own editorial title and
        // summary - never from the cache's Spotify-hosted cover_image_url.
        app.MapGet("/playlists/{slug}/og-image.png", async (
            string slug,
            PlaylistContentRepository repository,
            SocialCardGenerator generator,
            CancellationToken cancellationToken) =>
        {
            var content = await repository.FindBySlugAsync(slug, cancellationToken);
            return content is null
                ? Results.NotFound()
                : Results.File(generator.Generate(content.Title, content.Summary), "image/png");
        });

        app.MapRazorComponents<App>();

        return app;
    }
}
