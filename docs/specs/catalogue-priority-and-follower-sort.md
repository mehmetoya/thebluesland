# Home page ordering: curated priority + follower count

## Problem

The home page catalogue sorts by `displayOrder` ascending, then `publishedAt` descending
(`PlaylistCatalogueSort`). In practice neither differentiates anything: 118 of 120 playlists have
no `displayOrder` (default 0), and **all 120** share the identical `publishedAt: 2026-09-05` (a
batch-import artifact). The real, unintentional order is therefore whatever
`PlaylistContentReader` happens to read files in (alphabetical-ish), with two outliers
(`masterpieces-of-erkin-the-father`, `dear-mr-fantasy`, both `displayOrder: 1`/`2`) pushed to the
very end.

Mehmet wants two things instead: specific playlists (starting with Bluesland and Jazzstation, more
to come) pinned to the front in an order he controls, and - since there's no genuine "recency"
signal anywhere in the data (front matter, `SyncedAt`, and git history were all checked and are
equally batch-uniform, see conversation 2026-09-10) - Spotify's real follower count as a secondary
signal for everything else.

## Decision

**Curated tier.** `PlaylistContent.Featured` (a `bool` that was already parsed and validated but
never actually consumed by any sort or render code - a half-wired feature) becomes
`FeaturedOrder` (`int?`). Present = featured, at that explicit priority (ascending, 1 first); null
= not featured. This replaces a true/false flag with the ordering Mehmet actually wants, and turns
"add another featured playlist" into a plain content edit - no code involved.

The existing cross-file cap (`PlaylistContentValidator.MaxFeaturedPlaylists = 4`) is removed
entirely, per Mehmet's explicit instruction (2026-09-10) - he expects to grow this list over time.

**Follower count.** Spotify's Get Playlist endpoint already returns `followers.total` in the same
single, unpaginated request `GetPlaylistSummaryAsync` already makes for every playlist - widening
its `fields` parameter costs zero additional Spotify requests and applies on every sync run,
skipped playlists included (follower count, like name/description/cover, is refreshed via
`ApplyPlaylistMetadata` regardless of whether the paginated track read runs). It is an aggregate
playlist-level number, the same class as `TrackCount` - not track-level data (spec 9.4/11.2).

**Final order:** featured playlists first (by `FeaturedOrder` ascending), then everything else by
follower count descending (unknown/unavailable last, stable otherwise - i.e. falls back to today's
incidental file order when no cache data is available, exactly like today's fallback behavior).

## A structural consequence: sorting moves from load-time to read-time

`PlaylistContentRepository` loads its catalogue **once per process** (`Lazy<Task<Catalogue>>`) and
today bakes `PlaylistCatalogueSort.Apply`'s result into that one-time snapshot. Follower count lives
in `spotify_playlist_cache`, refreshed monthly by an out-of-process sync tool - if sorting stayed
at load-time, a new follower count would never be reflected until the next deploy/restart.

This project already has the exact right pattern for "Spotify-cache-derived signal that must be
fresher than a process restart, merged onto the static Markdown catalogue at read time":
`PlaylistEraCache`, a 5-minute `IMemoryCache`-backed batched lookup, merged in on every
`FindAllPublishedAsync`/`FindBySlugAsync` call via `PlaylistEraAssignment.Apply`. Follower count
reuses the exact same cache and refresh interval rather than adding a second poller: `PlaylistEraCache`
is renamed `PlaylistCacheSignalsCache` and its cached value widens from `string[]` (computed eras)
to `PlaylistCacheSignals(string[] ComputedEras, int? FollowerCount)` - one query still does what two
would otherwise do. `PlaylistCatalogueSort.Apply` moves from `PlaylistContentRepository`'s one-time
`LoadCatalogueAsync` into the per-call `FindAllPublishedAsync`, run after `ApplyErasAsync`, fed by
the same fresh signals dictionary.

`HomePage.razor` needs no change: it already calls `FindAllPublishedAsync` before filtering/paging,
and `PlaylistFilter.Apply` is a plain, order-preserving `.Where()` - the corrected order flows
through untouched.

## Acceptance criteria

- [ ] `PlaylistContent.Featured` (bool) is replaced by `FeaturedOrder` (int?); front matter key
      `featured: true`/`false` becomes `featuredOrder: <N>` (present) or omitted (absent) -
      matching how `displayOrder` is already conventionally omitted rather than defaulted.
- [ ] `PlaylistContentValidator.MaxFeaturedPlaylists`/`AddFeaturedCapIssues` and their three tests
      are removed - no cap exists any more.
- [ ] `spotify_playlist_cache` gains a `follower_count` column (migration), populated from
      Spotify's `followers.total` on every sync (full read or metadata-only skip alike) - zero
      additional Spotify requests.
- [ ] `PlaylistCatalogueSort.Apply` orders featured playlists first by `FeaturedOrder` ascending,
      then the rest by follower count descending; a playlist with no cache data (DB unreachable,
      never synced) sorts after every playlist that has a follower count, not before or crashing.
- [ ] Follower count is refreshed at most 5 minutes stale on the home page (same interval as era
      tags today), without requiring a restart/redeploy.
- [ ] Bluesland and Jazzstation (Mehmet's named examples) plus the three playlists already marked
      `featured: true` (`anatolian-domestic-products`, `dear-mr-fantasy`,
      `masterpieces-of-erkin-the-father`) get an initial `featuredOrder`; Mehmet can freely
      reorder/add/remove by editing the number in any playlist's front matter going forward.

## Out of scope

- A UI treatment for featured playlists (badge, distinct card style) - not asked for; they are
  simply first in the existing list.
- Any change to `displayOrder`/`publishedAt` themselves, or to sorting on `/collections/*` pages -
  this story is the home page catalogue only.
- Backfilling `follower_count` immediately - it fills in on the next scheduled sync like any other
  metadata field; no forced resync needed (unlike the 2026-09-09 era threshold change, this field
  is refreshed by `ApplyPlaylistMetadata` even on a skipped/unchanged-snapshot sync, not gated by
  US-024's paginated-read skip).
