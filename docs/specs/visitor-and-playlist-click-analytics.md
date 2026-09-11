# Spec: Visitor count and playlist-click analytics

Mehmet wants to measure how many people visit the site and which playlists they click into/through
to Spotify. Discussed two approaches; Mehmet picked **self-hosted, server-side logging into the
existing Neon Postgres** - zero third-party analytics script, no CSP change, no cookies/consent
banner. This is genuinely new ground for this app: it's the **first time the production web
environment holds any write-capable database credential** (ADR-0002 madde 4 says production only
ever held a read-only connection - the sync tool was the only writer). That boundary isn't being
removed, it's being narrowed differently - see Design section 1 for how this stays least-privilege.

## Objective

1. Every page view on this site's real content routes (`/`, `/playlists/{slug}`, `/collections`,
   `/collections/{slug}`, `/about`, `/privacy`, `/terms`) is recorded: when, which path, and (for
   playlist pages) which playlist.
2. Every click on a playlist's "Open in Spotify" link is recorded as a distinct event, before the
   visitor is redirected to Spotify.
3. Approximate daily unique visitor counts are derivable from the recorded data without storing any
   raw IP address or setting any cookie.
4. Mehmet can query this data himself in the Neon SQL editor - no new UI page, no dashboard, no new
   report workflow in this iteration (avoid building a whole reporting surface nobody asked for
   yet; revisit if he wants one after seeing the raw data for a while).

## Design

### 1. A second, narrowly-scoped `AnalyticsDbContext` - not a new DbSet on `TheBlueslandDbContext`

`TheBlueslandDbContext` is documented (its own class summary) as "the shared model for
`spotify_playlist_cache`" and the production web app's connection to it is read-only by design
(ADR-0002). Bolting a `page_view_events` DbSet onto that same context would force a single
connection string to cover both concerns, meaning any bug in the analytics write path shares the
same DB role as the cache read path - the opposite of the least-privilege separation this project
already applies to `spotify_playlist_cache` itself (`spotify_cache_readonly` vs
`spotify_cache_readwrite`, `src/TheBluesland.Data/Scripts/create-spotify-cache-roles.sql`).

Instead, add a new, minimal `AnalyticsDbContext` (same `TheBluesland.Data` project - this is a new
class, not a new project, so it doesn't touch ADR-0003's "no new project without a real boundary"):

```csharp
public sealed class AnalyticsDbContext : DbContext
{
    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : base(options) { }
    public DbSet<PageViewEvent> PageViewEvents => Set<PageViewEvent>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new PageViewEventConfiguration());
}
```

`PageViewEvent` entity (new file, same pattern as `SpotifyPlaylistCacheEntry`):

```csharp
public sealed class PageViewEvent
{
    public long Id { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string EventType { get; init; }       // "page_view" | "spotify_click"
    public required string Path { get; init; }             // e.g. "/", "/playlists/my-slug", "/out/my-slug"
    public string? PlaylistSlug { get; init; }              // set for playlist-detail views and spotify_click; null otherwise
    public required string VisitorHash { get; init; }       // see section 3 - never a raw IP
}
```

EF Core migration for this context is generated the same way as always (`dotnet ef migrations add
... --context AnalyticsDbContext --project src/TheBluesland.Data`) but, per this project's standing
rule, is **never applied to Neon by an agent** - Mehmet runs `dotnet ef database update` himself,
exactly like `AddFollowerCount` before it.

### 2. New Postgres role, new connection string - mirrors `create-spotify-cache-roles.sql` exactly

Add `src/TheBluesland.Data/Scripts/create-analytics-role.sql`, same shape/header-comment style as
the existing role script:

```sql
CREATE ROLE analytics_writer LOGIN PASSWORD 'placeholder-analytics-password';
GRANT INSERT ON page_view_events TO analytics_writer;
GRANT USAGE, SELECT ON SEQUENCE page_view_events_id_seq TO analytics_writer;
```

Note: **no SELECT grant** - the web app writes events but never needs to read them back (Mehmet
reads via his own Neon SQL editor session, which uses his own admin credentials, not this role).
This role cannot touch `spotify_playlist_cache` at all - completely disjoint from the existing
`spotify_cache_readonly`/`spotify_cache_readwrite` roles.

