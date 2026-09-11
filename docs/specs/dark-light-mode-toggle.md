# Spec: Dark/light theme toggle

Mehmet wants a light mode option on the site. Dark is this site's ADR'd brand identity ("late-night
record room", spec section 10.1/10.2, deliberately preserved through the 2026-09-09/10 Apple/hubx
craft-polish work) - this is not a redesign or a replacement of that identity, it is an *additional*
mode a visitor can opt into.

Mehmet confirmed via AskUserQuestion (2026-09-11): **dark stays the default for every first-time
visitor, regardless of the visitor's OS/browser color-scheme preference.** Light mode is opt-in only,
via a manual toggle, remembered after that. This deliberately does not follow the more common
"respect `prefers-color-scheme` on first visit" pattern - the whole point is that most visitors
should see the brand's intended dark identity by default; system preference is not consulted.

## Objective

Add a header toggle button that switches the site between the existing dark theme and a new light
theme. First-time visitors (no stored preference) always get dark. Once a visitor picks a mode, it's
remembered (`localStorage`) and applied instantly on every subsequent page load, with no visible
flash of the wrong theme.

## Design

### 1. Token architecture (`src/TheBluesland.Web/Styles/app.css`)

The existing `@theme` block (lines 8-31) already defines every color as a semantic custom property
(`--color-bg`, `--color-bg-elevated`, `--color-bg-elevated-hover`, `--color-border`,
`--color-border-strong`, `--color-text`, `--color-text-muted`, `--color-accent`,
`--color-accent-strong`, `--color-accent-contrast`, `--color-secondary`) - confirmed by grep, every
single color reference in this file already goes through one of these `var(--color-*)` tokens, never
a hardcoded hex. This is exactly what makes a second theme tractable without touching component
selectors: only the token *values* change per theme, not the ~80 places that consume them.

