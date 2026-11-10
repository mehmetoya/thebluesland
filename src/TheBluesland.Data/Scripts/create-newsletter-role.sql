-- Role for the TheBluesland Weekly pipeline (docs/specs/weekly-newsletter-news-ingest.md, Design
-- section 5). Used only by tools/weekly-newsletter from GitHub Actions; never by the production web
-- app, which has no access to the newsletter schema at all.
--
-- Least privilege, mirroring create-analytics-role.sql:
--   - USAGE on the newsletter schema only - no CREATE, so no DDL of any kind.
--   - SELECT/INSERT/DELETE on newsletter.news_article: insert (incl. ON CONFLICT DO NOTHING),
--     read back for clustering, delete for the 8-week retention. No UPDATE - ingest never rewrites
--     a row.
--   - USAGE/SELECT on the identity sequence, needed for EF Core's INSERT ... RETURNING id (the
--     2026-09-11 analytics incident, see create-analytics-role.sql).
--   - Nothing on newsletter.__ef_migrations_history (migrations are applied by Mehmet's own admin
--     credentials) and nothing on spotify_playlist_cache or page_view_events.
-- Later modules (story-clustering, ...) add their tables' grants to this script explicitly rather
-- than through ALTER DEFAULT PRIVILEGES, so every privilege stays visible here.
--
-- Run this AFTER the NewsletterDbContext migrations have been applied (the table and its sequence
-- must already exist). Portable PostgreSQL only - it runs unmodified on Neon and on the disposable
-- Testcontainers instance in tests/TheBluesland.UnitTests/NewsletterRoleTests.cs. Safe to re-run.
--
-- SECURITY: the password below is a placeholder for local/test use only. Immediately after running
-- this script against a real Neon project, rotate it:
--   ALTER ROLE newsletter_writer WITH PASSWORD '<new-strong-password>';
-- Then store the role's connection string only as the GitHub Actions secret
-- NEON_NEWSLETTER_CONNECTION_STRING, scoped to the newsletter workflows. Never in Render.

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'newsletter_writer') THEN
        CREATE ROLE newsletter_writer LOGIN PASSWORD 'placeholder-newsletter-password';
    END IF;
END
$$;

GRANT USAGE ON SCHEMA newsletter TO newsletter_writer;
GRANT SELECT, INSERT, DELETE ON newsletter.news_article TO newsletter_writer;
GRANT USAGE, SELECT ON SEQUENCE newsletter.news_article_id_seq TO newsletter_writer;
