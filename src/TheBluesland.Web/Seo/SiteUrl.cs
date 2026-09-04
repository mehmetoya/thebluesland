namespace TheBluesland.Web.Seo;

/// <summary>
/// Builds absolute site URLs from the current request's own scheme and host (US-011). No configured
/// public hostname exists anywhere in this codebase today - the alternative considered was a
/// config key read the same way <see cref="TheBluesland.Web.Content.PlaylistContentRepository.ContentDirectoryConfigKey"/>
/// reads its own setting. A request-derived base URL was chosen instead because every caller that
/// needs one - page metadata render, <c>/sitemap.xml</c>, <c>/robots.txt</c>, the OG-image endpoints
/// - is always reached through a real HTTP request in this architecture; there is no offline or
/// background sitemap generation. That makes the request's own <c>Host</c> header guaranteed
/// accurate for wherever the app is actually being served from, with no extra deploy-time setting to
/// keep in sync (and it is what makes integration tests - which bind an ephemeral loopback port -
/// assert against the right host for free).
///
/// Known remaining risk, not addressed here: Render (spec 12.2) terminates TLS at its edge and
/// forwards plain HTTP to the container, so without forwarded-headers middleware (out of this
/// story's scope) <c>Request.Scheme</c> could read "http" in production even though the public URL
/// is https. Tracked for US-014 (deploy pipeline), which is where the reverse-proxy configuration
/// this depends on is actually decided.
/// </summary>
public static class SiteUrl
{
    public static string BuildAbsolute(HttpContext httpContext, string path) =>
        $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{path}";
}
