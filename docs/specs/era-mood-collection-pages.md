# Spec: Era and mood collection pages

## Objective

Give every mood and non-fallback era value its own indexable, SEO-visible landing page, using the
collection mechanism that already exists for genre/occasion (`PlaylistCollections` +
`/collections/{slug}`) instead of building a new page type. Today only 3 hand-picked collections
exist (`anadolu-rock`, `blues`, `late-night` — 2 genre, 1 occasion); mood and era have zero. This
compounds the AEO/GEO investment already made (FAQ, `MusicPlaylist` schema, About identity,
`thebluesland.com`) by turning 9 more taxonomy values into their own crawlable, linkable pages
instead of only being reachable as a `?mood=`/`?era=` query-string filter on the home page (which
spec section 14 deliberately canonicalises away from being indexed on its own).

Who benefits: a visitor or search engine arriving with an era/mood-specific query ("70s blues
playlists", "energetic playlists") lands on a page that speaks directly to that intent, with its
own title, intro copy, and canonical URL — rather than only the generic home page.

## Scope decisions (resolved 2026-09-09)

- **Coverage:** all 5 moods (`melancholic`, `warm`, `energetic`, `raw`, `nostalgic`) and 4 of the 5
  eras. `mixed-era` is excluded — per spec section 8.5 it is an explicit escape valve for
  multi-decade catalogues, not a listening context, and does not make a coherent landing page.
  9 new collections total, added to the existing 3.
- **Copy authorship:** title/description/introduction are written once per value as part of this
  implementation (short, direct, matching the existing 3 entries' voice — see Code Style below),
  not authored individually by Mehmet the way curator notes are. Normal PR review is the only
  approval gate; this does not need the ADR-0005 AI-content-boundary treatment because no AI tool
  generates it per-playlist at runtime — it is fixed editorial copy checked into source, exactly
  like the existing 3 entries.
- **Genre/occasion:** explicitly out of scope here. They already have collection coverage
  (partial); whether to extend that to every genre/occasion value is a separate future decision,
  not blocked by this spec.
- **URL structure:** reuses the existing flat `/collections/{slug}` namespace rather than
  introducing dimension-prefixed routes (`/eras/...`, `/moods/...`). Rationale: the entire
  pipeline — routing, canonical URL, breadcrumb, sitemap entry, `llms.txt` mention — already works
  end-to-end for this shape and is already indexed; a second route prefix would duplicate
  `CollectionPage.razor`'s logic for no proven SEO benefit (URL folder names are a weak ranking
  signal next to title/H1/content) and this project's standing bias is against new layers without
  a concrete need (`CLAUDE.md` — "Yeni proje, katman... paket ancak somut ihtiyaç varsa eklenir").
  **Flag this back if you disagree** — it is the one call in this spec made without asking.

## Tech Stack

No change. .NET 10 / C# 14, Blazor Web App static SSR, existing `TheBluesland.Web` project only —
this feature touches zero other projects (no `TheBluesland.Data`, no sync tool).

## Commands

```
Build: dotnet build TheBluesland.slnx
Test:  dotnet test TheBluesland.slnx
```

## Project Structure

```
src/TheBluesland.Web/Content/PlaylistCollections.cs   → add 9 entries; add Dimension to the record
src/TheBluesland.Web/Components/Pages/CollectionsPage.razor → group the index by Dimension
src/TheBluesland.Web/Components/Pages/CollectionPage.razor  → pass StructuredDataJson
src/TheBluesland.Web/Seo/StructuredDataBuilder.cs     → new builder for a playlist-list page
tests/TheBluesland.UnitTests/Web/...                  → tests for the above (see Testing Strategy)
```

No new files needed for the collections/pages themselves — `CollectionPage.razor` and
`SitemapGenerator.cs` are already generic over `PlaylistCollections.All` and require no changes
beyond the JSON-LD addition.

## Code Style

New entries follow the exact shape and voice of the existing 3 — short, sensory, second-person-free,
no marketing filler:

```csharp
new("melancholic", "Melancholic Playlists",
    "Slower, reflective listening. Explore melancholic Spotify playlists at TheBluesland.",
    "For when the mood calls for something quieter and more inward. These playlists lean into a",
    "slower tempo, sparser arrangement, or simply a more reflective lyric — different routes to",
    "the same unhurried feeling.",
    Dimension.Mood,
    new PlaylistFilterCriteria(["melancholic"], [], [], [])),
```

`Dimension` is a new small enum (`Genre | Occasion | Mood | Era`) on `PlaylistCollection`, used only
by `CollectionsPage.razor` to group the index — it carries no filtering logic; `Criteria` still does
that, unchanged.

## Testing Strategy

Existing test project/conventions (xUnit, Shouldly) — no new framework.

- `PlaylistCollections`: a test asserting every mood and every era except `mixed-era` has exactly
  one collection entry, and each entry's `Criteria` selects only its own value (guards against a
  copy-paste slug/criteria mismatch across 9 near-identical entries).
- `StructuredDataBuilder`: a test for the new playlist-list builder — valid JSON, correct `@type`,
  one `ItemList` element per playlist passed in, in the order given.
- `SitemapGenerator`: existing test presumably already asserts one `<url>` per `PlaylistCollections.All`
  entry — extend/confirm it now counts 12, not 3.
- No E2E/Playwright test is required for this: it is the same `CollectionPage.razor` code path
  already covered by existing E2E smoke tests, exercised with more data, not new UI.

## Boundaries

- **Always:** run `dotnet test` for the touched projects before calling this done; keep the new
  entries' tone consistent with the existing 3 (read them before writing the 9 new ones).
- **Ask first:** don't touch `docs/business-technical-specification.md` section 8 wording — this
  spec doesn't change the taxonomy, only what pages exist for values that already exist. If someone
  wants to soften the "mixed-era" exclusion, that's a scope change, ask first.
- **Never:** don't add a new route/page type when `PlaylistCollections` + `CollectionPage.razor`
  already covers the shape; don't hand-wave the JSON-LD gap — spec section 14 / FR-032 already
  established that indexable pages get structured data, this closes that for collections too.

## Success Criteria

- [ ] `PlaylistCollections.All` has 12 entries: the original 3 plus 5 mood + 4 era.
- [ ] `/collections` groups entries under Genre / Occasion / Mood / Era headings.
- [ ] Every new collection page renders with its own title, canonical URL, breadcrumb — same as the
      existing 3 — and now also a JSON-LD `CollectionPage`/`ItemList` document listing its playlists.
- [ ] `SitemapGenerator` output contains all 12 `/collections/{slug}` URLs.
- [ ] `dotnet test` green, including the new tests above.
- [ ] No change to `HomePage.razor`'s filter behavior, `PlaylistFilter`, or the taxonomy files
      themselves — this is additive only.

## Open Questions

None blocking — the two real decisions (coverage, authorship) were resolved above with Mehmet
2026-09-09. The URL-structure call is stated with rationale for him to override if he disagrees.
