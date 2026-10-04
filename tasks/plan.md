# Implementation Plan: Playlist search

## Overview

Add a native, URL-backed search field to the home-page catalogue. Search ranks published
playlists using style/mood words found in editorial text and tags, while preserving existing
filter, order, and progressive-pagination behavior.

## Architecture decisions

- Keep matching as a pure helper under `TheBluesland.Web.Content`; no service, endpoint, package,
  or database work is needed.
- Use the existing home-page GET form and static SSR query binding, so search composes with
  filters and survives reloads without JavaScript.
- Rank by distinct query-term coverage and preserve source order for ties.

## Task list

### Phase 1: Search matching

- [ ] Task 1: Add deterministic playlist search and focused unit tests.
  - Acceptance: blank query returns the original catalogue order; title, summary, and taxonomy
    tokens match case-insensitively; partial matches rank by matched query-term count with stable
    ties.
  - Verify: focused `PlaylistSearchTests`; solution build.
  - Files: `PlaylistSearch.cs`, `PlaylistSearchTests.cs`.

### Checkpoint: Search matching

- [ ] Pure search tests pass and the solution builds.

### Phase 2: Catalogue experience

- [ ] Task 2: Add the accessible GET search field to the home page and compose it with filters,
  result messaging, and pagination.
  - Acceptance: query survives reload and "Show more"; filters still compose; empty/no-match
    states are correct; no JavaScript is needed.
  - Verify: focused `HomePageSearchIntegrationTests` and existing home-page filter/pagination tests.
  - Files: `HomePage.razor`, `app.css`, `HomePageSearchIntegrationTests.cs`.

### Checkpoint: End-to-end

- [ ] Search, filter composition, and pagination are verified through the HTTP-rendered page.

### Phase 3: Final verification

- [ ] Task 3: Run build, focused and full tests, and browser accessibility/responsive checks.
  - Acceptance: solution builds; tests pass (environmental limitations documented); search works
    via keyboard and at mobile/desktop widths.
  - Verify: commands in `docs/specs/playlist-search.md` and local browser check.
  - Files: no implementation changes unless verification reveals a defect.

### Checkpoint: Complete

- [ ] All success criteria in `docs/specs/playlist-search.md` are met.

## Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Search could disturb current filtering or pagination | High | Keep the query in the same GET form and explicitly test filter composition and "Show more". |
| Broad terms may produce many weak results | Medium | Rank by distinct query-term coverage and preserve editorial catalogue order for ties. |
| Static SSR could make a client-only search control inert | High | Use a native labelled GET form; verify server-rendered HTTP behavior. |

## Open questions

None; scope and behavior were confirmed during the interview.
