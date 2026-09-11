using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TheBluesland.Data;
using TheBluesland.Web.Analytics;
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

    // docs/specs/visitor-and-playlist-click-analytics.md, Design section 2: a second, disjoint
    // connection string/role (analytics_writer) - never shares a role or connection with the
    // spotify_playlist_cache read path above.
    public const string AnalyticsConnectionStringName = "Analytics";
    private const string VisitorHashPepperConfigKey = "Analytics:VisitorHashPepper";
    private const long SocialCardCacheSizeBytes = 16 * 1024 * 1024;
    private const long SocialCardMaximumBodySizeBytes = 1024 * 1024;
    private static readonly TimeSpan SocialCardCacheDuration = TimeSpan.FromHours(24);

    // webRootPath: WebApplicationBuilder.WebHost.UseWebRoot(...) throws at Build() time ("web root
    // changed... not supported") - the minimal-hosting API only accepts a web root via
    // WebApplicationOptions at CreateBuilder time. A test project referencing this one doesn't get
    // TheBluesland.Web's own wwwroot copied into its own output directory (unlike this project's
    // own bin folder when it runs for real), so a test that needs UseStaticFiles() to actually
    // serve something passes the real path here instead. Null (every other caller) preserves the
    // exact default resolution WebApplication.CreateBuilder(args) already used.
    public static WebApplication Create(
        string[] args,
        Action<WebApplicationBuilder>? configureForTests = null,
        string? webRootPath = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = webRootPath });
        configureForTests?.Invoke(builder);

        var publicOrigin = builder.Configuration[SiteUrl.PublicOriginConfigKey] ?? SiteUrl.DefaultPublicOrigin;
        if (!Uri.TryCreate(publicOrigin, UriKind.Absolute, out var publicUri) ||
            publicUri.Scheme != Uri.UriSchemeHttps || publicUri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(publicUri.Query) || !string.IsNullOrEmpty(publicUri.Fragment) ||
            !string.IsNullOrEmpty(publicUri.UserInfo))
        {
            throw new InvalidOperationException("Site:PublicOrigin must be an HTTPS origin without a path, query or credentials.");
        }

        // Gates /health/cache below. Read here (rather than via injected IConfiguration in the
        // handler) so a missing value fails the same way a wrong one does - no special-cased "not
        // configured" branch to forget.
        var cacheHealthKey = builder.Configuration["Diagnostics:CacheHealthKey"];

        // docs/specs/visitor-and-playlist-click-analytics.md, Design section 3: a random secret
        // (Render env var `Analytics__VisitorHashPepper`, never committed) so VisitorHash cannot be
        // brute-forced back to an IP by anyone without it. The local-dev fallback below is only
        // ever used outside Render and never protects anything real.
        var visitorHashPepper = builder.Configuration[VisitorHashPepperConfigKey] ?? "local-dev-pepper-not-for-production";

        // Local hostnames permit local development; public traffic must use the configured host or
        // one of the legacy hosts that get redirected to it below (2026-09-09 domain migration -
        // SPEC-custom-domain-migration.md). Without these here, HostFilteringMiddleware would
        // reject them with 400 before the redirect below ever ran.
        var allowedHosts = new[] { publicUri.Host, "localhost", "127.0.0.1", "[::1]" }
            .Concat(SiteUrl.LegacyRedirectHosts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        builder.Configuration["AllowedHosts"] = string.Join(';', allowedHosts);
        builder.Services.AddHostFiltering(options => options.AllowedHosts = allowedHosts);
        builder.Services.AddOutputCache(options =>
        {
            options.SizeLimit = SocialCardCacheSizeBytes;
            options.MaximumBodySize = SocialCardMaximumBodySizeBytes;
        });
        builder.Services.AddRazorComponents();

        builder.Services.AddDbContextFactory<TheBlueslandDbContext>(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString(ConnectionStringName)
                ?? "Host=localhost;Database=thebluesland;Username=postgres;Password=postgres";
            options.UseNpgsql(connectionString);
        });

        // docs/specs/visitor-and-playlist-click-analytics.md, Design section 2: its own
        // IDbContextFactory, its own connection string - never shared with TheBlueslandDbContext
        // above, so a bug in this write path cannot touch spotify_playlist_cache.
        builder.Services.AddDbContextFactory<AnalyticsDbContext>(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString(AnalyticsConnectionStringName)
                ?? "Host=localhost;Database=thebluesland;Username=postgres;Password=postgres";
            options.UseNpgsql(connectionString);
        });
        // Holds only the (effectively singleton-lifetime) db context factory and logger - never any
        // request-scoped state - so it is safe to use from a detached fire-and-forget task that may
        // still be running after the request that started it has ended.
        builder.Services.AddSingleton<PageViewRecorder>();

        builder.Services.AddSingleton<StaticAssetVersion>();
        builder.Services.AddSingleton<PlaylistContentReader>();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<PlaylistCacheSignalsCache>();
        builder.Services.AddSingleton<PlaylistContentRepository>();
        builder.Services.AddScoped<PlaylistCacheLookup>();
        // US-011 AC3: stateless (each call builds its own Image<T> locally), safe as a singleton.
        builder.Services.AddSingleton<SocialCardGenerator>();

        builder.Services
            .AddHealthChecks()
            .AddCheck<PlaylistContentHealthCheck>("playlist-content", tags: ["ready"]);

        var app = builder.Build();
        app.UseHostFiltering();

        // 2026-09-09 domain migration (SPEC-custom-domain-migration.md): a request that reaches
        // this host filter under a legacy hostname - already indexed, already shared, already
        // referenced from a past llms.txt/JSON-LD generation - is permanently redirected to the
        // canonical origin rather than served, path and query string intact. The `publicUri.Host`
        // check guards the default/local/test configuration, where the "canonical" host and
        // SiteUrl.DefaultPublicOrigin's host are the same onrender.com value: without it, that
        // configuration would redirect to itself forever instead of serving normally.
        app.Use(async (context, next) =>
        {
            var requestHost = context.Request.Host.Host;
            if (!string.Equals(requestHost, publicUri.Host, StringComparison.OrdinalIgnoreCase)
                && SiteUrl.LegacyRedirectHosts.Contains(requestHost, StringComparer.OrdinalIgnoreCase))
            {
                var target = publicOrigin.TrimEnd('/') + context.Request.Path + context.Request.QueryString;
                context.Response.Redirect(target, permanent: true);
                return;
            }

            await next();
        });

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

        // docs/specs/visitor-and-playlist-click-analytics.md, Design section 4: runs next() first
        // so the real response status is known - a 404 for an unknown /playlists/{slug} must never
        // be logged as a view - then, only for a 200 on one of PageViewRoute's known content
        // routes, fires off a fire-and-forget write. /out/{slug} is deliberately excluded here
        // (PageViewRoute never matches it): that endpoint logs its own spotify_click event instead
        // (Design section 5).
        app.Use(async (context, next) =>
        {
            await next();

            if (context.Response.StatusCode != StatusCodes.Status200OK)
            {
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;
            if (!PageViewRoute.TryClassify(path, out var playlistSlug))
            {
                return;
            }

            var visitorHash = VisitorHashing.Compute(
                visitorHashPepper,
                DateOnly.FromDateTime(DateTime.UtcNow),
                context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                context.Request.Headers.UserAgent.ToString());

            context.RequestServices.GetRequiredService<PageViewRecorder>()
                .RecordFireAndForget("page_view", path, playlistSlug, visitorHash, DateTimeOffset.UtcNow);
        });

        // US-013 AC1/spec 12.2: serves wwwroot/css/app.css (the compiled Tailwind stylesheet) and
        // wwwroot/images/ (the grain texture asset). Placed after the security-headers middleware
        // above so static responses carry them too, and before UseAntiforgery/routing since static
        // files need no antiforgery/component-endpoint handling.
        //
        // A year-long immutable Cache-Control is safe specifically because every reference to
        // css/app.css and the two js/ files already goes through StaticAssetVersion's content-hash
        // `?v=` query string (App.razor) - any real content change produces a new URL, so a stale
        // cached response under the old URL is simply never requested again after a deploy. Without
        // this, browsers fall back to their own (often much shorter) heuristics despite the
        // versioning already making long-lived caching safe.
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
                context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable",
        });

        app.UseOutputCache();
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

        // Deliberately DB-dependent (unlike /health/ready, FR-024/16.2) - a read-only probe so
        // cache connectivity can be checked without Render dashboard/log access, matching
        // PlaylistCacheLookup's own never-throws contract. A 2026-09-06 security review (PR #29)
        // removed the original, fully public version of this endpoint as an information-disclosure
        // finding; re-added here gated on a shared-secret `key` query parameter (constant-time
        // compared against Diagnostics__CacheHealthKey, a Render-only value, never committed).
        // Query string rather than a header: the only real consumer - fetching this without Render
        // dashboard access - can only issue a plain unauthenticated GET with no custom headers. A
        // missing, wrong, or unconfigured key returns 404 rather than 401/403, so an anonymous
        // prober gets no signal this route exists at all, same as before the security review.
        // Reports only row counts (already implied by the public catalogue's size to anyone who
        // does hold the key) and, on failure, the exception type name only - never ex.Message or
        // the connection string, since some Npgsql failure modes echo connection details back in
        // the message.
        app.MapGet("/health/cache", async (
            HttpContext context,
            IDbContextFactory<TheBlueslandDbContext> dbContextFactory,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorizedCacheHealthRequest(context, cacheHealthKey))
            {
                return Results.NotFound();
            }

            // Optional `id`: mirrors PlaylistCacheLookup.GetSnapshotAsync's exact unprojected
            // SingleOrDefaultAsync for one row, to see why a specific playlist degrades when the
            // aggregate counts below look fine (2026-09-07: this caught a real per-row failure that
            // COUNT(*) couldn't).
            var diagnosticId = context.Request.Query["id"].ToString();
            if (!string.IsNullOrEmpty(diagnosticId))
            {
                try
                {
                    await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                    var entry = await dbContext.SpotifyPlaylistCache
                        .AsNoTracking()
                        .SingleOrDefaultAsync(row => row.SpotifyPlaylistId == diagnosticId, cancellationToken);
                    return Results.Json(new
                    {
                        reachable = true,
                        found = entry is not null,
                        isAvailable = entry?.IsAvailable,
                        coverImageUrl = entry?.CoverImageUrl,
                        trackCount = entry?.TrackCount,
                        computedEras = entry?.ComputedEras,
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Same rule as the aggregate branch below: never echo a connection-level
                    // Npgsql failure's message (some modes include host/credential fragments). EF
                    // Core wraps a connection-level failure in its own InvalidOperationException
                    // ("...likely due to a transient failure...") rather than surfacing the
                    // NpgsqlException directly - confirmed via this exact code path's own test
                    // fixture - so the whole InnerException chain must be checked, not just ex's
                    // own type.
                    var errorMessage = ContainsConnectionFailure(ex) ? null : ex.Message;
                    return Results.Json(new { reachable = false, errorType = ex.GetType().Name, errorMessage });
                }
            }

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

        // Only published content enters the sitemap. SiteUrl uses the configured public origin,
        // independent of the incoming Host and forwarded headers.
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
            return Results.Text(AiDiscoveryGenerator.BuildRobots(context), "text/plain");
        });

        app.MapGet("/llms.txt", async (
            HttpContext context,
            PlaylistContentRepository repository,
            CancellationToken cancellationToken) =>
        {
            var published = await repository.FindAllPublishedAsync(cancellationToken);
            return Results.Text(AiDiscoveryGenerator.BuildLlms(context, published), "text/plain");
        });

        // US-011 AC3/FR-031: site-wide default social card for pages with no dedicated playlist
        // (home/about/privacy/terms) - a real generated image, never Spotify cover art.
        app.MapGet("/og-image.png", (SocialCardGenerator generator) => Results.File(
            generator.Generate("TheBluesland", "Rooted in blues. Open to wherever the music leads."),
            "image/png"))
            .CacheOutput(policy => policy.Expire(SocialCardCacheDuration).SetVaryByQuery([]));

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
        }).CacheOutput(policy => policy.Expire(SocialCardCacheDuration).SetVaryByQuery([]));

        // docs/specs/visitor-and-playlist-click-analytics.md, Design section 5: resolves the slug
        // through PlaylistContentRepository - never a caller-supplied redirect target - so an
        // unknown slug 404s and redirects nowhere. This is a deliberate open-redirect prevention,
        // not incidental: never change this to accept/echo a raw target URL from the request.
        app.MapGet("/out/{slug}", async (
            string slug,
            HttpContext context,
            PlaylistContentRepository repository,
            PageViewRecorder recorder,
            CancellationToken cancellationToken) =>
        {
            var content = await repository.FindBySlugAsync(slug, cancellationToken);
            if (content is null)
            {
                return Results.NotFound();
            }

            var visitorHash = VisitorHashing.Compute(
                visitorHashPepper,
                DateOnly.FromDateTime(DateTime.UtcNow),
                context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                context.Request.Headers.UserAgent.ToString());
            recorder.RecordFireAndForget("spotify_click", $"/out/{slug}", slug, visitorHash, DateTimeOffset.UtcNow);

            return Results.Redirect($"https://open.spotify.com/playlist/{content.SpotifyPlaylistId}");
        });

        app.MapRazorComponents<App>();

        return app;
    }

    // Length-checked before FixedTimeEquals (which requires equal-length spans) so an unconfigured
    // or wrong-length key still compares in a way that reveals nothing timing-wise beyond "no".
    private static bool IsAuthorizedCacheHealthRequest(HttpContext context, string? configuredKey)
    {
        if (string.IsNullOrEmpty(configuredKey))
        {
            return false;
        }

        var providedKey = context.Request.Query["key"].ToString();
        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        var providedBytes = Encoding.UTF8.GetBytes(providedKey);

        return configuredBytes.Length == providedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(configuredBytes, providedBytes);
    }

    private static bool ContainsConnectionFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException)
            {
                return true;
            }
        }

        return false;
    }
}
