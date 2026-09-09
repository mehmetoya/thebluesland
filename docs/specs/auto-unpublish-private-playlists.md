# Spec: Auto-unpublish playlists that became private on Spotify

Triggered by a real incident (2026-09-09): `my-shazam-tracks` was made private on Spotify but kept
showing on the site, because content publish status (`content/playlists/*.md`'s `status` field) is
purely editorial and never derived from Spotify's own visibility flag - the monthly/manual sync only
refreshes numeric cache stats. Mehmet wants this closed: when the sync tool finds a playlist has
gone private, it should **silently auto-unpublish it** - no manual step.

Mehmet confirmed the mechanism via AskUserQuestion (2026-09-09): **open a PR and auto-merge it once
CI passes**, not a direct push to `main` (which is branch-protected and would need a real bypass
grant) and not a job-summary-only suggestion (the existing pattern for `suggest-curator-note.yml`,
per ADR-0005 - that pattern is for *AI-generated* text needing human judgment; this is a
deterministic fact check with no generative content, so auto-merge is the right fit, not a
precedent break of ADR-0005's actual concern).

**This is this repo's first workflow with write access to the repository.** Document that
explicitly - see Boundaries.

## ⚠️ Implementation is blocked right now - read before starting

`tools/spotify-playlist-fetcher/Spotify/SpotifyPlaylistClient.cs`,
`SpotifyPlaylistSummary.cs`, and `Sync/PlaylistCacheSyncService.cs` - the exact three files this
spec's core module touches - are **currently mid-edit with uncommitted changes from a different,
concurrent session** (a follower-count/catalogue-priority feature: `docs/specs/catalogue-priority-and-follower-sort.md`,
a new `AddFollowerCount` migration, `PlaylistCacheSignalsCache.cs`). The build does not compile
right now because of that in-progress work. **Do not dispatch implementation until that other
session's work is committed/merged and the build is green again** - editing the same files
concurrently risks direct collisions and wasted work on both sides. Check `git status` and
`dotnet build` before starting; if still broken/uncommitted, wait and ask Mehmet for a status update
rather than proceeding.

## Objective

`sync` mode (the default `sync-spotify.yml` path) already fetches each playlist listed in
`content/playlists/*.md`. Extend it to also read the playlist's `public` field from the Spotify API;
when a playlist is `public: false` and its content file currently says `status: published`, rewrite
that file's front matter (`status: draft`, drop `publishedAt`) as part of the sync run. If any file
changed, the workflow opens a PR with that change and auto-merges it once required checks pass -
Mehmet does nothing.

## Design

### 1. `SpotifyPlaylistSummary` (`tools/spotify-playlist-fetcher/Spotify/SpotifyPlaylistSummary.cs`)

Add `public required bool IsPublic { get; init; }`. Populate it from the same API response
`SpotifyPlaylistClient` already parses for `Name`/`Description`/etc. - add `public` to whatever
`fields=` query parameter that call already restricts to (mirror `SpotifyMyPlaylistsClient`'s
existing `...,public,...` pattern, don't invent a new field-selection approach).

### 2. `PlaylistCacheSyncService` (`tools/spotify-playlist-fetcher/Sync/PlaylistCacheSyncService.cs`)

`SyncAsync` needs the content directory path and each playlist's current `status`/`slug` alongside
its Spotify id (it already reads ids via `frontMatterReader.ReadDistinctSpotifyPlaylistIdsAsync` in
`Program.cs` - extend that reader, or add a sibling read, to also carry `status` and file path per
id, whichever fits the existing reader's shape with the least disruption).

For each `Found` result where `Summary.IsPublic == false` and the matching content file's `status`
is `published`: rewrite that file (see #3) and record its slug in a new
`SyncSummary.NewlyUnpublishedSlugs` (`IReadOnlyList<string>`, default empty). Do **not** touch files
that are already `draft` or don't exist as published - this only ever moves published → draft, never
touches a file that's already in the state it should be.

### 3. Front-matter rewrite (new, small, focused type - e.g. `PlaylistFrontMatterWriter` alongside
   the existing `PlaylistContentReader`/reading code)

Read the file, replace the front-matter block's `status: published` line with `status: draft`, and
remove the `publishedAt: ...` line entirely (matching the manual edit already made for
`my-shazam-tracks.md` - same two-line change, now automated). Preserve every other line byte-for-byte
- this is a targeted line replace/removal, not a full YAML round-trip re-serialization (re-serializing
risks reordering keys or reformatting the human-authored body/comments unpredictably).

### 4. `Program.cs`'s `sync` branch

After `syncService.SyncAsync(...)` returns, if `summary.NewlyUnpublishedSlugs` is non-empty, print
them clearly (`Console.WriteLine`) so the job log/summary shows what happened - this is the only
place a human sees this happened, since the PR itself doesn't require anyone to open it.

### 5. `sync-spotify.yml` workflow

- Add `permissions: contents: write` and `pull-requests: write` **on this workflow only** (mirrors
  the existing per-workflow secret-scoping comment style already in this file's header) - `ci.yml`
  and `deploy.yml` must stay `contents: read`, unchanged.
- After the sync step, add: `git status --porcelain -- content/playlists` check; if non-empty,
  create a branch (e.g. `auto/unpublish-private-playlists-<run id>`), commit
  (`git commit -m "Auto-unpublish playlist(s) made private on Spotify"` with the same
  `Co-Authored-By` convention this repo's own commits use), push, `gh pr create`, then
  `gh pr merge --auto --squash` (requires the repo's auto-merge feature enabled - note this as a
  one-time GitHub repo setting Mehmet may need to toggle, same class of external step as the
  existing secret-scoping note in this file's header).
- Only the `sync` mode does this - `list-playlists`, `dump-cache`, `resync-eras` stay read-only,
  unchanged.

## Tech Stack / Commands

No new dependency. `dotnet build TheBluesland.slnx`, `dotnet test TheBluesland.slnx` as usual.

## Testing Strategy

- Unit test `SpotifyPlaylistSummary`/`SpotifyPlaylistClient` parsing `public` correctly (mirror the
  existing `SpotifyMyPlaylistsClient` test pattern for the same field).
- Unit test the front-matter rewrite in isolation: given a `status: published` + `publishedAt: ...`
  fixture file, assert the output has `status: draft`, no `publishedAt` line, and every other line
  unchanged (byte-for-byte, including the body).
- Unit test `PlaylistCacheSyncService.SyncAsync`: a `Found` result with `IsPublic: false` against a
  currently-published entry produces exactly one `NewlyUnpublishedSlugs` entry and rewrites the file;
  `IsPublic: true` or an already-draft file produces none.
- No workflow-YAML test (not testable in this suite) - the PR/auto-merge steps get verified by
  Mehmet watching the first real run, per the Success Criteria below.

## Boundaries

- **Always:** run the full test suite; the front-matter rewrite must be a minimal, targeted text
  change, never a full re-serialization.
- **Ask first:** any change to `ci.yml`/`deploy.yml`'s permissions (must stay `contents: read`);
  any change to how `list-playlists`/`dump-cache`/`resync-eras` modes behave.
- **Never:** let this new write-back path touch any file outside `content/playlists/*.md`; let it
  ever move a file from `draft` to `published` (this is one-directional - unpublish only, never
  auto-publish, which stays a fully human decision per this project's existing content boundary);
  add an LLM/generative step to this path - the whole point is that this is a deterministic fact
  ("Spotify says private"), not a judgment call, which is what makes auto-merge appropriate here
  when it wasn't for `suggest-curator-note.yml`.

**Documentation debt this creates:** once implemented, `docs/adr/0002-spotify-veri-mimarisi.md` (or
a new ADR) needs a short note that `sync-spotify.yml` is now the first workflow with `contents:write`/
`pull-requests:write`, and why that's scoped/safe (deterministic, one-directional, PR+CI-gated, not
a direct push). That doc edit is outside backend-dev's write path (`docs/` is main-session-only) -
the main session will add it after this ships and is verified, not backend-dev.

## Success Criteria

- [ ] `SpotifyPlaylistSummary.IsPublic` populated correctly from the API.
- [ ] A playlist found `IsPublic: false` while its content file says `published` gets that file
      rewritten to `status: draft` (no `publishedAt`) and reported in `SyncSummary.NewlyUnpublishedSlugs`.
- [ ] `dotnet build`/`dotnet test` green, including the new unit tests above.
- [ ] `sync-spotify.yml` has `contents: write`/`pull-requests: write` scoped to itself only;
      `ci.yml`/`deploy.yml` unchanged.
- [ ] First real run (next scheduled sync, or a manual `workflow_dispatch`) observed by Mehmet to
      confirm the PR-open-and-auto-merge flow actually works end-to-end - this is the one thing that
      cannot be verified in this test suite.

## Open Questions

None blocking the spec itself. The one prerequisite noted above (repo's auto-merge feature must be
enabled in GitHub settings for `gh pr merge --auto` to work) is an external, one-time step Mehmet
may need to do, not a code decision.
