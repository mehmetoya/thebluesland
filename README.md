# TheBluesland

A public, editorial playlist showcase for Spotify playlists curated by Mehmet Oya. See
`docs/business-technical-specification.md` (v0.2) for the full spec, `docs/adr/` for architecture
decisions, and `docs/product/backlog.md` for the implementation-ordered backlog.

## Spotify cache database access (SEC-001)

`spotify_playlist_cache` is accessed through two separate Postgres roles, created by
`src/TheBluesland.Data/Scripts/create-spotify-cache-roles.sql` (run once against the Neon project,
after migrations have been applied — see the script's own header comment for the exact steps,
including rotating the placeholder passwords it ships with):

- `spotify_cache_readonly` — SELECT only. Its connection string is stored as `NEON_READONLY_CONNECTION_STRING`,
  a **Render environment variable**. This is the production web app's only runtime database access.
- `spotify_cache_readwrite` — SELECT/INSERT/UPDATE. Its connection string is stored as `NEON_SYNC_CONNECTION_STRING`,
  a **GitHub Actions repository secret only**, scoped to the `sync-spotify.yml` workflow. It is never
  present in the Render production environment.

**Connection string format:** Neon's dashboard gives you a `postgresql://user:pass@host/db?...` URI by
default — Npgsql does **not** accept that format directly and fails with a confusing
`NpgsqlConnectionStringBuilder` parsing error. Convert it to ADO.NET keyword/value form before storing
it as either secret:
```
Host=<neon-host>;Database=<db>;Username=<role>;Password=<password>;SSL Mode=Require
```
