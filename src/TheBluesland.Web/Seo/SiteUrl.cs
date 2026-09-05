namespace TheBluesland.Web.Seo;

/// <summary>Canonical URLs use a configured public origin, never request or forwarded headers.</summary>
public static class SiteUrl
{
    public const string PublicOriginConfigKey = "Site:PublicOrigin";
    public const string DefaultPublicOrigin = "https://thebluesland.onrender.com";

    public static string BuildAbsolute(HttpContext httpContext, string path)
    {
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var origin = configuration[PublicOriginConfigKey] ?? DefaultPublicOrigin;
        return origin.TrimEnd('/') + path;
    }
}
