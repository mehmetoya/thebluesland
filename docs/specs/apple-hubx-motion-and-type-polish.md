# Spec: Apple/hubx motion and typography polish

Follow-up to `docs/specs/design-tokens-and-home-pilot.md` and
`docs/specs/design-tokens-rollout-remaining-pages.md` (both shipped, approved). Mehmet asked to
also match apple.com/hubx.co's transition effects and typography feel. Resolved with Mehmet via
AskUserQuestion (2026-09-09): **headings stay serif** (`--font-serif` unchanged - this is the
editorial/"late-night record room" signature from spec 10.1, not up for revision here); only the
body font stack, heading weight/tracking, and motion get the Apple/hubx treatment.

## Objective

Two independent, additive changes - no color, layout, or structural change:

1. **Typography polish**: bring `--font-sans` closer to Apple's actual system-font stack, and give
   the two big heading selectors (`.brand-intro h1`, `.playlist-detail h1`) the tighter
   letter-spacing and confident weight that reads as "Apple-caliber," without changing the font
   family itself.
2. **Motion polish**: this codebase already has exactly the right pattern in one place -
   `.playlist-card-link:hover .playlist-card` uses an unconditional `transition:` plus a
   `transform` that only activates inside `@media (prefers-reduced-motion: no-preference)`. Extend
   that same pattern to the interactive elements that currently snap instead of transition:
   `.collection-link` (no hover state at all today) and every plain color-change hover
   (`a:hover`, nav links, buttons) that currently has no `transition`.

## Tech Stack / Commands

No change. `dotnet build TheBluesland.slnx`, `dotnet test TheBluesland.slnx`,
`npm run build:css` (from `src/TheBluesland.Web`), local verification via `dotnet run` + curl as
established in the prior two specs.

## Typography polish - concrete targets

```css
/* Before */
--font-sans: ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;

/* After - -apple-system/BlinkMacSystemFont lead (the literal Apple/Chrome-on-macOS system-font
   trigger), ui-sans-serif/system-ui kept as fallback, Helvetica/Arial added as a further fallback
   before the generic sans-serif keyword */
--font-sans: -apple-system, BlinkMacSystemFont, ui-sans-serif, system-ui, "Segoe UI", Roboto,
  Helvetica, Arial, sans-serif;
```

- `.brand-intro h1`: add `font-weight: 700;` and `letter-spacing: -0.02em;` (tight tracking on
  large display type - the Apple hallmark - keeps the serif family, just tightens and firms it up).
- `.playlist-detail h1`: same two additions (`font-weight: 700; letter-spacing: -0.02em;`) - same
  visual family as the hero heading, one step down in size (already `--font-size-2xl`, unchanged
  here).
- Do not touch `--font-serif`, `.collection-group h2`, `.catalogue h2`, or any body-text selector -
  this is scoped to the two hero-scale headings and the body font stack only.

## Motion polish - concrete targets

Match the existing `.playlist-card-link` pattern exactly: an unconditional `transition:` property
list, with any `transform` gated behind `@media (prefers-reduced-motion: no-preference)` exactly
like the existing block at the `.playlist-card-link:hover .playlist-card` rule - copy that
structure, don't invent a new one.

- `.collection-link`: add `transition: background-color 0.15s ease, border-color 0.15s ease,
  transform 0.15s ease;` to the base rule, then (inside a `prefers-reduced-motion: no-preference`
  block) `.collection-link:hover { transform: translateY(-2px); }`, plus an unguarded
  `.collection-link:hover { border-color: var(--color-border-strong); }` - this mirrors
  `.playlist-card-link:hover .playlist-card` exactly, just applied to the collection grid, which
  has no hover feedback at all today.
- Add `transition: color 0.15s ease;` (matching the established 0.15s/ease timing used everywhere
  else in this file) to the base rule of every selector below that currently changes `color` on
  `:hover` with no transition - this is a color-only transition, not motion/transform, so per this
  file's own existing convention (see `.playlist-card-link`'s background-color/border-color
  transitions, which are also unguarded) it does **not** need the reduced-motion media query:
  - `a:hover` (global link default)
  - `.site-wordmark:hover`
  - `.site-nav a:hover`
  - `.site-footer-nav a:hover`
  - `.filter-dropdown > summary:hover`
  - `.filter-apply:hover`
  - `.filter-clear:hover`
  - `.load-more:hover`
  - `.breadcrumb a:hover`
  - `.open-in-spotify:hover`

## Code Style

Follow the file's own established pattern exactly - do not introduce a new transition timing,
easing, or media-query structure:

```css
/* Existing pattern this spec extends - copy this shape */
.playlist-card {
  transition: background-color 0.15s ease, border-color 0.15s ease, transform 0.15s ease;
}

@media (prefers-reduced-motion: no-preference) {
  .playlist-card-link:hover .playlist-card {
    transform: translateY(-2px);
  }
}

.playlist-card-link:hover .playlist-card {
  background-color: var(--color-bg-elevated-hover);
  border-color: var(--color-border-strong);
}
```

## Testing Strategy

Pure CSS - no new unit-testable behavior. Keep `dotnet test TheBluesland.slnx` green.

## Boundaries

- **Always:** every new `transform`-based motion goes inside the existing
  `@media (prefers-reduced-motion: no-preference)` pattern - never apply `transform` unconditionally;
  verify HomePage, `/collections`, and a playlist detail page locally (curl) before reporting done.
- **Ask first:** touching `--color-*` tokens; touching `--font-serif` or any heading's font-family;
  adding a JS-driven animation, scroll-trigger, or `IntersectionObserver` - this project's filter UI
  and general presentation are deliberately zero-JS (spec 12.3); CSS-only transitions fit that
  constraint, script-driven ones do not.
- **Never:** add a web font / font-loading dependency (Apple's actual SF Pro is licensed for Apple
  platforms only, not embeddable - matching their system-font *stack* is the correct approach here,
  not trying to load SF Pro itself); change any non-hover, non-heading selector.

## Success Criteria

- [ ] `--font-sans` updated to the new stack; `--font-serif` unchanged.
- [ ] `.brand-intro h1` and `.playlist-detail h1` have `font-weight: 700` and
      `letter-spacing: -0.02em`, same font-family as before.
- [ ] `.collection-link` has hover feedback (lift + border-color change) matching
      `.playlist-card-link`'s existing pattern, correctly gated for `transform`.
- [ ] Every listed hover-color selector has a `transition: color 0.15s ease` (or equivalent combined
      transition list) so the color change animates instead of snapping.
- [ ] `dotnet build`/`dotnet test` green; all pages verified rendering locally with no server error.

## Open Questions

None blocking - font family and color palette are explicitly out of scope per Mehmet's answer above.