New connection string, `Analytics` (mirrors the existing `SpotifyPlaylistCache` connection-string
name in `WebHostFactory.ConnectionStringName`), registered as its own
`AddDbContextFactory<AnalyticsDbContext>(...)` in `WebHostFactory.cs`, right next to the existing
`TheBlueslandDbContext` registration. Render environment variable:
`ConnectionStrings__Analytics`, holding the `analytics_writer` role's connection string - **this is
the only other database credential the production web app will ever hold**, and it cannot read or
write anything outside `page_view_events`.

### 3. Privacy-preserving visitor hash - no raw IP stored, no cookie set

`VisitorHash = SHA256(pepper + utcDate + remoteIp + userAgent)`, hex-encoded. `pepper` is a new
required app-config value (`Analytics__VisitorHashPepper`, a random string - generate one, store it
as a Render env var, never commit it) so the hash can't be brute-forced back to an IP by anyone
without that pepper. `utcDate` (just the date, e.g. `"2026-09-11"`, not the time) means the same
visitor gets a *different* hash every day - good enough to compute "distinct visitors per day" via
`COUNT(DISTINCT visitor_hash)` grouped by date, without ever being able to correlate one visitor
across two different days from the stored data alone. No cookie is set, no client-side storage used
- this is entirely a server-side computation per request.

### 4. Recording page views - middleware in `WebHostFactory.cs`

Add one more `app.Use(...)` middleware, alongside the existing security-headers/legacy-host-redirect
ones. Runs `await next()` first (so the real response status is known - a 404 for an unknown
`/playlists/{slug}` must **not** be logged as a view), then, only when
`context.Response.StatusCode == 200` and the request path matches one of the known content routes
below, fires off a background write:

- Exact match: `/`, `/about`, `/collections`, `/privacy`, `/terms` -> `PlaylistSlug = null`.
- Prefix match: `/playlists/{slug}` -> `PlaylistSlug = slug`. `/collections/{slug}` ->
  `PlaylistSlug = null` (it's a collection key, not a playlist - don't conflate the two in one
  column).
- Everything else (static assets, `/health/*`, `/sitemap.xml`, `/robots.txt`, `/llms.txt`,
  `*/og-image.png`, `/out/{slug}` - that one is logged separately, see section 5) is not a
  `page_view` and must not be logged as one.

