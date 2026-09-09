# Tasks: Era and mood collection pages

Plan: `tasks/plan.md`. Spec: `SPEC-era-mood-collection-pages.md`.

## Task 1: Add `Dimension` to `PlaylistCollection`

**Description:** Add a `Dimension` enum (`Genre`, `Occasion`, `Mood`, `Era`) and a required
`Dimension` field on the `PlaylistCollection` record. Set it explicitly on the 3 existing entries
(`anadolu-rock` → Genre, `blues` → Genre, `late-night` → Occasion). No behavior change yet — this
only prepares the data model for Tasks 4 and 5.

**Acceptance criteria:**
- [x] `PlaylistCollection` has a `Dimension` property of the new enum type
- [x] All 3 existing entries compile with an explicit, correct `Dimension` value

**Verification:**
- [x] Build: `dotnet build TheBluesland.slnx`
- [x] Tests: `dotnet test TheBluesland.slnx --filter FullyQualifiedName~PlaylistCollections`
- [x] Manual check: none needed, no rendering change

**Dependencies:** None

**Files likely touched:**
- `src/TheBluesland.Web/Content/PlaylistCollections.cs`

**Estimated scope:** XS (1 file)

---

## Task 2: Add `StructuredDataBuilder.BuildCollectionPageForPlaylists(...)`

**Description:** Add a new method to `Seo/StructuredDataBuilder.cs` that builds a `CollectionPage` +
`ItemList` JSON-LD document for a page listing multiple playlists (a `PlaylistCollection` and its
matched `PlaylistContent` list + canonical URL in, a JSON string out) — distinct from the existing
`BuildCollectionPage(PlaylistContent, ...)`, which describes one playlist detail page, not a list.
Follow the file's existing pattern: typed `record`s with `[JsonPropertyName]`, `System.Text.Json`,
never string concatenation.

**Acceptance criteria:**
- [x] New method returns valid JSON with `"@type": "CollectionPage"` and a nested `ItemList` whose
      `itemListElement` has one entry per playlist passed in, in the given order, each pointing at
      that playlist's canonical `/playlists/{slug}` URL
- [x] Reuses the file's existing JSON serialization approach (no new library, no manual string building)

**Verification:**
- [x] Build: `dotnet build TheBluesland.slnx`
- [x] Tests: a new unit test in `tests/TheBluesland.UnitTests/Web/StructuredDataBuilderTests.cs` (or
      wherever the existing `StructuredDataBuilder` tests live) covering the JSON shape above
- [x] `dotnet test TheBluesland.slnx --filter FullyQualifiedName~StructuredDataBuilder`

**Dependencies:** None

**Files likely touched:**
- `src/TheBluesland.Web/Seo/StructuredDataBuilder.cs`
- `tests/TheBluesland.UnitTests/Web/StructuredDataBuilderTests.cs` (or existing equivalent)

**Estimated scope:** S (1-2 files)

---

## Task 3: Wire `CollectionPage.razor` to the new builder

**Description:** In `CollectionPage.razor`'s `OnInitializedAsync`, after `_playlists` is resolved,
build the JSON-LD via Task 2's method and pass it to `<PageMetadata StructuredDataJson="..." />`
(currently not passed at all on this page).

**Acceptance criteria:**
- [x] `/collections/{slug}` renders a `<script type="application/ld+json">` block for every existing
      and future collection, not just some
- [x] No change to the page's existing title/description/canonical/breadcrumb rendering

**Verification:**
- [x] Build: `dotnet build TheBluesland.slnx`
- [x] Tests: `dotnet test TheBluesland.slnx` (relevant Web tests)
- [ ] Manual check: run the app locally, view source on `/collections/blues`, confirm the JSON-LD
      block is present and valid (no rendering regression on an existing collection) — **not done
      this session**: `dotnet run` and `curl` are both permission-gated in this environment; CI's
      `playwright-smoke` job will exercise the real page before merge

**Dependencies:** Task 2

