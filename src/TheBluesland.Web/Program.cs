using TheBluesland.Web;

// US-005: minimal host wiring only (content reader, cache lookup, health checks, the single
// /playlists/{slug} render surface). Full routing table, homepage catalogue, SEO and security
// headers land in later stories (US-008/009/010/011/012) per docs/adr/0003-mimari-kapsam.md.
var app = WebHostFactory.Create(args);
await app.RunAsync();
