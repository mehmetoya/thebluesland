# Implementation Plan: Era and mood collection pages

Spec: `SPEC-era-mood-collection-pages.md` (approved by Mehmet, 2026-09-09).

## Overview

Extend the existing `PlaylistCollections` mechanism (already fully wired to routing, sitemap,
breadcrumbs, canonical URLs) with 9 new entries — 5 moods, 4 eras excluding `mixed-era` — and close
the one real gap it has today: `CollectionPage.razor` renders no JSON-LD for the playlists it lists.
No new routes, page components, or taxonomy changes.

## Architecture Decisions

- `Dimension` is a new small enum (`Genre | Occasion | Mood | Era`) on the `PlaylistCollection`
  record, used only by `CollectionsPage.razor` to group the index. It carries no filtering logic —
  `Criteria` still does that, unchanged.
- JSON-LD for a collection's playlist list is a new `StructuredDataBuilder` method (`CollectionPage`
  + `ItemList`), separate from the existing `BuildCollectionPage(PlaylistContent, ...)` which
  describes a single playlist detail page, not a list — reusing that method's name for a different
  shape would be confusing; the plan uses `BuildCollectionPageForPlaylists` as a working name,
  backend-dev may rename if a clearer one emerges during implementation.
- All 9 new entries are added as plain data literals in `PlaylistCollections.All`, matching the
  existing 3 exactly — no loop/generator over `PlaylistTaxonomy`, per the spec's rejection of
  unnecessary abstraction for 9 known, fixed values.

## Task List

### Phase 1: Foundation

- [x] Task 1: Add `Dimension` to `PlaylistCollection`

### Checkpoint: Foundation
- [x] `dotnet build` clean
- [x] Existing `PlaylistCollections`/`SitemapGenerator`/`CollectionsPage` tests still pass unchanged

### Phase 2: JSON-LD for collection pages

- [x] Task 2: Add `StructuredDataBuilder.BuildCollectionPageForPlaylists(...)`
- [x] Task 3: Wire `CollectionPage.razor` to the new builder

### Checkpoint: JSON-LD path complete
- [x] `dotnet test` green
- [ ] Manual check: view source on `/collections/blues` (or any existing collection) locally, confirm
      a `<script type="application/ld+json">` block with `@type: "CollectionPage"` and an `ItemList`
      of that collection's playlists — not verified this session (`dotnet run`/`curl` are
      permission-gated here); CI's `playwright-smoke` job covers the real page before merge

### Phase 3: New collections + index grouping

- [x] Task 4: Add the 9 new mood/era collection entries
- [x] Task 5: Group `CollectionsPage.razor`'s index by `Dimension`

### Checkpoint: Complete
- [x] `dotnet build` and `dotnet test` both green
- [x] `PlaylistCollections.All` has 12 entries; `SitemapGenerator` test confirms 12 `/collections/*`
      sitemap URLs
- [x] `/collections` renders four grouped sections (Genre, Occasion, Mood, Era)
- [x] All acceptance criteria in `SPEC-era-mood-collection-pages.md`'s Success Criteria are met
- [x] Ready for code-review-and-quality (`/review`)

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| 9 near-identical entries copy-pasted with a wrong `Criteria` value (e.g. an era entry accidentally filtering on the wrong era) | Medium — a collection page would silently show the wrong playlists | Task 4's acceptance criteria requires one test per new entry asserting its `Criteria` selects exactly its own value, not just "the page renders" |
| New JSON-LD breaks existing `CollectionPage.razor` rendering for the 3 existing collections | Low — additive `<script>` tag, but still worth a regression check | Task 3's verification step explicitly re-checks an existing collection (`/collections/blues`), not only a new one |

## Open Questions

None — spec is fully resolved, this plan does not introduce new decisions.