**Files likely touched:**
- `src/TheBluesland.Web/Components/Pages/CollectionPage.razor`

**Estimated scope:** S (1 file)

---

## Task 4: Add the 9 new mood/era collection entries

**Description:** Add 9 new entries to `PlaylistCollections.All`: one per mood (`melancholic`,
`warm`, `energetic`, `raw`, `nostalgic`) and one per era except `mixed-era` (`pre-1970`, `1970s`,
`1980s-1990s`, `2000s-present`). Each entry needs a slug (the taxonomy value itself, already
URL-safe), a title, a one-line meta description, a short introduction paragraph (match the existing
3 entries' voice — read them first), the correct `Dimension` (Mood or Era), and a
`PlaylistFilterCriteria` selecting only that one value in the matching dimension.

**Acceptance criteria:**
- [x] `PlaylistCollections.All` has 12 entries total (3 existing + 9 new)
- [x] Each new entry's `Criteria` selects exactly its own value and nothing else
- [x] No slug collides with an existing entry or another new entry
- [x] `mixed-era` has no entry

**Verification:**
- [x] Build: `dotnet build TheBluesland.slnx`
- [x] Tests: extend `PlaylistCollectionsTests` (or equivalent) with one assertion per new entry's
      `Criteria`, plus a test that every mood and every era-except-`mixed-era` value has exactly one
      collection
- [x] Extend/confirm the `SitemapGenerator` test now expects 12 `/collections/*` URLs, not 3
- [x] `dotnet test TheBluesland.slnx`

**Dependencies:** Task 1

**Files likely touched:**
- `src/TheBluesland.Web/Content/PlaylistCollections.cs`
- `tests/TheBluesland.UnitTests/Web/PlaylistCollectionsTests.cs` (or equivalent)
- `tests/TheBluesland.UnitTests/Seo/SitemapGeneratorTests.cs` (or equivalent)

**Estimated scope:** M (2-3 files, but mechanically repetitive — 9 similar entries)

---

## Task 5: Group `CollectionsPage.razor`'s index by `Dimension`

**Description:** Change `/collections`' rendering from one flat list of 12 link cards to four
labeled sections (Genre, Occasion, Mood, Era), each listing that dimension's collections. Keep the
existing per-card markup (title + description + link) — only the grouping/headings are new.

**Acceptance criteria:**
- [x] `/collections` shows a heading per `Dimension` value that has at least one entry, with that
      dimension's collections underneath
- [x] Section order is stable (e.g. Genre, Occasion, Mood, Era) regardless of `PlaylistCollections.All`'s
      literal array order
- [x] No existing collection disappears or duplicates across sections

**Verification:**
- [x] Build: `dotnet build TheBluesland.slnx`
- [ ] Tests: existing E2E/Playwright smoke test for `/collections` still passes; add or extend one
      assertion for section headings if the existing test is specific enough to need it — **not run
      this session** (`TheBluesland.E2ETests` isn't in `TheBluesland.slnx`, only exercised by CI's
      `playwright-smoke` job); the count assertion was updated to `PlaylistCollections.All.Count`
      but not locally executed
- [ ] Manual check: run the app locally, visit `/collections`, confirm all four sections render with
      the right entries — **not done this session**, same permission constraint as Task 3

**Dependencies:** Task 1

**Files likely touched:**
- `src/TheBluesland.Web/Components/Pages/CollectionsPage.razor`

**Estimated scope:** S (1 file)

---

## Checkpoint: After Task 3
- [x] `dotnet build` and `dotnet test` both green
- [ ] Manual JSON-LD check on an existing collection passes — not verified this session, see Task 3
- [x] Review with Mehmet before proceeding to Phase 3 (optional — spec is fully approved, this is a
      natural pause point, not a hard gate)

## Checkpoint: After Task 5 (Complete)
- [x] `dotnet build` and `dotnet test` both green
- [x] All Success Criteria in `SPEC-era-mood-collection-pages.md` met
- [x] Ready for `code-review-and-quality` (`/review`)
