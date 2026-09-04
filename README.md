# TheBluesland

A public, editorial playlist showcase for Spotify playlists curated by Mehmet Oya — blues, rock,
and the records that connect them, each introduced with a short curator note rather than left to
speak for itself. Visitors filter the catalogue by mood, genre, occasion, and era; every playlist
page embeds the real Spotify player (click-to-load) alongside editorial context Spotify itself
doesn't provide.

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

Track-level data is **never** persisted anywhere. The production web app never holds a Spotify or
AI credential — only a read-only database connection string. See
[`docs/adr/0002-spotify-veri-mimarisi.md`](docs/adr/0002-spotify-veri-mimarisi.md) for the full
rationale, and [`docs/business-technical-specification.md`](docs/business-technical-specification.md)
for the complete spec.

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

## Repository layout

```text
src/TheBluesland.Web/       Blazor Web App - content reading, validation, and web presentation
src/TheBluesland.Data/      EF Core / PostgreSQL schema and migrations
tools/spotify-playlist-fetcher/   Spotify sync tool, run monthly by GitHub Actions
content/playlists/          Version-controlled editorial playlist content
tests/TheBluesland.UnitTests/     Unit, schema, and web integration tests (Testcontainers Postgres)
tests/TheBluesland.E2ETests/      Playwright smoke tests against the real app, in-process
.github/workflows/           CI, monthly Spotify sync, and deploy automation
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

## CI/CD

Every pull request runs six independent checks (`.github/workflows/ci.yml`): content validation,
build + test + format, a Playwright smoke test, the Tailwind production build, a dependency and
secret scan, and a Docker image build. All six are required status checks on `main`.

On every push to `main`, `.github/workflows/deploy.yml` builds the same Dockerfile and pushes an
immutable, commit-SHA-tagged image to GHCR, then triggers a Render deploy. Render gates traffic on
the app's own `/health/ready` endpoint before routing to the new instance, and rollback is Render's
native, immutable-image-based rollback (see `.github/render.yaml`).

## Spotify cache database access (SEC-001)

`spotify_playlist_cache` is accessed through two separate Postgres roles, created by
[`src/TheBluesland.Data/Scripts/create-spotify-cache-roles.sql`](src/TheBluesland.Data/Scripts/create-spotify-cache-roles.sql)
(run once against the Neon project, after migrations have been applied — see the script's own
header comment for the exact steps, including rotating the placeholder passwords it ships with):

- `spotify_cache_readonly` — SELECT only. Its connection string is stored as the Render environment
  variable `ConnectionStrings__SpotifyPlaylistCache` (`.github/render.yaml`). This is the production
  web app's **only** runtime database access, and its only runtime credential of any kind.
- `spotify_cache_readwrite` — SELECT/INSERT/UPDATE. Its connection string is stored as
  `NEON_SYNC_CONNECTION_STRING`, a GitHub Actions repository secret scoped only to
  `sync-spotify.yml`. It is never present in the Render production environment.

Spotify credentials (`SPOTIFY_CLIENT_ID`, `SPOTIFY_REFRESH_TOKEN`) are likewise GitHub Actions
secrets scoped only to the monthly sync workflow — never Render, never `ci.yml`, never `deploy.yml`.
This isolation is pinned by regression tests
(`tests/TheBluesland.UnitTests/Workflows/CiWorkflowSecretIsolationTests.cs` and
`DeployWorkflowSecretIsolationTests.cs`).

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
- [`docs/product/backlog.md`](docs/product/backlog.md) — implementation-ordered user stories
- [`docs/product/plan.md`](docs/product/plan.md) — current phase and progress
