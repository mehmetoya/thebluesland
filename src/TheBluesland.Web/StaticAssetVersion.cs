using System.Security.Cryptography;

namespace TheBluesland.Web;

/// <summary>
/// A short content-hash query-string suffix (e.g. <c>?v=3f9a1c2b</c>) for a static asset under
/// wwwroot, so <c>/css/app.css</c>/<c>/js/infinite-scroll.js</c> get a fresh URL every time their
/// content actually changes - browsers that aggressively cache a same-URL static file (the default
/// <c>UseStaticFiles()</c> sets no explicit Cache-Control, so this depends entirely on browser
/// heuristics) would otherwise keep serving a stale copy after a deploy until the cache happens to
/// expire, producing exactly the "new HTML, old CSS" mismatch a mid-deploy screenshot showed
/// (2026-09-06). Hashes file *content*, not last-write-time: a fresh CI checkout resets every
/// file's mtime to the same instant, which would make a timestamp-based version identical across
/// every deploy and defeat the whole point.
/// </summary>
public static class StaticAssetVersion
{
    private static readonly Dictionary<string, string> Cache = [];
    private static readonly Lock CacheLock = new();

    /// <summary>Cached for the lifetime of the process - a deploy always starts a fresh process,
    /// so there is never a stale in-memory hash next to changed bytes on disk.</summary>
    public static string For(IWebHostEnvironment environment, string webRootRelativePath)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(webRootRelativePath, out var cached))
            {
                return cached;
            }

            var physicalPath = Path.Combine(environment.WebRootPath, webRootRelativePath);
            var bytes = File.Exists(physicalPath) ? File.ReadAllBytes(physicalPath) : [];
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes))[..8];
            Cache[webRootRelativePath] = hash;
            return hash;
        }
    }
}
