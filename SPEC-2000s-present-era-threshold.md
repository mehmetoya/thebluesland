# Raise the 2000s-present majority threshold to 75%

## Problem

The first real, complete era measurement (2026-09-09, after US-026's backfill sync) confirmed a
suspicion raised the day before from a manual artist-list audit: several large, classic-catalogue
playlists sat just over the original 60% majority threshold for `2000s-present`, causing the
algorithm to suggest it as the *sole, dominant* era - suppressing `mixed-era` entirely - for
playlists whose actual musical content is anything but modern:

| Playlist | Dated tracks | Measured 2000s-present share |
| --- | --- | --- |
| Songs with Blues in Their Names | 138 | 61% |
| Bluesland | 1601 | 63% |
| Blues Will Save Us | 211 | 64% |
| Free Fall | 31 | 65% |
| My Shazam Tracks | 651 | 69% |

Spotify's `album.release_date` is the release date of the specific catalogue entry a track is
attached to, not necessarily the track's original recording/release - a reissue or compilation can
overwrite it. That effect is one-directional (a track can only misreport as *later* than it truly
is, never earlier) and concentrates entirely in the newest bucket, so `2000s-present` is
structurally the least trustworthy bucket for a large, decades-spanning catalogue, and the
one bucket where a threshold error skews toward false dominance rather than being roughly symmetric
across buckets.

By contrast, 37 of the 48 playlists whose only suggested era is `2000s-present` measured at 80% or
higher - genuinely, overwhelmingly modern, not a borderline call.

## Decision

`2000s-present` gets its own majority threshold: 75%, not the 60% every other bucket keeps
(`PlaylistEraDistributionCalculator.MajorityThresholds`). The 20% suggestion-inclusion floor is
**unchanged and identical across all four buckets** - raising it too would have silently dropped
`2000s-present` as a legitimate *secondary* tag from 50+ playlists that genuinely split across two
eras (e.g. a 45%/40% split), which was never the problem being fixed.

Concretely, for the five playlists above: instead of suggesting `2000s-present` alone, the
calculator now suggests `2000s-present, mixed-era` together - the signal is kept, but the false
claim of exclusive dominance is removed. A playlist measuring 75% or higher is unaffected: still
suggested as `2000s-present` alone, same as before.

## Why not a clean swap to plain `mixed-era`

The alternative - drop `2000s-present` from the suggestion entirely below 75%, leaving only
`mixed-era` - was considered and rejected: it would require raising the *inclusion* floor for this
bucket specifically, and the report shows dozens of playlists (e.g. `a-name-with-rain` at 35%,
`dear-mr-fantasy` at 45%) where `2000s-present` legitimately co-occurs with another concrete bucket
well under 75%, correctly describing a genuine two-era split. Collapsing those to a single
`mixed-era` tag would erase real signal to fix a problem that only exists at the *majority*
threshold, not the inclusion one.

## Consequence: this does not take effect until the next full resync

`PlaylistCacheSyncService` only recomputes and writes a playlist's `computed_eras` when it actually
re-reads that playlist's tracks. US-024's snapshot-id skip means an unchanged playlist's
`computed_eras` is untouched by a normal monthly `sync` run - so this threshold change, once
deployed, changes what `report-eras` *would* suggest immediately (it reads live from stored
`era_bucket_counts`), but changes nothing on the live site until `sync-spotify.yml`'s
`resync-eras` mode (US-025) is run to force every playlist's `computed_eras` to be recomputed under
the new rule.

**Do not run `resync-eras` the same day as another full Spotify crawl.** The 2026-09-09 backfill
sync (0 created, 120 updated, 0 skipped - forced by `era_bucket_counts` being null everywhere)
already spent that day's one-heavy-Spotify-job budget (`docs/automatic-eras.md`'s rule, written
after the 2026-09-07/08 rate-limit lockouts). Run `resync-eras` on a later day, alone.

## Acceptance criteria

- [x] `2000s-present` alone requires ≥75% to suppress `mixed-era`; the other three buckets keep 60%.
- [x] The 20% suggestion-inclusion floor is identical across all four buckets, unchanged.
- [x] A playlist at exactly 75% `2000s-present` still suggests it alone (boundary is inclusive,
      matching the existing `>=` convention for every other threshold in this calculator).
- [x] A non-`2000s-present` bucket reaching 60% still suppresses `mixed-era` on its own, unaffected
      by this change (regression guard).
- [ ] A `resync-eras` run, on a day with no other heavy Spotify job, confirms the five playlists
      above (and any others in the 60-74% range) now carry `computed_eras` including `mixed-era`,
      and that the report's suggestions and the live site's era filter agree afterward.

## Out of scope

- Any other bucket's threshold - only `2000s-present`'s reissue-date vulnerability motivated this.
- Automating the "one heavy Spotify job per day" rule in code (e.g. a workflow-level guard) - stays
  a documented operational rule for now; revisit only if it's violated by accident again.
