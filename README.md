# TheBluesland

[![CI](https://github.com/mehmetoya/thebluesland/actions/workflows/ci.yml/badge.svg)](https://github.com/mehmetoya/thebluesland/actions/workflows/ci.yml)
[![Deploy](https://github.com/mehmetoya/thebluesland/actions/workflows/deploy.yml/badge.svg)](https://github.com/mehmetoya/thebluesland/actions/workflows/deploy.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Blazor](https://img.shields.io/badge/Blazor-Web%20App-512BD4?logo=blazor&logoColor=white)
![Tailwind CSS 4](https://img.shields.io/badge/Tailwind%20CSS-4-06B6D4?logo=tailwindcss&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Neon-4169E1?logo=postgresql&logoColor=white)

A public, editorial playlist showcase for Spotify playlists curated by Mehmet Oya — blues, rock,
and the records that connect them, each introduced with a short curator note rather than left to
speak for itself. Visitors filter the catalogue by mood, genre, occasion, and era; every playlist
page embeds the real Spotify player (click-to-load) alongside editorial context Spotify itself
doesn't provide. Playlists whose era is genuinely mixed get an era tag computed automatically from
Spotify's own release-date data, rather than staying untagged or guessed by hand.

**Live:** <https://thebluesland.onrender.com>

This is not a Spotify clone or a new music catalogue — it has no accounts, no playback outside the
embedded Spotify player, and never stores track-level data.

## ✨ At a glance

- 🎧 120 curated playlists, each with a hand-written curator note — never left to speak for itself
- 🏷️ Filter by mood, genre, occasion, and era, or browse dedicated pages per taxonomy value
- 🕰️ Automatic era tagging for genuinely mixed-era playlists, computed from Spotify's own release dates
- 🌓 A calm, dark-first "late-night record room" visual identity — see [Design system](#-design-system)
- 🤖 Built for AI answer engines, not just search bots — `llms.txt`, JSON-LD, `FAQPage` schema
- 🔒 Zero Spotify/AI credentials in production — only a read-only database connection

## 🏗️ Architecture

TheBluesland is a **hybrid content model**: editorial judgment lives in git, Spotify-sourced facts
live in a database cache, and the two are joined by `spotifyPlaylistId`.

```mermaid
flowchart LR
    MD["content/playlists/*.md<br/>title, tags, curator note"]
    API["Spotify Web API<br/>name, cover, track count, artists"]
    Sync["Monthly sync job<br/>(GitHub Actions)"]
    Cache[("PostgreSQL cache<br/>spotify_playlist_cache")]
    Web["TheBluesland.Web<br/>static SSR"]
    Visitor(["Visitor"])

    API --> Sync --> Cache
    MD --> Web
    Cache -- read-only --> Web
    Web --> Visitor
```

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

## 🛠️ Tech stack

| Layer | Choice |
| --- | --- |
| Application | .NET 10 / C# 14, Blazor Web App |
| Rendering | Static SSR by default; isolated Interactive Server only where needed |
| Database | PostgreSQL (Neon, free tier) via EF Core — Spotify-sourced fields only |
| Editorial content | Markdown + YAML front matter, git-versioned |
| Spotify integration | Authorization Code + PKCE, monthly sync via GitHub Actions (never in the production app) |
| Styling | Tailwind CSS 4, a hand-rolled design-token scale (see [Design system](#-design-system)) |
| Testing | xUnit + Shouldly, Testcontainers (real Postgres in tests), Playwright (.NET) for e2e smoke |
| CI/CD | GitHub Actions — PR pipeline (build/test/format/content-validation/dependency+secret scan/Docker build) and a deploy pipeline to Render |
| Hosting | Render (web, Docker image) + Neon (Postgres) — $0/month |

No MediatR, no AutoMapper, no second client (API/mobile) — see
[`docs/adr/0003-mimari-kapsam.md`](docs/adr/0003-mimari-kapsam.md) for why the architecture stays
deliberately small.

## 🎨 Design system

TheBluesland is built to feel like **a late-night record room, not a Spotify clone** — intimate,
editorial and calm rather than another generic streaming-app UI
(`docs/business-technical-specification.md` section 10).

- 🌑 **Palette** — dark-first by design: a deep navy/charcoal background (`#12141b`), warm
  off-white text (`#ece5d8`), and exactly one warm accent, amber (`#d99a4e`). No Spotify-green
  anywhere.
- 🌗 **Light mode is opt-in, not the default.** A header toggle switches to a warm cream/parchment
  counterpart palette (WCAG AA-verified) that every first-time visitor still meets as dark
  regardless of their OS color-scheme setting — the identity above is deliberately what most
  visitors see first. Choice is remembered (`localStorage`); an external, non-deferred
  `wwwroot/js/theme.js` (required by this app's inline-script-free CSP) applies it before first
  paint so there's no flash of the other theme. See `docs/specs/dark-light-mode-toggle.md`.
- **Type** — serif headings (`ui-serif`) carry the editorial voice; body text uses a system-font
  sans stack aligned with Apple's own (`-apple-system, BlinkMacSystemFont, …`) for familiar, fast,
  zero-webfont legibility.
- **A real token scale, not one-off numbers.** `--space-1` … `--space-8` (an 8px grid) and
  `--font-size-xs` … `--font-size-display` drive every touched selector in
  `src/TheBluesland.Web/Styles/app.css`. The two largest heading sizes are fluid (`clamp()`), so a
  single-word title never overflows a narrow phone screen.
- **Motion is restrained and optional.** Hover states use short (150ms) transitions; anything that
  actually moves (`transform`) only runs inside `@media (prefers-reduced-motion: no-preference)` —
  respecting a visitor's OS-level motion preference is a hard rule, not a nice-to-have.
- Rolled out in three reviewed passes rather than one sweeping rewrite — a token scale, then a
  single-page pilot, then full rollout plus a typography/motion pass — each shipped and checked
  before the next began. See `docs/specs/design-tokens-and-home-pilot.md`,
  `docs/specs/design-tokens-rollout-remaining-pages.md` and
  `docs/specs/apple-hubx-motion-and-type-polish.md`.

## 🔍 Search & AI discoverability

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

## 📁 Repository layout

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

## 💻 Local development

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

## 🔄 CI/CD & automation

Every pull request runs six independent checks (`.github/workflows/ci.yml`): content validation,
build + test + format, a Playwright smoke test, the Tailwind production build, a dependency and
secret scan, and a Docker image build. All six are required status checks on `main`.

On every push to `main`, `.github/workflows/deploy.yml` builds the same Dockerfile and pushes an
immutable, commit-SHA-tagged image to GHCR, then triggers a Render deploy. Render gates traffic on
the app's own `/health/ready` endpoint before routing to the new instance, and rollback is Render's
native, immutable-image-based rollback (see `.github/render.yaml`).

**Keeping the free-tier instance awake:** Render's free plan sleeps a web service after 15 minutes
without inbound traffic, and the resulting cold start is slow enough to be noticeable to a first
visitor. Any inbound HTTP request resets that 15-minute clock, so a low-frequency external ping
prevents the sleep entirely without touching the app itself. `/health/live` (`WebHostFactory.cs`)
is the right target for that ping: unlike `/health/ready`, it runs no health checks at all (no
content load, no database, no Spotify/AI calls) and always returns a plain 200 — process liveness
only, matching spec 16.2's DB-independence rule and needing no new endpoint. To wire this up:

1. In [UptimeRobot](https://uptimerobot.com) (free plan, no card required), add an HTTP(s) monitor
   for `https://thebluesland.com/health/live` (or the `onrender.com` origin, pre-custom-domain) on
   its shortest free interval (5 minutes) — comfortably inside Render's 15-minute sleep window.
2. This is an external dashboard setup step with no repository-side configuration: no code, secret,
   or workflow change is required, and Render's own `healthCheckPath` (`.github/render.yaml`) stays
   on `/health/ready` unchanged — that setting governs traffic routing to a new deploy, not sleep.
3. Free-tier instance-hours (750/month per workspace) are still spent while the service stays awake
   around the clock; for a single service this fits within a 31-day month, but it's shared across
   every free service in the same Render workspace.

Everything that talks to Spotify or an AI provider runs out-of-process, on its own schedule, never
inside the web app or the PR/deploy pipelines:

| Workflow | Trigger | What it does |
| --- | --- | --- |
| `sync-spotify.yml` | Monthly cron, or manual | Refreshes `spotify_playlist_cache` for every playlist and auto-unpublishes any playlist Spotify now reports as private (`docs/specs/auto-unpublish-private-playlists.md`) via a bot-opened, auto-merged pull request — the only workflow with write access to the repository, scoped to this one job. `mode: sync` (default) skips playlists whose Spotify `snapshot_id` hasn't changed since the last run and processes the least-recently-synced playlists first, so a run interrupted mid-list (e.g. a Spotify rate-limit cooldown) resumes with whatever it didn't reach last time instead of restarting from the same point; `resync-eras` forces a full re-read (only needed after the era-bucket rules themselves change); `list-playlists`/`dump-cache` are read-only discovery modes. |
| `retry-rate-limited-sync.yml` | Every 3 hours, or manual | Re-dispatches `sync-spotify.yml` automatically once a detected Spotify rate-limit cooldown has elapsed — only for that specific known failure signature; any other failure is left alone so it still surfaces to Mehmet. Holds no Spotify/database credential, only `actions: write`. |
| `report-eras.yml` | Manual | Prints a per-playlist era-distribution report from the cache's stored bucket counts — a plain database read, no Spotify call, safe to re-run any time. |
| `suggest-curator-note.yml` | Manual | Drafts a curator-note suggestion for one playlist via Gemini, from the cache's public fields only; never writes to `content/playlists/*.md` — Mehmet applies it through a normal PR if he agrees. |

Each of `sync-spotify.yml`, `report-eras.yml` and `suggest-curator-note.yml` is the *only* workflow allowed to hold its particular external credential (see
[Spotify cache database access](#-spotify-cache-database-access-sec-001) below) — `ci.yml` and
`deploy.yml` can reach neither Spotify nor any AI provider, enforced by regression tests, not just
convention.

## 🔐 Spotify cache database access (SEC-001)

`spotify_playlist_cache` is accessed through two separate Postgres roles, created by
[`src/TheBluesland.Data/Scripts/create-spotify-cache-roles.sql`](src/TheBluesland.Data/Scripts/create-spotify-cache-roles.sql)
(run once against the Neon project, after migrations have been applied — see the script's own
header comment for the exact steps, including rotating the placeholder passwords it ships with):

- `spotify_cache_readonly` — SELECT only. Its connection string is stored as the Render environment
  variable `ConnectionStrings__SpotifyPlaylistCache` (`.github/render.yaml`) and separately as
  `NEON_READONLY_CONNECTION_STRING`, a GitHub Actions repository secret scoped only to
  `suggest-curator-note.yml` (US-016/ADR-0005; Render's environment variables aren't reachable from
  a GitHub Actions workflow, so the same role's connection string is stored a second time rather
  than shared).
- `spotify_cache_readwrite` — SELECT/INSERT/UPDATE. Its connection string is stored as
  `NEON_SYNC_CONNECTION_STRING`, a GitHub Actions repository secret scoped only to
  `sync-spotify.yml`. It is never present in the Render production environment.

### Analytics database access

`page_view_events` (visitor/playlist-click analytics, `docs/specs/visitor-and-playlist-click-analytics.md`)
is a second, completely separate table/role pair, created by
[`src/TheBluesland.Data/Scripts/create-analytics-role.sql`](src/TheBluesland.Data/Scripts/create-analytics-role.sql) —
`analytics_writer` can only `INSERT` into `page_view_events` (not even `SELECT` it back) and has no
access at all to `spotify_playlist_cache`. Its connection string is `ConnectionStrings__Analytics`,
a Render environment variable — **the first write-capable database credential the production web
app has ever held** (previously `spotify_cache_readonly` was its only DB access). No raw IP address
is ever stored: each event's `visitor_hash` is `SHA256(pepper + UTC-date + ip + user-agent)`, so the
same visitor hashes differently every day. `Analytics__VisitorHashPepper` (a random secret string,
also a Render env var) feeds that hash and must never be committed. See the role script's own header
comment for example SQL queries (daily unique visitors, top playlists by view/click) to run in the
Neon SQL editor.

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

## 📚 Documentation

- [`docs/business-technical-specification.md`](docs/business-technical-specification.md) — full
  product and technical spec (v0.2), including the design system (section 10) and taxonomy
  (section 8)
- [`docs/adr/`](docs/adr/) — architecture decision records
- [`docs/specs/`](docs/specs/) — feature-level specs written before implementation (design system
  rollout, auto-unpublish, era-threshold tuning, custom domain migration, and more)
- [`docs/automatic-eras.md`](docs/automatic-eras.md) — how automatic era tagging works, and its
  one-time rollout steps
- [`docs/product/backlog.md`](docs/product/backlog.md) — historical record of implementation-ordered
  user stories (frozen 2026-09-09; new work is tracked via `docs/specs/` instead)
- [`docs/product/plan.md`](docs/product/plan.md) — historical phase/progress record (frozen alongside
  the backlog)

### ⚙️ Runtime content and security

The application loads a catalogue snapshot once per process. Restart after editing Markdown;
production content changes take effect with the next deployment. `/health/ready` requires a
nonempty published catalogue that passes schema validation, independently of database availability.
Draft detail pages, draft aliases and draft social cards return 404. The temporary `/health/cache`
diagnostic endpoint has been removed; use operational logs for database diagnostics.

Canonical URLs use `Site__PublicOrigin` (default `https://thebluesland.onrender.com`). Set it to
an HTTPS origin when introducing a custom domain. Only that host, local development hosts, and two
hardcoded legacy hosts (`thebluesland.onrender.com`, `www.thebluesland.com` — permanently 301'd to
whatever `Site__PublicOrigin` is currently set to, see `SiteUrl.LegacyRedirectHosts` and
[`docs/specs/custom-domain-migration.md`](docs/specs/custom-domain-migration.md)) are accepted.
Request and forwarded headers never determine canonical URLs; no untrusted proxy headers are
enabled. Social card responses are cached server-side for 24 hours within a 16 MiB cache; a new
deployment clears the cache.

The taxonomy report is an explicit local tool, not a test. From the repository root:

```sh
dotnet run --project tools/playlist-taxonomy-report -- content/playlists /tmp/thebluesland-taxonomy
```

It writes `taxonomy-distribution.txt` and `all-playlists-taxonomy-scan.txt` to the requested directory.
