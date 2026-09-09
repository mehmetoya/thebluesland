# Spec: Design tokens rollout - collections, playlist detail, shared chrome

Modules: `collections-visual-upgrade`, `playlist-detail-visual-upgrade`,
`shared-chrome-visual-upgrade` (the 3 remaining modules from the capability map approved
2026-09-09, after `design-tokens` + `home-visual-upgrade` - see
`docs/specs/design-tokens-and-home-pilot.md` - shipped and merged, Mehmet approved the pilot: "tüm
sayfalara uyarla").

No new design decision here - the scale (`--space-1..8`, `--font-size-xs..display`) and direction
(bigger confident headings, more generous spacing, less border chrome, same dark palette) are
already set and approved. This is systematic rollout to the pages the pilot didn't touch, bundled
into one spec because none of the three needs its own separate judgment call.

## Objective

Apply the existing design tokens (already in `app.css`'s `:root`, do not add new ones unless a
genuinely new size is needed and none of the 8 spacing / 8 type steps fits) to every remaining
page: `/collections`, `/collections/{slug}`, `/playlists/{slug}`, the site header, the site footer,
and `/about`, `/privacy`, `/terms`.

**Already inherited for free, verify but do not re-implement:** `CollectionsPage.razor` and
`CollectionPage.razor` both reuse the `.brand-intro`/`.brand-intro h1` classes HomePage uses, so
they already picked up the pilot's `--font-size-display` heading and `--space-8` spacing when that
PR merged. Confirm this looks right locally; do not add a second, separate hero treatment for them.

## Tech Stack / Commands

Same as the pilot - no change. `dotnet build TheBluesland.slnx`, `dotnet test TheBluesland.slnx`,
`npm run build:css` (from `src/TheBluesland.Web`), and local verification via
`PlaylistContent__Directory=<repo>/content/playlists dotnet run --project src/TheBluesland.Web
--no-launch-profile --urls http://localhost:5299` + curl - the main session does this itself now.

## Concrete targets per module

### `shared-chrome-visual-upgrade` (`SiteHeader.razor`, `SiteFooter.razor`)

- `.site-header`: drop `border-bottom` (matches the pilot's "less border chrome" call on
  `.filter-navbar` - the header already sits visually distinct without an outline).
- `.site-header-inner`: `padding: 1.25rem 1.5rem` -> `var(--space-4) var(--space-4)`.
- `.site-footer`: `margin-top: 3rem` -> `var(--space-7)` (more separation from page content above).
- `.site-footer-inner`: `padding: 2rem 1.5rem` -> `var(--space-5) var(--space-4)`.
- `.site-wordmark`/`.site-nav`: tokenize the font-size references (`--font-size-md` /
  `--font-size-sm` respectively) but do **not** change their actual rendered size - these are
  functional UI chrome, not editorial headline moments; the point here is consistency (one source
  of truth for "small text" sizing), not making the nav bigger.

### `playlist-detail-visual-upgrade` (`PlaylistDetailView.razor`)

- `.playlist-detail h1`: `font-size: 2rem` -> `var(--font-size-2xl)` (3rem - a confident page
  headline, one step below HomePage's own `--font-size-display` since a playlist title is
  page-specific content, not the site's brand hero); `margin-bottom: 0.75rem` -> `var(--space-4)`.
- `.playlist-detail .summary`, `.curator-note`: tokenize `font-size: 1.0625rem` to
  `var(--font-size-md)` - same rendered size, now token-sourced.
- `.tags`: `margin: 1rem 0 1.5rem` -> `var(--space-3) 0 var(--space-4)`.
- `.breadcrumb`: `margin-bottom: 1rem` -> `var(--space-3)`.
- Leave `.cover-image`, `.tags li`, `.open-in-spotify`, `.related-playlists` alone unless a value
  there visibly clashes once the above lands - this module's scope is the page's textual hierarchy
  and spacing, not the cover art or track-count chrome.

### `collections-visual-upgrade` (`CollectionsPage.razor`, `CollectionPage.razor`)

- Confirm the inherited `.brand-intro` hero (see Objective) renders correctly on both pages - no
  code change expected here, just verification.
- `.collection-group`: `margin-bottom: 2rem` -> `var(--space-6)` (more air between the Genre/
  Occasion/Mood/Era sections on `/collections`).
- `.collection-group h2`: tokenize `font-size: 1.375rem` to `var(--font-size-lg)` (same value);
  `margin-bottom: 1rem` -> `var(--space-4)`.
- `.collection-links`: `gap: 1rem` -> `var(--space-4)`.
- `.collection-link`: `padding: 1.25rem` -> `var(--space-4)`. **Keep its `border`** - unlike
  `.filter-navbar`/`.site-header`, these cards have no background fill, so the border is load-bearing
  for the card boundary, not decorative chrome; removing it would make the grid unreadable.
- `.collection-link h3`: leave the literal `1.125rem` as-is if it doesn't cleanly match a token step
  - don't force a mismatched token value onto something that isn't actually changing just to
    "tokenize everything"; a real behavior/value change is the bar, not literal token usage everywhere.

## Testing Strategy

Same as the pilot: no new unit-testable behavior, this is CSS/markup only. Keep
`dotnet test TheBluesland.slnx` green - nothing here should change server-rendered structure enough
to break existing `PlaylistDetailPage`/`CollectionPage`/`CollectionsPage` assertions.

## Boundaries

- **Always:** every value you touch becomes a token unless the "don't force a mismatch" exception
  above applies; verify each of the 3 page groups renders locally (curl) before reporting done, not
  just the ones you changed most - a regression in an untouched page from a shared selector change
  (e.g. `.site-header` affects every page) is still your responsibility to catch.
- **Ask first:** touching `--color-*` tokens; touching `HomePage.razor`/`.brand-intro`/`.catalogue`/
  `.filter-navbar` (already done, out of scope here - don't re-touch the pilot); adding a new token
  not already in the `:root` scale.
- **Never:** add JS/interactivity anywhere; change routing, `PlaylistFilter`, `PlaylistCollections`
  data, or any `.cs` file - this is CSS-only across `SiteHeader.razor`, `SiteFooter.razor`,
  `PlaylistDetailView.razor`, `app.css`.

## Success Criteria

- [ ] `.site-header`, `.site-footer`, `.playlist-detail h1`/`.summary`, `.curator-note`, `.tags`,
      `.breadcrumb`, `.collection-group`, `.collection-links` all reference tokens per the targets above.
- [ ] `dotnet build` and `dotnet test` both green.
- [ ] All of `/`, `/collections`, `/collections/{any slug}`, `/playlists/{any slug}`, `/about` verified
      rendering locally with no server error (curl or equivalent) - `/` because shared-chrome changes
      touch it too.
- [ ] A brief before/after note per module in the handoff, same as the pilot.

## Open Questions

None blocking - this is rollout of an already-approved scale, not new design work. If something
looks visibly wrong once applied, flag it rather than guessing a fix; Mehmet's review is still the
final judgment call on craft quality, same as the pilot.
