-- Role definitions for spotify_playlist_cache read/write separation (US-002, spec section 13
-- SEC-001). The production web app (TheBluesland.Web) must only ever be able to SELECT from
-- spotify_playlist_cache; only the out-of-process monthly sync tool
-- (tools/spotify-playlist-fetcher) may INSERT/UPDATE it. This script creates the two Postgres
-- roles that enforce that separation and grants each role exactly the privileges it needs.
--
-- Run this AFTER the TheBluesland.Data migrations have been applied (the spotify_playlist_cache
-- table must already exist).
--
-- Portable, standard PostgreSQL syntax only (CREATE ROLE / GRANT, no Neon-specific extensions),
-- so it runs unmodified both against a real Neon project (Neon SQL editor or psql) and against a
-- disposable Testcontainers Postgres instance in
-- tests/TheBluesland.UnitTests/SpotifyPlaylistCacheRoleTests.cs. Safe to re-run: role creation is
-- guarded, and re-issuing an identical GRANT is a no-op.
--
-- SECURITY: the passwords below are placeholders for local/test use only. Immediately after
-- running this script against a real Neon project, rotate both passwords:
--   ALTER ROLE spotify_cache_readonly WITH PASSWORD '<new-strong-password>';
--   ALTER ROLE spotify_cache_readwrite WITH PASSWORD '<new-strong-password>';
-- Then build the two connection strings and store them as documented in README.md:
--   - readonly  -> NEON_READONLY_CONNECTION_STRING, Render environment variable
--   - readwrite -> NEON_SYNC_CONNECTION_STRING, GitHub Actions repository secret only

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'spotify_cache_readonly') THEN
        CREATE ROLE spotify_cache_readonly LOGIN PASSWORD 'placeholder-readonly-password';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'spotify_cache_readwrite') THEN
        CREATE ROLE spotify_cache_readwrite LOGIN PASSWORD 'placeholder-readwrite-password';
    END IF;
END
$$;

GRANT SELECT ON spotify_playlist_cache TO spotify_cache_readonly;
GRANT SELECT, INSERT, UPDATE ON spotify_playlist_cache TO spotify_cache_readwrite;
