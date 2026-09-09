# Custom domain migration: thebluesland.com

## Problem

Mehmet bought `thebluesland.com`. The site's only identity today is
`https://thebluesland.onrender.com` — every canonical URL, the sitemap, `robots.txt`'s `Sitemap:`
line, `llms.txt`, and every JSON-LD `@id`/`url` field is built from `Site:PublicOrigin`
(`src/TheBluesland.Web/Seo/SiteUrl.cs`), which defaults to that onrender.com origin and is not yet
overridden anywhere.

Naively flipping `Site:PublicOrigin` to the new domain breaks more than it fixes:
`WebHostFactory.Create` derives `AllowedHosts`/`AddHostFiltering` from that same single value
(one canonical host, by design — a Host-header allowlist, not a redirect list), so every request
still arriving at `thebluesland.onrender.com` — already indexed by Google, already the target of
any existing share/bookmark, referenced from `llms.txt` and every JSON-LD document generated
before this migration — would start receiving `400 Bad Request` outright, with zero notice to the
client or to search engines that the content moved.

## Decision

Canonical origin becomes `https://thebluesland.com` (bare apex, not `www` — Mehmet's call,
2026-09-09: shorter, cleaner in shares; `www.thebluesland.com` becomes a legacy host like
onrender.com, not a second identity).

Two hostnames become permanent 301 redirect sources to the canonical origin, path and query string
preserved: `thebluesland.onrender.com` (Render's own subdomain — stays reachable forever, Render
does not remove it when a custom domain is added) and `www.thebluesland.com`. Both must also be
added to `AllowedHosts` (host filtering would otherwise reject them with 400 before the redirect
middleware ever runs) and to Render's Custom Domains for this service (otherwise DNS/TLS never
reaches the app for `www` at all).

A hardcoded list is the right size here, not a new config surface: this is a one-time, permanently-
known migration, not a recurring need, and every existing config knob in `WebHostFactory` (US-014
`Diagnostics:CacheHealthKey`, `ConnectionStrings:SpotifyPlaylistCache`) exists because its value is
either secret or plausibly different per environment — neither is true of "which domain used to be
thebluesland's").

## Acceptance criteria

- [x] A request with `Host: thebluesland.onrender.com` or `Host: www.thebluesland.com` receives a
      `301 Moved Permanently` to the same path and query string under the canonical origin
      (`Site:PublicOrigin`), not a 400 and not a 200.
- [x] The canonical host itself is unaffected: a normal request still returns 200, no redirect loop.
- [x] An unrecognized host (already covered by `SecurityPerformanceRegressionTests`) still gets 400 —
      the legacy hosts are the *only* addition to the allowlist.
- [x] `.github/render.yaml` documents the new `Site__PublicOrigin` env var Mehmet must set by hand
      in the Render Dashboard (`sync: false`, same pattern as every other manual env var there).

## Manual steps (Mehmet, code-external)

1. Render Dashboard → this service → Settings → Custom Domains → add both `thebluesland.com` and
   `www.thebluesland.com`, and add the DNS records Render's UI shows for each at the domain
   registrar. Render auto-provisions the TLS certificate once DNS resolves.
2. Render Dashboard → Environment → add `Site__PublicOrigin` = `https://thebluesland.com`.
3. Verify `https://thebluesland.com` loads correctly *before* relying on the redirect - the redirect
   code ships in this PR regardless of whether the domain is attached yet, so it is dormant (the
   default `Site:PublicOrigin` stays `https://thebluesland.onrender.com`, under which
   `thebluesland.onrender.com` is the canonical host, not a legacy one - see the acceptance
   criterion above) until step 2 is done.

## Out of scope

- `www` as canonical instead of apex (Mehmet's decision, made 2026-09-09; revisit only if he asks).
- Ever removing the `thebluesland.onrender.com` redirect - Render's own subdomain persists
  indefinitely, so there is no future point where dropping it is required, and keeping it costs
  nothing.
- Updating `tests/TheBluesland.E2ETests/SmokeTests.cs`'s hardcoded `thebluesland.onrender.com` URLs
  to the new domain - `HttpClient` follows redirects by default, so those tests keep passing
  unchanged and additionally exercise the new redirect on every run; revisit only if that smoke
  suite's intent shifts from "the live site works" to "the canonical URL is now onrender.com-free".