Add a light-theme override block, scoped to `:root[data-theme="light"]` (higher specificity than the
plain `:root` Tailwind's `@theme` compiles to, so it cleanly overrides), placed after the `@theme`
block - `@theme` itself is Tailwind's default-token declaration, not the place for a conditional
variant. Also make `html { color-scheme: dark; }` (line 70-72) conditional:
`html[data-theme="light"] { color-scheme: light; }` alongside the existing dark default, so native
form controls/scrollbars follow the active theme.

**Starting palette** (a starting point to implement and then verify for contrast, not a rigid
contract - mirrors this file's own existing design-tokens comment convention). Intent: a "sunlit
record store" counterpart to the dark "late-night record room" - warm cream/parchment, never stark
white; warm dark brown text, never pure black; same emotional warmth as the dark palette, not a
generic inverted grayscale:

```css
:root[data-theme="light"] {
  --color-bg: #f7f1e6;
  --color-bg-elevated: #efe6d4;
  --color-bg-elevated-hover: #e6dac2;
  --color-border: rgb(42 34 22 / 10%);
  --color-border-strong: rgb(42 34 22 / 20%);

  --color-text: #2a2216;
  --color-text-muted: #6b5f4e;

  --color-accent: #b5702e;       /* darkened from #d99a4e - the dark-mode amber likely fails AA
                                     text contrast against a light cream background; verify below. */
  --color-accent-strong: #9c5f26;
  --color-accent-contrast: #fdf8ef;

  --color-secondary: #3f6386;    /* darkened from #6f93b8 for the same reason. */
}
```

**Required, not optional:** verify WCAG AA contrast (4.5:1 normal text, 3:1 large text/UI
components) for every token pair actually used as foreground-on-background in this file (e.g.
`--color-text` on `--color-bg`, `--color-accent` on `--color-bg`, `--color-accent-contrast` on
`--color-accent`) for the light palette, adjusting the hex values above as needed - don't ship the
draft values unverified. Use whatever contrast-checking approach is convenient (a small throwaway
script, a browser devtools contrast check, an online calculator) and note the checked ratios in the
PR description; no need to add this as an automated test.

### 2. FOUC-free theme detection (new, small, blocking script)

**Constraint that shapes this whole section:** `WebHostFactory.cs`'s CSP is `script-src 'self'`
and `style-src 'self'` - **no `'unsafe-inline'`, no nonce mechanism exists in this app.** The usual
"tiny inline `<script>` in `<head>`" anti-flash trick is not available here. Every script must be an
external same-origin file, exactly like the existing `wwwroot/js/infinite-scroll.js` and `share.js`
(see their `AssetVersion.For(...)` cache-busting pattern in `App.razor` - reuse it, don't invent a
new asset-loading convention).

Add `wwwroot/js/theme.js`:

- On load (top-level code, not wrapped in a `DOMContentLoaded` listener - this must run
  synchronously, before first paint): read `localStorage.getItem("theme")`. If it's `"light"`, set
  `document.documentElement.setAttribute("data-theme", "light")`. If it's anything else (including
  never-set), do nothing - the CSS's dark defaults already apply with no attribute present, and
  system `prefers-color-scheme` is deliberately never consulted (Mehmet's explicit decision above).
- Also register a `DOMContentLoaded` listener (safe to add even though the script runs before body
  parsing - the listener simply fires later) that finds `.theme-toggle` button(s) and wires a click
  handler: flip `data-theme` between absent/`"dark"` and `"light"` on `<html>`, write the new value
  to `localStorage`, update the button's `aria-pressed`/label. One small file does both jobs; no
  need for two.

**In `App.razor`, this `<script>` tag must have neither `defer` nor `async`, and must be placed
before the `<link rel="stylesheet" ... app.css>` line (currently line 25)** - a deferred script (like
this file's other two) executes only after the whole document parses, which is exactly the flash
this section exists to prevent; only a blocking script placed ahead of the stylesheet guarantees
`data-theme` is set on `<html>` before styles are ever applied. Keep this file as small as possible
(this is deliberately a render-blocking request on every uncached first visit) - no unrelated logic
belongs in it.

### 3. Toggle button (`src/TheBluesland.Web/Components/Shared/SiteHeader.razor`)

Add one `<button type="button" class="theme-toggle" aria-label="Switch to light theme"
aria-pressed="false">...</button>` inside `.site-header-inner`, alongside the existing wordmark/nav.
Two small inline SVG icons (sun/moon, following this project's existing hand-authored-SVG convention;
see `App.razor`'s favicon comment; no icon font, no third-party icon package), toggled via CSS
using the `[data-theme]` attribute on `<html>` (e.g. `:root:not([data-theme="light"]) .theme-toggle
.icon-sun { display: none; }` and the mirror rule for the moon icon) - no JS-driven icon swap needed,
only the attribute flip from section 2 drives which icon shows.

Style it with the existing token/spacing scale (`--space-*`, `--color-*`), matching
`.site-nav a`'s existing hover/focus treatment (accessible-motion pattern already used throughout
this file: unconditional `transition:` property list, `transform` only inside
`@media (prefers-reduced-motion: no-preference)`).

## Tech Stack / Commands

No new dependency, no new build step. `npm run build:css` (Tailwind), `dotnet build
TheBluesland.slnx`, `dotnet test TheBluesland.slnx` as usual.

## Testing Strategy

- No unit test for the client-side JS itself (this project has no JS test runner, matching
  `infinite-scroll.js`/`share.js`'s existing precedent of being verified manually/via
  `playwright-smoke`, not unit-tested).
- Extend `tests/TheBluesland.UnitTests/Web/WebHostIntegrationTests.cs` (already asserts the rendered
  `<head>`/CSP header) with an assertion that `theme.js` is referenced without `defer`/`async` and
  appears before the stylesheet `<link>` - this is the one thing a passing build won't catch if
  someone "cleans up" the tag later by adding `defer` to match the other two scripts.
- Manual/browser verification (`dotnet run` + curl/browser, permission already granted this
  session): load the site fresh (no localStorage) → confirm dark. Toggle to light → confirm no
  flash on the *next* reload, confirm `localStorage` persisted, confirm native scrollbar/form
  control rendering matches (`color-scheme`). Toggle back to dark → confirm the attribute is
  actually removed/reset, not left as a stale `"dark"` string that a future default change could
  silently mis-handle.

## Boundaries

- **Always:** keep dark as the union of "no stored preference" and "explicitly stored dark" - never
  let system `prefers-color-scheme` influence the initial state, per Mehmet's explicit decision.
  Verify contrast for every new light-mode color pair before calling this done.
- **Ask first:** any change to the CSP (`script-src`/`style-src`) in `WebHostFactory.cs` - if the
  blocking-script approach above turns out to be insufficient for some reason, that's a stop-and-ask,
  not a silent CSP loosening.
- **Never:** add a UI framework/icon package for this; touch the dark palette's existing values
  (this is purely additive); make the toggle depend on Interactive Server/SignalR - it must work on
  the plain static-SSR render, like every other page.

## Success Criteria

- [ ] Fresh visit (no `localStorage`) renders dark, regardless of the browser's OS color-scheme
      setting.
- [ ] Toggling the header button switches the whole page's colors instantly and persists across a
      reload with no visible flash of the other theme.
- [ ] Every light-mode color pair actually used as foreground/background meets WCAG AA (4.5:1
      normal text, 3:1 large text/UI) - ratios noted in the PR.
- [ ] `theme.js` is not `defer`/`async` and sits before the stylesheet `<link>` in `App.razor`,
      covered by a test asserting this.
- [ ] `dotnet build`/`dotnet test` green.
- [ ] CSP unchanged (`script-src 'self'`, `style-src 'self'`, no `'unsafe-inline'`).

## Open Questions

None blocking. If contrast verification forces the accent/secondary hexes further from the dark
palette's family than the starting values above, that's an expected, acceptable outcome of the
"verify, don't assume" requirement - not something to stop and ask about.