**The write must be genuinely fire-and-forget and must never throw into the response pipeline**:
capture only plain values (path, slug, hashed visitor, timestamp) before starting the background
work; do not capture `HttpContext` itself into the detached task (its scope/services may already be
disposed by the time the task runs). Use `IDbContextFactory<AnalyticsDbContext>.CreateDbContextAsync`
inside the detached task (a fresh context, not the request's scoped one) with `CancellationToken.None`
(a request ending must not cancel a write already in flight), wrapped in try/catch that swallows and
logs (never rethrows) - an unreachable analytics DB must never affect page serving, same
graceful-degradation principle this app already applies to the cache read path (spec 16.2), just
mirrored for a write instead of a read.

### 5. Recording Spotify clicks - new `/out/{slug}` redirect endpoint

Add to `WebHostFactory.cs`, alongside the other `app.MapGet(...)` endpoints:

```csharp
app.MapGet("/out/{slug}", async (string slug, PlaylistContentRepository repository, ...) =>
{
    var content = await repository.FindBySlugAsync(slug, cancellationToken);
    if (content is null) { return Results.NotFound(); }

    // record spotify_click event (path "/out/{slug}", PlaylistSlug = slug) - same fire-and-forget
    // pattern as section 4, not inline-awaited.

    return Results.Redirect($"https://open.spotify.com/playlist/{content.SpotifyPlaylistId}");
});
```

Looking the slug up through `PlaylistContentRepository` (never accepting a raw target URL from the
caller) is required, not optional - redirecting to a caller-supplied URL would be an open-redirect
vulnerability. A slug that doesn't resolve returns 404 and redirects nowhere.

In `PlaylistDetailView.razor`, change the existing `OpenInSpotifyUrl` from
`https://open.spotify.com/playlist/{Content.SpotifyPlaylistId}` to `/out/{Content.Slug}` - keep the
existing `target="_blank" rel="noopener noreferrer"` attributes as they are, only the `href` changes.

## Tech Stack / Commands

No new dependency (uses the EF Core/Npgsql packages already referenced by `TheBluesland.Data`).
`dotnet build TheBluesland.slnx`, `dotnet test TheBluesland.slnx` as usual. New migration via
`dotnet ef migrations add <Name> --context AnalyticsDbContext --project src/TheBluesland.Data
--startup-project src/TheBluesland.Web` (mirror whatever exact invocation the existing
`AddFollowerCount` migration used for `TheBlueslandDbContext`, just with `--context` added).

## Testing Strategy

- `PageViewEventConfiguration`/migration: a schema test mirroring
  `SpotifyPlaylistCacheEntrySchemaTests.cs`'s pattern.
- New `create-analytics-role.sql` gets a Testcontainers-backed role-privilege test mirroring
  `SpotifyPlaylistCacheRoleTests.cs` exactly: `analytics_writer` can `INSERT` into
  `page_view_events`, cannot `SELECT` from it, and cannot touch `spotify_playlist_cache` at all
  (neither read nor write).
- Integration test (mirroring `WebHostIntegrationTests.cs`'s existing patterns): a request to `/`
  and to a known-published `/playlists/{slug}` each produce exactly one new `page_view_events` row
  with the expected `Path`/`PlaylistSlug`; a request to an unknown `/playlists/{slug}` (404)
  produces none; a request to `/health/live` produces none.
- Integration test for `/out/{slug}`: a known slug returns a 302 to the correct
  `open.spotify.com/playlist/{id}` URL and records exactly one `spotify_click` row; an unknown slug
  returns 404 and records nothing.
- Unit test for the visitor-hash function: same (pepper, date, ip, userAgent) always produces the
  same hash; changing any one input changes the hash; the same ip+userAgent on two different dates
  produces two different hashes (this is the actual privacy property being relied on - test it
  directly, don't just assert "it's some string").
- Test that an unreachable `Analytics` connection string does not throw an exception out of a
  request (mirrors `PlaylistCacheLookup`'s existing DB-unreachable graceful-degradation tests) -
  the page must still render/redirect normally even if the write silently fails.

## Boundaries

- **Always:** keep this credential and this DbContext completely separate from
  `spotify_playlist_cache`'s read path - never let the two share a role or a connection string.
  Never let an analytics write failure affect the response already being served.
- **Ask first:** anything that would require reading raw IP addresses back out, setting a cookie, or
  adding any client-side script for this - all explicitly out of scope for this iteration; if the
  design above turns out to be insufficient, stop and ask rather than reaching for those.
- **Never:** accept a caller-supplied redirect target for `/out/{slug}` (open-redirect risk - always
  resolve through `PlaylistContentRepository` by slug); log a `page_view` for a non-200 response;
  build a report page/dashboard/new GitHub Actions report workflow in this iteration (Mehmet queries
  Neon directly for now - see Success Criteria).

**Documentation debt this creates:** once implemented and verified, `docs/adr/0002-spotify-veri-mimarisi.md`
needs a short dated note that the production web app now also holds one additional, narrowly-scoped
write credential (`analytics_writer`, insert-only on one unrelated table) - main-session work, not
backend-dev's, same pattern as the auto-unpublish workflow's ADR note.

## Success Criteria

- [ ] `dotnet build`/`dotnet test` green, including every test in Testing Strategy above.
- [ ] A real page view against each of the seven listed routes produces exactly one correctly-shaped
      row; a 404 produces none.
- [ ] Clicking "Open in Spotify" on a real playlist page redirects to the correct Spotify URL and
      records exactly one `spotify_click` row.
- [ ] `analytics_writer` role can only INSERT into `page_view_events` and nothing else - verified by
      an automated test, not just the SQL script's comments.
- [ ] An unreachable `Analytics` connection string never breaks page rendering or the `/out/{slug}`
      redirect (verified by a test, not just code review).
- [ ] Mehmet has two or three example SQL queries handed to him (in the PR description or a short
      comment in the migration/role script) for: daily unique visitors
      (`COUNT(DISTINCT visitor_hash)` grouped by date), and top playlists by view/click count - so
      he can actually use this the day it ships, without waiting on a future reporting feature.

## Open Questions

None blocking. If Mehmet later wants a real dashboard/report instead of querying Neon by hand,
that's a follow-up spec, not part of this one (Boundaries above deliberately excludes it now).
