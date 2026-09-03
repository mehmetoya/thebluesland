using TheBluesland.Web;
using TheBluesland.Web.Content;

// US-005/US-008: host wiring (content reader, cache lookup, health checks) plus the static-SSR
// route table (/, /playlists/{slug}, /about, /privacy, /terms). Filter interaction, Spotify embed,
// SEO metadata and security headers land in later stories (US-009/010/011/012) per
// docs/adr/0003-mimari-kapsam.md.
//
// US-007 (spec 18.1, item 3): a `validate-content` first argument switches this entry point into
// a one-shot content-validation CLI instead of starting Kestrel, so CI can run
// `dotnet run --project src/TheBluesland.Web -- validate-content` and get a non-zero exit on any
// content/playlists violation. Kept here rather than a new console project per ADR-0003 ("no new
// project/layer without a real dependency boundary") and mirrors the same args-driven CLI pattern
// tools/spotify-playlist-fetcher/Program.cs already uses for its own GitHub Actions invocation.
// Normal `dotnet run` (no args) is unaffected - it falls through to the existing web host path.
if (args is [ContentValidationCli.CommandArgument, ..])
{
    var contentDirectory = args.Length > 1
        ? args[1]
        : Path.Combine(Directory.GetCurrentDirectory(), "content", "playlists");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    return await ContentValidationCli.RunAsync(contentDirectory, Console.Out, cts.Token);
}

var app = WebHostFactory.Create(args);
await app.RunAsync();
return 0;
