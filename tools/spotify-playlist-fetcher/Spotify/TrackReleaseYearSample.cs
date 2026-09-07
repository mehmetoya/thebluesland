namespace TheBluesland.SpotifyFetcher.Spotify;

/// <summary>
/// US-023: the release years read for one playlist, plus whether the read stopped at
/// <see cref="SpotifyPlaylistClient"/>'s page cap instead of reaching the end of the playlist.
///
/// The cap exists because the first real run against this catalogue got the whole Spotify account
/// rate-limited for ~19.5 hours (2026-09-07): following <c>next</c> to exhaustion meant ~100
/// requests for `psychedelia` alone (10,000 tracks) and ~87 for `no-more-words`, several hundred
/// across 120 playlists. An era *distribution* needs a representative sample, not a census, so
/// large playlists are now sampled and the report says so - a suggestion drawn from 300 tracks is
/// still a far better signal than the guesswork it replaces, and it costs a fraction of the quota.
///
/// Carries no release year outward by itself - the caller aggregates
/// <see cref="ReleaseYears"/> into percentages immediately (spec section 9.4/11.2).
/// </summary>
/// <param name="ReleaseYears">Years parsed from <c>album.release_date</c>, undated tracks skipped.</param>
/// <param name="WasSampled">True when the playlist was longer than the page cap allowed reading.</param>
public sealed record TrackReleaseYearSample(IReadOnlyList<int> ReleaseYears, bool WasSampled);
