# Spec: Design tokens + HomePage visual pilot

Modules: `design-tokens` → `home-visual-upgrade` (capability map approved by Mehmet, 2026-09-09).
Written together because `design-tokens` alone produces no visible change - the pilot is what lets
Mehmet actually judge the direction before it rolls out to `collections-visual-upgrade`,
`playlist-detail-visual-upgrade`, and `shared-chrome-visual-upgrade` (later, separate specs).

## Objective

Raise TheBluesland's visual craft quality toward apple.com/hubx.co's execution discipline - bigger,
more confident typography for headline moments; far more generous spacing/vertical rhythm; less
border/box chrome - **without changing the existing dark "late-night record room" color identity**
(spec section 10.1-10.2: navy/charcoal background, warm off-white text, one amber accent - ADR-backed,
unchanged). Today's CSS (`app.css`) hardcodes a `font-size`/`padding`/`margin`/`gap` value per
selector with no shared scale; the largest heading anywhere is 2.5rem and most spacing sits under
2rem. This spec introduces a real type/spacing scale as CSS custom properties, then applies it to
one page (HomePage) as a pilot Mehmet can review before further rollout.

## Tech Stack

No change. Plain CSS custom properties in the existing `src/TheBluesland.Web/Styles/app.css` -
Tailwind is already wired for utility classes elsewhere, but this file's own `:root` tokens are the
right layer for a site-wide scale (matches the existing `--color-*`/`--font-serif`/`--font-sans`
pattern already there).

## Commands

```
Build: dotnet build TheBluesland.slnx
Test:  dotnet test TheBluesland.slnx
Run:   dotnet run --project src/TheBluesland.Web --no-launch-profile --urls http://localhost:5299
       (content directory must be set explicitly when not run from the project's own working
       directory assumption - see "Local verification" below)
```

## Project Structure

```
src/TheBluesland.Web/Styles/app.css                    -> new :root tokens + HomePage selector updates
src/TheBluesland.Web/Components/Pages/HomePage.razor    -> class names only if a new wrapper/section
                                                            is genuinely needed; prefer reusing existing
                                                            classes (.brand-intro, .filter-navbar,
                                                            .catalogue) over renaming them
```

## Design tokens (add to `:root`, alongside the existing `--color-*`/`--font-*` block)

Starting values - a reasonable scale grounded in the current file's numbers, not a rigid contract.
Adjust during implementation if something looks visibly wrong; the pilot review is what confirms
the final numbers, not this table.

```css
/* Spacing scale (8px grid) */
--space-1: 0.5rem;    /* 8px  - tight (chip/pill padding) */
--space-2: 0.75rem;   /* 12px - existing default, unchanged */
--space-3: 1rem;      /* 16px */
--space-4: 1.5rem;    /* 24px */
--space-5: 2rem;      /* 32px - catalogue grid gap target */
--space-6: 3rem;      /* 48px */
--space-7: 4rem;      /* 64px */
--space-8: 6rem;      /* 96px - major section separation target */

/* Type scale */
--font-size-xs: 0.75rem;     /* 12px - meta/fine print, unchanged from today */
--font-size-sm: 0.875rem;    /* 14px - secondary text, unchanged from today */
--font-size-base: 1rem;      /* 16px - body, unchanged from today */
--font-size-md: 1.0625rem;   /* 17px - lead paragraph, matches existing .brand-intro p */
--font-size-lg: 1.375rem;    /* 22px - card/subsection titles */
--font-size-xl: 2rem;        /* 32px - section headings */
--font-size-2xl: 3rem;       /* 48px - page headings */
--font-size-display: 4.5rem; /* 72px - NEW: hero/display size, nothing this large exists today */
```

## Code Style

Existing token pattern to match exactly (from the current file):

```css
:root {
  --color-bg: #12141b;
  --font-sans: ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
}
```

Applying a token to a selector (example - `.brand-intro h1` today vs. the pilot target):

```css
/* Before */
.brand-intro h1 {
  font-size: 2.5rem;
  margin-bottom: 0.75rem;
}

/* After */
.brand-intro h1 {
  font-size: var(--font-size-display);
  margin-bottom: var(--space-4);
}
```

