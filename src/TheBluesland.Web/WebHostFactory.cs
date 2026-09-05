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

        // US-012 AC1/spec 13 SEC-002..SEC-005: applied to every response (not only a "production"
        // check) - simplest way to guarantee production always has them, and harmless in any other
        // environment. Set before UseAntiforgery/routing so no downstream branch (404, redirect,
        // health check, ...) can skip it. `frame-src` is the only Spotify-specific grant: the
        // click-to-load embed (PlaylistDetailView.EmbedUrl) is the sole reason this page ever loads
        // a cross-origin iframe. `img-src` additionally allows Spotify's cover-art CDNs, since
        // PlaylistDetailView/PlaylistCard render `CacheSnapshot.CoverImageUrl` directly (never
        // re-hosted, per FR-031) - no other Spotify origin is granted anywhere.
        //
        // Wildcarded to `*.scdn.co`/`*.spotifycdn.com` rather than pinned to `i.scdn.co` alone:
        // confirmed in production 2026-09-05 that Spotify now also serves cover art from
        // `image-cdn-ak.spotifycdn.com`/`image-cdn-fa.spotifycdn.com` (per-region CDN pool) and
        // `mosaic.scdn.co` (the 4-image collage Spotify generates for playlists with no custom
        // cover), silently CSP-blocked until this fix even though the cache lookup itself was
        // reachable - same "Spotify quietly renamed/added a hostname" pattern already hit twice
        // this project (the Feb-2026 API track-count field rename). Still scoped to Spotify's own
        // two root domains, not a broad host wildcard.
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.Append("X-Content-Type-Options", "nosniff");
            headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            headers.Append(
                "Permissions-Policy",
                "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()");
            headers.Append(
                "Content-Security-Policy",
                "default-src 'self'; " +
                "script-src 'self'; " +
                "style-src 'self'; " +
                "img-src 'self' https://*.scdn.co https://*.spotifycdn.com; " +
                "frame-src https://open.spotify.com; " +
                "frame-ancestors 'self'; " +
                "object-src 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self'");

            await next();
        });

        // US-013 AC1/spec 12.2: serves wwwroot/css/app.css (the compiled Tailwind stylesheet) and
        // wwwroot/images/ (the grain texture asset). Placed after the security-headers middleware
        // above so static responses carry them too, and before UseAntiforgery/routing since static
        // files need no antiforgery/component-endpoint handling.
        app.UseStaticFiles();

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

        // Deliberately DB-dependent (unlike /health/ready, FR-024/16.2) - a public, read-only probe
        // so cache connectivity can be checked without Render dashboard/log access, matching
        // PlaylistCacheLookup's own never-throws contract. Reports only row counts (already implied
        // by the public catalogue's size - no new information disclosure) and, on failure, the
        // exception type name only - never ex.Message or the connection string, since some Npgsql
        // failure modes echo connection details back in the message.
        app.MapGet("/health/cache", async (
            IDbContextFactory<TheBlueslandDbContext> dbContextFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var total = await dbContext.SpotifyPlaylistCache.CountAsync(cancellationToken);
                var available = await dbContext.SpotifyPlaylistCache.CountAsync(row => row.IsAvailable, cancellationToken);
                return Results.Json(new { reachable = true, totalRows = total, availableRows = available });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Json(new { reachable = false, errorType = ex.GetType().Name });
            }
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
