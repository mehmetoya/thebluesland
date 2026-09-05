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
///
/// Registered as a singleton service (not a static class - a real bug in the original version):
/// <see cref="IWebHostEnvironment.WebRootPath"/> is null whenever no wwwroot directory exists
/// (every test project here, since none copies TheBluesland.Web's own wwwroot into its output -
/// see WebHostFactory.Create's webRootPath parameter), and Path.Combine throws ArgumentNullException
/// on a null first argument. A static class's cache is also shared process-wide across every
/// WebApplication instance, so one test with a valid WebRootPath silently pre-populated the cache
/// for every other test in the same process, masking that crash entirely until a fresh process
/// (a real Playwright browser, via TheBluesland.E2ETests) hit it as a bare 500 on every page.
/// A per-instance singleton scopes the cache correctly to one app's own WebRootPath instead.
/// </summary>
public sealed class StaticAssetVersion(IWebHostEnvironment environment)
{
    private readonly Dictionary<string, string> _cache = [];
    private readonly Lock _cacheLock = new();

    public string For(string webRootRelativePath)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(webRootRelativePath, out var cached))
            {
                return cached;
            }

            var physicalPath = environment.WebRootPath is { Length: > 0 } webRootPath
                ? Path.Combine(webRootPath, webRootRelativePath)
                : null;
            var bytes = physicalPath is not null && File.Exists(physicalPath)
                ? File.ReadAllBytes(physicalPath)
                : [];
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes))[..8];
            _cache[webRootRelativePath] = hash;
            return hash;
        }
    }
}