Every `font-size`/`padding`/`margin`/`gap` value touched on HomePage in this pass must reference a
token, not a new hardcoded number - that consistency is the entire point of this module.

## HomePage pilot - concrete targets

- `.brand-intro h1`: `var(--font-size-display)` (was 2.5rem) - the confident, oversized headline moment.
- `.brand-intro`: `margin-bottom: var(--space-8)` (was 2.5rem) - real air before the filter navbar.
- `.filter-navbar`: keep fully functional (zero-JS `<details>`/query-string pattern, spec 12.3 - do
  not add interactivity). Reduce chrome: consider dropping or lightening `border` per the "less
  border/box chrome" direction; use `var(--space-4)` for padding.
- `.catalogue`: `gap: var(--space-5)` (was 1.25rem).
- `.catalogue h2`: `font-size: var(--font-size-lg)` (was 1.125rem), `margin-bottom: var(--space-4)`.
- Overall: increase vertical separation between the navbar and the catalogue grid using a token
  (`var(--space-7)` or `var(--space-8)` on whichever side reads better) - there is currently almost
  none.
- `PlaylistCard` (used inside `.catalogue`) - leave as-is unless it visibly clashes with the new
  scale; this pilot's scope is the page shell (hero, nav, grid rhythm), not the card component.

## Local verification (new capability, use it)

The main session can now run the app and check it directly - `dotnet run`/`curl` to
`localhost`/`127.0.0.1` no longer need approval. `content/playlists` resolves relative to the
process's working directory, which `dotnet run` sets to the project folder, not wherever you invoked
it from - pass the real path explicitly:

```bash
cd src/TheBluesland.Web
PlaylistContent__Directory=/Users/mehmetoya/SpotifyPlaylists/content/playlists \
  dotnet run --no-launch-profile --urls http://localhost:5299
```

Then `curl -s http://localhost:5299/ | grep -o '<title>[^<]*</title>'` (or similar) to confirm it
renders. This does not replace Mehmet's own visual review - CSS craft quality is a judgment call a
curl request cannot make - but it does prove the page still renders, has no server error, and that
specific values actually landed in the served HTML/CSS.

## Testing Strategy

This is a pure CSS/markup change - no new unit-testable behavior. Existing test suite must stay
green (nothing here should change server-rendered structure enough to break `PlaylistDetailPage`/
`HomePage` assertions in `tests/TheBluesland.UnitTests` or the Playwright smoke tests in
`tests/TheBluesland.E2ETests`). Run both once before reporting done.

## Boundaries

- **Always:** every new hardcoded pixel/rem value on a touched selector becomes a token instead;
  run `dotnet build`/`dotnet test` before reporting done; verify HomePage actually renders locally
  (see above) before reporting done - a visual change that was never looked at is not done.
- **Ask first:** touching any file outside `app.css`/`HomePage.razor`; touching color tokens
  (`--color-*`) at all; adding a CSS framework or build-step dependency beyond what's already wired.
- **Never:** change `PlaylistFilterCriteria`/`PlaylistFilter`/routing/query-string behavior - this is
  visual-only; add JS/interactivity to the filter navbar (spec 12.3); touch any other page
  (`PlaylistDetailPage`, `CollectionPage`, `CollectionsPage`, About/Privacy/Terms) - those are
  separate, later modules.

## Success Criteria

- [ ] `:root` in `app.css` has the new `--space-*`/`--font-size-*` tokens alongside the existing
      `--color-*`/`--font-*` ones.
- [ ] `.brand-intro h1`, `.brand-intro`, `.filter-navbar`, `.catalogue`, `.catalogue h2` all
      reference tokens for their font-size/spacing values (no new hardcoded numbers).
      Playlist card titles/text stay as-is.
- [ ] `dotnet build` and `dotnet test` both green.
- [ ] HomePage verified rendering locally (curl or equivalent) with no server error.
- [ ] A brief before/after description (or the actual rendered HTML snippet for the changed
      selectors) is included in the handoff so Mehmet can judge the pilot without pulling the branch.

## Open Questions

None blocking. The exact token values above are a starting point Mehmet can redirect after seeing
the pilot - that's the reason this rolls out one page at a time instead of everywhere at once.
