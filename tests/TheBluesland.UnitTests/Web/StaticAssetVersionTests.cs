using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Shouldly;
using TheBluesland.Web;
using Xunit;

namespace TheBluesland.UnitTests.Web;

/// <summary>
/// Regression test (2026-09-06): <see cref="IWebHostEnvironment.WebRootPath"/> is null whenever no
/// wwwroot directory exists - true of every test project here, since none copies TheBluesland.Web's
/// own wwwroot into its own output directory (see WebHostFactory.Create's webRootPath parameter
/// doc). The original implementation called Path.Combine(environment.WebRootPath, ...) directly,
/// which throws ArgumentNullException on a null first argument - every page render threw, turning
/// into a bare 500 for any host with no wwwroot. That went unnoticed in TheBluesland.UnitTests
/// because StaticAssetVersion was a static class with one process-wide cache: whichever test with a
/// *valid* WebRootPath happened to run first pre-populated the cache for every other test in the
/// same process, and only a fresh process (TheBluesland.E2ETests, a real Chromium browser) hit the
/// crash directly, as a real 500 on every page. Now a per-instance singleton (see the class's own
/// doc comment) with no such cross-instance masking.
/// </summary>
public sealed class StaticAssetVersionTests
{
    [Fact]
    public void For_with_a_null_WebRootPath_returns_a_hash_instead_of_throwing()
    {
        var assetVersion = new StaticAssetVersion(new FakeWebHostEnvironment(webRootPath: null));

        Should.NotThrow(() => assetVersion.For("css/app.css"));
    }

    [Fact]
    public void For_with_a_valid_WebRootPath_but_a_missing_file_returns_a_hash_instead_of_throwing()
    {
        var assetVersion = new StaticAssetVersion(new FakeWebHostEnvironment(webRootPath: Path.GetTempPath()));

        Should.NotThrow(() => assetVersion.For("css/this-file-does-not-exist.css"));
    }

    [Fact]
    public void For_returns_a_different_hash_when_the_files_content_actually_differs()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "a.css"), "body { color: red; }");
            File.WriteAllText(Path.Combine(directory, "b.css"), "body { color: blue; }");
            var assetVersion = new StaticAssetVersion(new FakeWebHostEnvironment(directory));

            assetVersion.For("a.css").ShouldNotBe(assetVersion.For("b.css"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeWebHostEnvironment(string? webRootPath) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = webRootPath!;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "TestApp";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Test";
    }
}
