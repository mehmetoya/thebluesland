-- Role for the TheBluesland Weekly pipeline (docs/specs/weekly-newsletter-news-ingest.md, Design
-- section 5). Used only by tools/weekly-newsletter from GitHub Actions; never by the production web
-- app, which has no access to the newsletter schema at all.
--
-- Least privilege, mirroring create-analytics-role.sql:
--   - USAGE on the newsletter schema only - no CREATE, so no DDL of any kind.
--   - SELECT/INSERT/DELETE on newsletter.news_article: insert (incl. ON CONFLICT DO NOTHING and
--     EF Core's INSERT ... RETURNING id, which needs SELECT on id), read back for clustering,
--     delete for the 8-week retention. No UPDATE: defence in depth against accidental rewrites,
--     not an integrity guarantee (a leaked credential could still delete and re-insert).
--   - No sequence grant: an identity column's nextval during INSERT does not check the sequence's
--     ACL, so granting it would only let the role burn ids directly.
--   - Nothing on newsletter.__ef_migrations_history, spotify_playlist_cache or page_view_events.
-- Later modules (story-clustering, ...) add their tables' grants here explicitly rather than
-- through ALTER DEFAULT PRIVILEGES, so every privilege stays visible in this file.
--
-- Fail-closed (security review 2026-11-10):
--   - The role is created NOLOGIN, so forgetting the password step leaves it unusable, never open
--     with a password that is visible in this public repository.
--   - A pre-existing role (e.g. created through the Neon Console, which makes it a neon_superuser
--     member with read/write on every table) is rejected rather than silently adopted. Create
--     this role ONLY through this script.
--   - Re-running revokes and re-grants, so the role converges to exactly what is listed below.
--   - The script ends by verifying the grants actually took effect.
--
-- Run this AFTER the NewsletterDbContext migrations have been applied (the table must exist), as
-- the database owner. Portable PostgreSQL only - it runs unmodified on Neon and on the disposable
-- Testcontainers instance in tests/TheBluesland.UnitTests/NewsletterRoleTests.cs. Safe to re-run.
--
-- Then enable login with a strong password without putting it in SQL history, e.g. in psql:
--   ALTER ROLE newsletter_writer LOGIN;
--   \password newsletter_writer
-- (or set the password in the Neon Console). Build the connection string with sslmode=require and
-- store it only as the GitHub Actions secret NEON_NEWSLETTER_CONNECTION_STRING, scoped to the
-- newsletter workflows - never in Render. Rotate it after any suspected exposure and update the
-- secret in the same step.

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'newsletter_writer') THEN
        CREATE ROLE newsletter_writer NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
    END IF;

    -- Verify rather than ALTER: on Neon the owner is not a superuser and may not change these
    -- attributes, but it can always read them.
    IF EXISTS (
        SELECT 1 FROM pg_catalog.pg_roles
        WHERE rolname = 'newsletter_writer'
          AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)
    ) THEN
        RAISE EXCEPTION 'newsletter_writer has elevated attributes (superuser/createdb/createrole/replication/bypassrls); drop it and re-run this script';
    END IF;

    IF EXISTS (
        SELECT 1 FROM pg_catalog.pg_auth_members m
        JOIN pg_catalog.pg_roles r ON r.oid = m.member
        WHERE r.rolname = 'newsletter_writer'
    ) THEN
        RAISE EXCEPTION 'newsletter_writer is a member of another role (e.g. neon_superuser); drop it and re-run this script';
    END IF;
END
$$;

REVOKE ALL ON ALL TABLES IN SCHEMA newsletter FROM newsletter_writer;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA newsletter FROM newsletter_writer;

GRANT USAGE ON SCHEMA newsletter TO newsletter_writer;
GRANT SELECT, INSERT, DELETE ON newsletter.news_article TO newsletter_writer;

DO $$
BEGIN
    IF NOT has_schema_privilege('newsletter_writer', 'newsletter', 'USAGE')
       OR NOT has_table_privilege('newsletter_writer', 'newsletter.news_article', 'SELECT')
       OR NOT has_table_privilege('newsletter_writer', 'newsletter.news_article', 'INSERT')
       OR NOT has_table_privilege('newsletter_writer', 'newsletter.news_article', 'DELETE') THEN
        RAISE EXCEPTION 'newsletter_writer grants did not take effect; run this script as the owner of the newsletter schema';
    END IF;
END
$$;
