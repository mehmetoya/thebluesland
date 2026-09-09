namespace TheBluesland.Web.Seo;

/// <summary>Canonical URLs use a configured public origin, never request or forwarded headers.</summary>
public static class SiteUrl
{
    public const string PublicOriginConfigKey = "Site:PublicOrigin";
    public const string DefaultPublicOrigin = "https://thebluesland.onrender.com";

    /// <summary>
    /// 2026-09-09 domain migration to thebluesland.com (see SPEC-custom-domain-migration.md):
    /// hostnames that permanently redirect (301) to the configured canonical origin instead of
    /// serving content directly. Render's own subdomain never goes away even after a custom domain
    /// is attached, and it - along with <c>www.thebluesland.com</c> - is already the target of
    /// existing search-engine indexing, shares and <c>llms.txt</c>/JSON-LD references, so silently
    /// rejecting it (its only alternative under <see cref="WebHostFactory"/>'s single-canonical-
    /// host allowlist) would break every one of those rather than migrating them. A hardcoded list
    /// is deliberate: this is a one-time, permanently-known migration, not a recurring
    /// per-environment value like every other <c>WebHostFactory</c> config key.
    /// </summary>
    public static readonly IReadOnlyList<string> LegacyRedirectHosts = ["thebluesland.onrender.com", "www.thebluesland.com"];

    public static string BuildAbsolute(HttpContext httpContext, string path)
    {
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var origin = configuration[PublicOriginConfigKey] ?? DefaultPublicOrigin;
        return origin.TrimEnd('/') + path;
    }
}
