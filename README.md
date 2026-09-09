# TheBluesland

[![CI](https://github.com/mehmetoya/thebluesland/actions/workflows/ci.yml/badge.svg)](https://github.com/mehmetoya/thebluesland/actions/workflows/ci.yml)
[![Deploy](https://github.com/mehmetoya/thebluesland/actions/workflows/deploy.yml/badge.svg)](https://github.com/mehmetoya/thebluesland/actions/workflows/deploy.yml)

A public, editorial playlist showcase for Spotify playlists curated by Mehmet Oya — blues, rock,
and the records that connect them, each introduced with a short curator note rather than left to
speak for itself. Visitors filter the catalogue by mood, genre, occasion, and era; every playlist
page embeds the real Spotify player (click-to-load) alongside editorial context Spotify itself
doesn't provide. Playlists whose era is genuinely mixed get an era tag computed automatically from
Spotify's own release-date data, rather than staying untagged or guessed by hand.

**Live:** <https://thebluesland.onrender.com>

This is not a Spotify clone or a new music catalogue — it has no accounts, no playback outside the
embedded Spotify player, and never stores track-level data.

## Architecture

TheBluesland is a **hybrid content model**: editorial judgment lives in git, Spotify-sourced facts
live in a database cache, and the two are joined by `spotifyPlaylistId`.

| What | Where | Who writes it |
| --- | --- | --- |
| Title, summary, mood/genre/occasion/era tags, curator note, slug | Version-controlled Markdown + YAML front matter (`content/playlists/*.md`) | Hand-authored, reviewed like code |
| Playlist name, description, cover image, track count, artist list | PostgreSQL cache (`spotify_playlist_cache` table) | A monthly GitHub Actions sync job, from the real Spotify Web API |
| Automatic era tag, only for playlists whose editorial era is `mixed-era` | Same cache row (`computed_eras`, derived from `era_bucket_counts`) | Same sync job — reads each track's release year, buckets it, and stores only the aggregate counts, never a single release date |

Explicit editorial era tags always win; the computed tag only fills in where the editor
deliberately left it ambiguous. Track-level data is **never** persisted anywhere — not even
transiently for the era computation above. The production web app never holds a Spotify or AI
credential — only a read-only database connection string. See
[`docs/adr/0002-spotify-veri-mimarisi.md`](docs/adr/0002-spotify-veri-mimarisi.md) for the full
rationale, [`docs/automatic-eras.md`](docs/automatic-eras.md) for how the era computation works,
and [`docs/business-technical-specification.md`](docs/business-technical-specification.md) for the
complete spec.

The web app degrades gracefully: if the database is unreachable or a playlist's cache row is
missing/stale, editorial content still renders with a 200 response rather than an error page.

## Tech stack

| Layer | Choice |
| --- | --- |
| Application | .NET 10 / C# 14, Blazor Web App |
| Rendering | Static SSR by default; isolated Interactive Server only where needed |
| Database | PostgreSQL (Neon, free tier) via EF Core — Spotify-sourced fields only |
| Editorial content | Markdown + YAML front matter, git-versioned |
| Spotify integration | Authorization Code + PKCE, monthly sync via GitHub Actions (never in the production app) |
| Styling | Tailwind CSS 4 |
| Testing | xUnit + Shouldly, Testcontainers (real Postgres in tests), Playwright (.NET) for e2e smoke |
| CI/CD | GitHub Actions — PR pipeline (build/test/format/content-validation/dependency+secret scan/Docker build) and a deploy pipeline to Render |
| Hosting | Render (web, Docker image) + Neon (Postgres) — $0/month |

No MediatR, no AutoMapper, no second client (API/mobile) — see
[`docs/adr/0003-mimari-kapsam.md`](docs/adr/0003-mimari-kapsam.md) for why the architecture stays
deliberately small.

## Search & AI discoverability

Beyond standard SEO (unique title/description/canonical per page, `sitemap.xml`, server-generated
Open Graph images), the site is deliberately set up to be read correctly by generative answer
engines, not just crawled by classic search bots:

- `robots.txt` explicitly allows GPTBot, ChatGPT-User, OAI-SearchBot, ClaudeBot, Claude-SearchBot,
  Claude-User, PerplexityBot and Google-Extended, alongside a plain-text `llms.txt` summarising the
  site and every published playlist — the emerging convention AI crawlers look for.
- JSON-LD structured data (`WebSite`, `CollectionPage`, `MusicPlaylist`, `BreadcrumbList`,
  `FAQPage`, `AboutPage`/`Person`) is built from typed records
  (`src/TheBluesland.Web/Seo/StructuredDataBuilder.cs`), never string concatenation, and a
  regression test pins down that it can never carry a track-level field.
- The About page's FAQ section and its `FAQPage` JSON-LD are generated from the same array, so the
  visible text and the structured data can never drift apart — a requirement of Google's own
  FAQPage guidance.

## Repository layout

```text
src/TheBluesland.Web/       Blazor Web App - content reading, validation, and web presentation
src/TheBluesland.Data/      EF Core / PostgreSQL schema and migrations
tools/spotify-playlist-fetcher/   Spotify sync tool, run monthly by GitHub Actions
tools/playlist-taxonomy-report/   Local-only tool: reports mood/genre/occasion/era tag distribution
content/playlists/          Version-controlled editorial playlist content
tests/TheBluesland.UnitTests/     Unit, schema, and web integration tests (Testcontainers Postgres)
tests/TheBluesland.E2ETests/      Playwright smoke tests against the real app, in-process
.github/workflows/           CI, deploy, monthly Spotify sync, and manual AI-assisted tooling
.github/render.yaml          Render Blueprint (service definition, no secret values)
docs/                        Spec, ADRs, and product backlog/plan
```

## Local development

**Prerequisites:** .NET 10 SDK, Docker (for Testcontainers-backed tests and local image builds),
Node.js 22 (for the Tailwind build).

```bash
dotnet build
dotnet test                                  # spins up a real, disposable Postgres via Testcontainers
cd src/TheBluesland.Web && npm ci && npm run build:css
```

There is no local `appsettings` database connection by default — most of the app renders from
editorial Markdown alone. To exercise the cache-backed code paths locally, set the
`ConnectionStrings:SpotifyPlaylistCache` configuration key (environment variable:
`ConnectionStrings__SpotifyPlaylistCache`) to a Postgres instance where
`create-spotify-cache-roles.sql`'s migrations have been applied.

## CI/CD & automation

Every pull request runs six independent checks (`.github/workflows/ci.yml`): content validation,
build + test + format, a Playwright smoke test, the Tailwind production build, a dependency and
secret scan, and a Docker image build. All six are required status checks on `main`.

On every push to `main`, `.github/workflows/deploy.yml` builds the same Dockerfile and pushes an
immutable, commit-SHA-tagged image to GHCR, then triggers a Render deploy. Render gates traffic on
the app's own `/health/ready` endpoint before routing to the new instance, and rollback is Render's
native, immutable-image-based rollback (see `.github/render.yaml`).

Everything that talks to Spotify or an AI provider runs out-of-process, on its own schedule, never
inside the web app or the PR/deploy pipelines:

| Workflow | Trigger | What it does |
| --- | --- | --- |
| `sync-spotify.yml` | Monthly cron, or manual | Refreshes `spotify_playlist_cache` for every playlist. `mode: sync` (default) skips playlists whose Spotify `snapshot_id` hasn't changed since the last run; `resync-eras` forces a full re-read (only needed after the era-bucket rules themselves change); `list-playlists`/`dump-cache` are read-only discovery modes. |
| `report-eras.yml` | Manual | Prints a per-playlist era-distribution report from the cache's stored bucket counts — a plain database read, no Spotify call, safe to re-run any time. |
| `suggest-curator-note.yml` | Manual | Drafts a curator-note suggestion for one playlist via Gemini, from the cache's public fields only; never writes to `content/playlists/*.md` — Mehmet applies it through a normal PR if he agrees. |

Each of these three is the *only* workflow allowed to hold its particular external credential (see
[Spotify cache database access](#spotify-cache-database-access-sec-001) below) — `ci.yml` and
`deploy.yml` can reach neither Spotify nor any AI provider, enforced by regression tests, not just
convention.

## Spotify cache database access (SEC-001)

`spotify_playlist_cache` is accessed through two separate Postgres roles, created by
[`src/TheBluesland.Data/Scripts/create-spotify-cache-roles.sql`](src/TheBluesland.Data/Scripts/create-spotify-cache-roles.sql)
(run once against the Neon project, after migrations have been applied — see the script's own
header comment for the exact steps, including rotating the placeholder passwords it ships with):

- `spotify_cache_readonly` — SELECT only. Its connection string is stored as the Render environment
  variable `ConnectionStrings__SpotifyPlaylistCache` (`.github/render.yaml`) — the production web
  app's **only** runtime database access — and separately as `NEON_READONLY_CONNECTION_STRING`, a
  GitHub Actions repository secret scoped only to `suggest-curator-note.yml` (US-016/ADR-0005;
  Render's environment variables aren't reachable from a GitHub Actions workflow, so the same
  role's connection string is stored a second time rather than shared).
- `spotify_cache_readwrite` — SELECT/INSERT/UPDATE. Its connection string is stored as
  `NEON_SYNC_CONNECTION_STRING`, a GitHub Actions repository secret scoped only to
  `sync-spotify.yml`. It is never present in the Render production environment.

Spotify credentials (`SPOTIFY_CLIENT_ID`, `SPOTIFY_REFRESH_TOKEN`) are likewise GitHub Actions
secrets scoped only to the monthly sync workflow, and `GEMINI_API_KEY` is scoped only to
`suggest-curator-note.yml` — none of these four secrets are ever present in Render, `ci.yml`, or
`deploy.yml`, and `suggest-curator-note.yml` never receives the Spotify or sync-write secrets
either. This isolation is pinned by regression tests
(`tests/TheBluesland.UnitTests/Workflows/CiWorkflowSecretIsolationTests.cs`,
`DeployWorkflowSecretIsolationTests.cs` and `SuggestCuratorNoteWorkflowSecretIsolationTests.cs`).

**Connection string format:** Neon's dashboard gives you a `postgresql://user:pass@host/db?...` URI
by default, which Npgsql also accepts directly. If you hit a
`NpgsqlConnectionStringBuilder` parsing error, convert to ADO.NET keyword/value form instead:

```text
Host=<neon-host>;Database=<db>;Username=<role>;Password=<password>;SSL Mode=Require
```

## Documentation

- [`docs/business-technical-specification.md`](docs/business-technical-specification.md) — full
  product and technical spec (v0.2)
- [`docs/adr/`](docs/adr/) — architecture decision records
- [`docs/automatic-eras.md`](docs/automatic-eras.md) — how automatic era tagging works, and its
  one-time rollout steps
- [`docs/product/backlog.md`](docs/product/backlog.md) — implementation-ordered user stories
- [`docs/product/plan.md`](docs/product/plan.md) — current phase and progress

### Runtime content and security

The application loads a catalogue snapshot once per process. Restart after editing Markdown;
production content changes take effect with the next deployment. `/health/ready` requires a
nonempty published catalogue that passes schema validation, independently of database availability.
Draft detail pages, draft aliases and draft social cards return 404. The temporary `/health/cache`
diagnostic endpoint has been removed; use operational logs for database diagnostics.

Canonical URLs use `Site__PublicOrigin` (default `https://thebluesland.onrender.com`). Set it to
an HTTPS origin when introducing a custom domain. Only that host and local development hosts are
accepted. Request and forwarded headers never determine canonical URLs; no untrusted proxy headers
are enabled. Social card responses are cached server-side for 24 hours within a 16 MiB cache;
a new deployment clears the cache.

The taxonomy report is an explicit local tool, not a test. From the repository root:

```sh
dotnet run --project tools/playlist-taxonomy-report -- content/playlists /tmp/thebluesland-taxonomy
```

It writes `taxonomy-distribution.txt` and `all-playlists-taxonomy-scan.txt` to the requested directory.
