# Spec: Public playlists JSON feed

Owner-provided contract (2026-09-18), verified against this repo before implementation. The
"Repo findings" section records what the code actually does; the contract sections are the owner's
text, unchanged in substance.

## Objective

Add a read-only, public endpoint `GET /playlists.json` returning every playlist listed on the site
in a fixed JSON shape.

Why: the owner's other site (mehmetoya.com) fetches this once per build to show thebluesland
playlists (a `/playlists/` page, a "latest playlists" home section, per-post "listen on
thebluesland" cards). There is no machine-readable listing today, and scraping HTML would be
brittle. This spec covers the producer side only - nothing for mehmetoya.com is built here.

The consumer is a server-side Node `fetch` with a short timeout, not a browser: no CORS, and no CORS
headers are added.

## Repo findings (verified against the code, replaces the owner's "observed from outside" notes)

- Stack is .NET 10 / Blazor static SSR (not Node/wrangler). Hosted on Render (Docker) behind
  Cloudflare. Routes are minimal-API `app.MapGet(...)` endpoints in `WebHostFactory.cs` next to the
  Razor component pages; `/sitemap.xml`, `/robots.txt`, `/llms.txt` are the existing
  machine-readable siblings this endpoint sits beside. `MapGet` registers GET only, so HEAD and
  every other method already return 405 - matches the "keep current behavior" requirement without
  extra code.
- **Single source of truth already exists:** `PlaylistContentRepository.FindAllPublishedAsync`. The
  home grid (`HomePage.razor` -> `PlaylistFilter.Apply(allPublished, criteria)` -> paging), every
  `/collections/{slug}` page (`PlaylistFilter.Apply(published, collection.Criteria)`) and
  `/sitemap.xml` (`SitemapGenerator`) all read it. Building the feed from the same call makes the
  three sets equal by construction, not by a test.
- **`addedAt` already exists as explicit data:** `PlaylistContent.PublishedAt` (front-matter
  `publishedAt: yyyy-MM-dd`). `PlaylistContentValidator` requires it for published content and
  the `content-validation` CI job enforces it. No git-history backfill and no data-model change is
  needed. It is not derived from Spotify and never from "today".
- `slug` is already validated against exactly the contract's pattern
  (`^[a-z0-9]+(-[a-z0-9]+)*$`) and for uniqueness by `PlaylistContentValidator` (CI-gated).
- The cards render `Content.Title`, `Content.Summary` (validated 40-180 chars, plain text, HTML-
  encoded by Razor), and - only when the cache snapshot `IsPlayable` - `CoverImageUrl` and
  `TrackCount`. The feed mirrors exactly that, so feed and site cannot disagree.
- Cover image: the sync tool stores a single URL per playlist (`images[0].url`, Spotify's first -
  typically largest - image). The contract says "prefer ~300px if several exist"; only one is
  stored, so the feed emits the same URL the cards use. Choosing a size would need a sync-tool/data
  change, which the contract lists under "Ask first" - not done here (reported as a deviation).
- Collections: membership is not stored; it is computed by `PlaylistFilter.Apply(published,
  collection.Criteria)` for each entry in `PlaylistCollections.All`. The feed uses the same call
  on the same era-overlaid list, so it matches the collection pages.
- `PageViewRoute` (analytics) only classifies known content pages, so feed fetches are not counted
  as visitors.

## The contract (owner's text)

`GET /playlists.json` -> `200`, `Content-Type: application/json; charset=utf-8`

```json
{
  "generatedAt": "2026-09-18T12:00:00Z",
  "playlists": [
    {
      "slug": "bluesland",
      "title": "Bluesland",
      "url": "https://thebluesland.com/playlists/bluesland",
      "description": "Short plain-text blurb.",
      "trackCount": 3195,
      "image": "https://i.scdn.co/image/ab67616d00001e02...",
      "collections": ["blues"],
      "addedAt": "2025-03-14"
    }
  ]
}
```

| Field | Type | Required | Rule | Source in this repo |
|---|---|---|---|---|
| `generatedAt` | string | yes | ISO 8601 UTC time the response was generated | request time, `yyyy-MM-ddTHH:mm:ssZ` |
| `playlists` | array | yes | Every publicly listed playlist; same set as the home grid (all pages) and the sitemap's `/playlists/` URLs. No pagination. | `FindAllPublishedAsync` |
| `slug` | string | yes | Unique, stable, equals the `/playlists/<slug>` path segment, matches `^[a-z0-9]+(-[a-z0-9]+)*$` | `Content.Slug` |
| `title` | string | yes | Display title exactly as shown | `Content.Title` |
| `url` | string | yes | Absolute canonical `https://thebluesland.com/playlists/<slug>` | `SiteUrl.BuildAbsolute` (configured public origin, same as the sitemap) |
| `description` | string | no | Plain text, max 300 chars | `Content.Summary` (already <= 180; defensively capped at 300) |
| `trackCount` | integer | no | >= 0 | cache `TrackCount`, only when `IsPlayable` (as on the cards) |
| `image` | string | no | Absolute https cover URL used on the cards | cache `CoverImageUrl`, only when `IsPlayable` and an absolute https URL |
| `collections` | string[] | no | Slugs of `/collections/<slug>` pages the playlist belongs to | computed as above; omitted when empty |
| `addedAt` | string | yes | `YYYY-MM-DD` first-published date | `Content.PublishedAt` |

- Optional fields are omitted when unknown - never `null`, never empty strings. Extra fields are not
  added.
- Order: `addedAt` descending, ties by `title` ascending (case-insensitive); a final `slug` tie-break
  makes the order total, so identical data always serializes identically.
- Excluded: anything private, unlisted or draft (already excluded by `FindAllPublishedAsync`). A
  published entry with no `publishedAt` cannot exist while content validation gates CI; if one ever
  reached runtime it is skipped rather than given an invented date.
- Headers: `Cache-Control: public, max-age=300`, `X-Robots-Tag: noindex`. Non-GET -> 405 (already the
  behavior of `MapGet`). No CORS headers. CSP untouched.

## Commands

```sh
dotnet build TheBluesland.slnx
dotnet test TheBluesland.slnx                      # Docker required (Testcontainers-backed tests elsewhere)
PlaylistContent__Directory="$(pwd)/content/playlists" \
  dotnet run --project src/TheBluesland.Web/TheBluesland.Web.csproj   # local dev on :5000
```

The contract's `curl | jq` checks are run against local dev, then production after the normal
deploy path (PR -> merge -> `deploy.yml` -> Render).

## Project structure

```
src/TheBluesland.Web/Feed/PlaylistsFeedBuilder.cs   -> feed records + Build + Serialize (one small
                                                        dedicated builder, same pattern as
                                                        SitemapGenerator/AiDiscoveryGenerator)
src/TheBluesland.Web/WebHostFactory.cs              -> `app.MapGet("/playlists.json", ...)`
tests/TheBluesland.UnitTests/Web/PlaylistsFeedBuilderTests.cs       -> pure mapping/ordering tests
tests/TheBluesland.UnitTests/Web/PlaylistsFeedIntegrationTests.cs   -> in-process host tests
README.md                                           -> one short "Public playlists feed" section
```

## Code style

Typed records serialized with `System.Text.Json` (camelCase, `WhenWritingNull`), like
`StructuredDataBuilder`. Relaxed JSON escaping so titles/summaries with apostrophes, `&` or Turkish
characters are emitted as written ("title exactly as shown") - safe because the response is
`application/json`, never embedded in HTML. The endpoint handler stays thin and delegates to the
builder.

## Testing strategy

- Builder unit tests (in-memory content + snapshots): required fields, slug pattern/uniqueness,
  ordering and tie-breaks, optional-field omission with a recursive "no null / empty string" scan,
  collection membership, unavailable-cache degradation, non-https image dropped, description cap,
  skipped null `publishedAt`, `generatedAt` format, unescaped non-ASCII.
- In-process integration tests (temp content directory written by the test, unreachable DB like the
  other web tests): 200 + exact content type, cache and robots headers, no CORS header, POST/HEAD
  -> 405, feed count equals the sitemap's `/playlists/` URL count, every feed URL resolves 200,
  canonical origin used, and a draft never appears.
- The contract's shell checks against local dev and then production (results go in the PR/report).

## Boundaries

- **Always:** reuse `FindAllPublishedAsync`; additive change only; deterministic output; published
  playlists only.
- **Ask first:** a new dependency; changing the playlist data model or the sync tool (e.g. storing
  several cover sizes); any Cloudflare/WAF/bot/caching setting; changing an existing URL or page.
- **Never:** touch existing HTML/CSS/CSP; add CORS headers; expose tokens or non-public data; scrape
  own HTML; invent dates; work around a Cloudflare challenge or WAF block (report it instead).

## Success criteria

- [ ] `GET /playlists.json` returns the exact contract shape in production and every shell check in
      the contract passes.
- [ ] Feed count equals the sitemap's `/playlists/` URL count.
- [ ] No existing page, style or header changed; README documents the endpoint.
- [ ] `dotnet build` / `dotnet test` green.

## Open questions

None blocking. One noted deviation: `image` is the single stored cover URL, not a ~300px variant
(see Repo findings).
