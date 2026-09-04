---
schemaVersion: 1
slug: invalid-spotify-id-fixture
spotifyPlaylistId: not-a-real-id
title: Invalid Spotify Id Fixture
summary: A solid summary describing this fixture playlist in plain, sufficiently long prose.
moods:
  - melancholic
genres:
  - jazz
occasions:
  - slow-evening
era: mixed-era
status: published
publishedAt: 2026-01-01
---

Curator note body for the invalid-spotify-id fixture. US-012 AC2: this spotifyPlaylistId is
deliberately not 22 base62 characters, so the embed and "Open in Spotify" link must be rejected
rather than built from it.
