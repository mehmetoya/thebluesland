-- Role definition for the visitor/playlist-click analytics write path
-- (docs/specs/visitor-and-playlist-click-analytics.md, Design section 2). This is the first
-- write-capable database credential the production web app has ever held (ADR-0002 madde 4:
-- previously read-only only). To keep that as narrow as possible, this role is completely
-- disjoint from spotify_cache_readonly/spotify_cache_readwrite (create-spotify-cache-roles.sql):
-- it can INSERT into page_view_events and read back nothing except the id column it just
-- generated - no SELECT on any other column, and no access of any kind to spotify_playlist_cache.
--
-- 2026-09-11 production incident: the version of this script without the schema USAGE and
-- column-level id SELECT grants below passed AnalyticsRoleTests.cs (which exercises a raw,
-- RETURNING-free INSERT) but failed in real production traffic with "permission denied for table
-- page_view_events". Root cause: EF Core's Npgsql provider generates
-- `INSERT ... RETURNING id` for the identity column, and PostgreSQL requires SELECT privilege on
-- any column named in a RETURNING clause - INSERT privilege alone is not enough. Fixed by granting
-- SELECT on just the id column (not the whole table, preserving the "can't read event content"
-- guarantee) and by granting schema USAGE explicitly rather than assuming Neon grants it to new
-- roles by default. AnalyticsRoleTests.cs was also fixed to insert via a real
-- IDbContextFactory<AnalyticsDbContext> (the actual runtime code path) instead of hand-written SQL,
-- so a future regression here fails the test instead of only showing up in production.
--
-- Run this AFTER the TheBluesland.Data migrations for AnalyticsDbContext have been applied (the
-- page_view_events table must already exist).
--
-- Portable, standard PostgreSQL syntax only (CREATE ROLE / GRANT, no Neon-specific extensions), so
-- it runs unmodified both against a real Neon project (Neon SQL editor or psql) and against a
-- disposable Testcontainers Postgres instance in
-- tests/TheBluesland.UnitTests/AnalyticsRoleTests.cs. Safe to re-run: role creation is guarded,
-- and re-issuing an identical GRANT is a no-op.
--
-- SECURITY: the password below is a placeholder for local/test use only. Immediately after running
-- this script against a real Neon project, rotate it:
--   ALTER ROLE analytics_writer WITH PASSWORD '<new-strong-password>';
-- Then build the connection string and store it as documented in README.md:
--   - analytics_writer -> ConnectionStrings__Analytics, Render environment variable only
--
-- Example queries for Mehmet to run himself in the Neon SQL editor (his own admin credentials,
-- never this role - it has no SELECT grant):
--   -- Approximate daily unique visitors:
--   select occurred_at::date as day, count(distinct visitor_hash) as unique_visitors
--   from page_view_events
--   where event_type = 'page_view'
--   group by day
--   order by day desc;
--
--   -- Top playlists by page view:
--   select playlist_slug, count(*) as views
--   from page_view_events
--   where event_type = 'page_view' and playlist_slug is not null
--   group by playlist_slug
--   order by views desc;
--
--   -- Top playlists by Spotify click-through:
--   select playlist_slug, count(*) as clicks
--   from page_view_events
--   where event_type = 'spotify_click'
--   group by playlist_slug
--   order by clicks desc;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'analytics_writer') THEN
        CREATE ROLE analytics_writer LOGIN PASSWORD 'placeholder-analytics-password';
    END IF;
END
$$;

GRANT USAGE ON SCHEMA public TO analytics_writer;
GRANT INSERT ON page_view_events TO analytics_writer;
GRANT SELECT (id) ON page_view_events TO analytics_writer;
GRANT USAGE, SELECT ON SEQUENCE page_view_events_id_seq TO analytics_writer;
