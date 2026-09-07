namespace TheBluesland.SpotifyFetcher.Sync;

/// <param name="Created">Playlists that had no cache row before this run.</param>
/// <param name="Updated">Existing rows refreshed from a full read, tracks included.</param>
/// <param name="Skipped">
/// Existing rows whose <c>snapshot_id</c> was unchanged, so the paginated track read was skipped
/// and the stored artists/eras were kept (US-024). Reported so a run that suddenly stops skipping
/// is visible in the job summary before it turns into another rate-limit lockout.
/// </param>
/// <param name="Unavailable">Playlists Spotify no longer returns.</param>
public sealed record SyncSummary(int Created, int Updated, int Skipped, int Unavailable);
