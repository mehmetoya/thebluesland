# Spec: Analytics dashboard

Mehmet wants to actually look at the data `page_view_events` has been collecting
(`docs/specs/visitor-and-playlist-click-analytics.md`) without hand-writing SQL every time. He
explicitly wants a dashboard now, not "later" - this supersedes that earlier spec's "no dashboard
in this iteration" boundary.

## Objective

A single page Mehmet can visit that shows: daily unique visitors (last 30 days), top playlists by
page view, and top playlists by Spotify click-through - the same three queries already handed to
him as raw SQL, now rendered as tables on one page instead of copy-pasted into the Neon SQL editor.

## Design

### 1. Widen `analytics_writer`'s grant (already done by hand on the real Neon DB 2026-09-11;
   this section documents it, doesn't re-run it)

`GRANT SELECT ON page_view_events TO analytics_writer;` - the existing write role also reads now.
No new role, no new connection string, no new Render env var: reusing the existing
`AnalyticsDbContext`/`ConnectionStrings__Analytics` avoids a third database credential for what
is, in practice, the same trust boundary (this app's own backend, not an untrusted caller) reading
data it itself wrote. This is a deliberate simplicity trade-off against the stricter "insert-only,
can't read a thing back" design from the original spec - acceptable because this is a single-owner
personal project and the data itself carries no PII (hashed visitor id, path, timestamp only).

### 2. Gated route: `GET /dashboard`, `WebHostFactory.cs`

Mirrors `/health/cache`'s existing shared-secret gate exactly (`IsAuthorizedCacheHealthRequest`
pattern) - same constant-time comparison, same "wrong/missing key returns 404, not 401/403" so an
anonymous prober gets no signal the route exists. New config key
`Diagnostics:AnalyticsDashboardKey` (Render env var `Diagnostics__AnalyticsDashboardKey`, already
set). Extract the shared-secret check into a small reusable helper (`IsAuthorizedByKey(HttpContext,
string? configuredKey)` or similar) rather than duplicating `IsAuthorizedCacheHealthRequest`
verbatim a second time - two call sites is exactly the point where "copy it" becomes "extract it."

On success, query `AnalyticsDbContext` (now read-capable per section 1) via three LINQ aggregates
matching the SQL already given to Mehmet:

```csharp
var dailyUniques = await dbContext.PageViewEvents
    .Where(e => e.EventType == "page_view" && e.OccurredAt >= cutoff)
    .GroupBy(e => e.OccurredAt.Date)
    .Select(g => new { Day = g.Key, Unique = g.Select(e => e.VisitorHash).Distinct().Count() })
    .OrderByDescending(g => g.Day)
    .ToListAsync(cancellationToken);
```

(mirror this shape for top-playlists-by-view and top-playlists-by-click, grouping by
`PlaylistSlug` where it's not null, ordered descending by count, capped at a reasonable limit -
20 rows each). `cutoff` = 30 days before `DateTimeOffset.UtcNow` for the daily-uniques query only;
the two playlist rankings are all-time (no date filter) since this is meant to answer "what's
popular", not "what's popular this month" - keep it simple, no date-range picker in this
iteration.

### 3. Rendering: plain HTML, not a Razor/Blazor component

This is a single-owner diagnostic view, not a public page - no design-token treatment, no dark/light
theme, matching this project's existing `/health/cache` precedent of a bare `Results.Json(...)`.
Here the shape calls for a readable table instead of raw JSON, so return
`Results.Content(html, "text/html")` with hand-built, minimal semantic HTML (`<table>`, `<th>`,
`<td>`) - no `<style>` block (this app's CSP has no `'unsafe-inline'` for `style-src` either,
exactly like `script-src` - see `docs/specs/dark-light-mode-toggle.md`'s section 2 for why that
constraint exists). Unstyled tables are perfectly fine for this audience; don't add a stylesheet
link or any styling machinery for a page only Mehmet will ever open.

A small new class, e.g. `AnalyticsDashboardHtmlBuilder` (same "one small dedicated builder" pattern
as `SocialCardGenerator`/`SitemapGenerator`/`AiDiscoveryGenerator`), takes the three result sets and
returns the HTML string - keeps `WebHostFactory.cs`'s endpoint body short, matching how the other
`app.MapGet` handlers in that file delegate to a dedicated class rather than building output inline.

## Tech Stack / Commands

No new dependency. `dotnet build TheBluesland.slnx`, `dotnet test TheBluesland.slnx` as usual.

## Testing Strategy

- Integration test (mirroring `WebHostIntegrationTests.cs`'s `/health/cache` key-gating tests):
  missing/wrong `key` returns 404; correct key returns 200 with `text/html`.
- Integration or unit test for `AnalyticsDashboardHtmlBuilder`: given known fixture rows, the
  rendered HTML contains the expected slugs/counts (a simple string-contains assertion is enough -
  this isn't a page that needs golden-file/snapshot testing).
- Seed a few `PageViewEvent` rows (Testcontainers, same pattern as `AnalyticsRoleTests.cs`/existing
  analytics integration tests) and assert the daily-unique-visitor and top-playlist aggregates come
  back correct for a known fixture (distinct visitor_hash counts per day; correct ordering by
  count).

## Boundaries

- **Always:** use the exact same 404-on-bad-key pattern as `/health/cache` - no new auth mechanism,
  no session/cookie-based login for a single-owner utility page.
- **Ask first:** anything beyond the three tables above (a date-range picker, CSV export, charts/JS)
  - keep this iteration to exactly what was asked.
- **Never:** expose this route without the key check; add any third-party charting
  library/JS framework for three HTML tables; expose raw `visitor_hash` values in the rendered page
  (they're pseudonymous, not secret, but there's no reason to print them - only aggregated
  counts/slugs/dates belong in the output).

**Documentation debt:** README's "Analytics database access" section gets one added sentence noting
`analytics_writer` now also has SELECT (supersedes the "id column only" note from the previous PR) -
main-session work after this ships, not backend-dev's.

## Success Criteria

- [ ] `GET /dashboard?key=<correct>` returns an HTML page with three tables: daily unique visitors
      (30 days), top playlists by view, top playlists by click.
- [ ] `GET /dashboard` (no key) and `GET /dashboard?key=<wrong>` both return 404.
- [ ] `dotnet build`/`dotnet test` green, including the new tests above.
- [ ] Verified live against the real production data (curl with the real key) before calling this
      done - this project's standing rule for anything DB-backed.

## Open Questions

None blocking.
