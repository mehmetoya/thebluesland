using Microsoft.Extensions.Diagnostics.HealthChecks;
using TheBluesland.Web.Content;

namespace TheBluesland.Web.HealthChecks;

/// <summary>
/// Backs <c>/health/ready</c>. Spec 16.2: readiness must depend only on whether editorial content
/// loaded (and, once US-006 lands, passed validation) - never on database reachability. This check
/// never touches <see cref="TheBluesland.Web.Cache.PlaylistCacheLookup"/> or the database.
/// </summary>
public sealed class PlaylistContentHealthCheck : IHealthCheck
{
    private readonly PlaylistContentRepository _contentRepository;

    public PlaylistContentHealthCheck(PlaylistContentRepository contentRepository)
    {
        _contentRepository = contentRepository;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isReady = await _contentRepository.IsReadyAsync(cancellationToken);
            return isReady
                ? HealthCheckResult.Healthy("Validated playlist content loaded.")
                : HealthCheckResult.Unhealthy("A valid catalogue with published playlists is required.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Playlist content failed to load.", ex);
        }
    }
}
