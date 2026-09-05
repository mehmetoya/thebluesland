# Plan — TheBluesland

Kaynak: `docs/product/backlog.md`, `docs/business-technical-specification.md` (v0.2).

## Aktif

Şu an aktif bir hikaye yok; sıradaki iş açık backlog sırasına göre ilerliyor (Mehmet, 2026-09-05:
"açık backloglardan sıradan gidelim").

## Sıradaki

- **US-019 — Playlist kataloğunu kaydırdıkça kademeli yükle (Should).** Sırada bir sonraki:
  120 playlist'lik kataloğu ilk yüklemede yalnızca ilk N (ör. 24) ile göstermek, "daha fazla
  yükle" ile devam etmek — bağımlılığı yok, US-018'den sonra doğal sıradaki.
- **US-021 — Mobil tam-ekran filtre deneyimi (Could).** Bağımlılığı US-018 (tamamlandı); aynı
  URL query-string mekanizmasını paylaşıyor.
- US-017'nin Could kriteri (10 aday dosyanın occasion etiketinin `focus`/`dancing`'e taşınması) —
  içerik PR'ı, kod değişikliği değil; Mehmet uygun görürse ayrı istenebilir.
- Bilinen küçük doküman/hijyen açıkları (2026-09-05 denetiminde bulundu, kod değil): ADR
  numaralandırmasında 0004 boşluğu (atlanmış mı belli değil); `displayOrder`/`publishedAt`in 120
  playlist'in çoğunda ayrım yapmaması (ana sayfa sıralaması şu an dosya-adı sırasına yakın) —
  Mehmet'in kararına bırakıldı, "burası kalsın" (2026-09-05).
- Render'ın native rollback özelliği hâlâ gerçek bir olayla denenmedi (US-014'ün bilerek açık
  bırakılan tek maddesi).
- `suggest-curator-note.yml`'ın `GEMINI_API_KEY`/`NEON_READONLY_CONNECTION_STRING`
  secret'larını bu workflow'a scope etmek (GitHub repo ayarları) Mehmet'in elle yapacağı tek
  seferlik adım — US-004/US-007 ile aynı desen.

## Tamamlanan

- **US-018 — Filtreleri navbar'a taşı (dropdown deseni) (2026-09-06).** Ana sayfanın hep-açık
  `<fieldset>` filtre formu, dört bağımsız native `<details>`/`<summary>` dropdown'undan oluşan
  bir `.filter-navbar`'a dönüştürüldü (`HomePage.razor`, `Styles/app.css`). `<details>` native
  olduğu için her dropdown yalnızca kendi panelini gösterir ve JavaScript'e ihtiyaç duymaz;
  US-009'daki `<form method="get">`/query-string filtre mekanizması değişmeden korundu. Her
  dropdown'ın `<summary>`'si aktif seçim sayısını "Dimension (N)" biçiminde gösteriyor. Ok
  ikonunun dönüşü `prefers-reduced-motion: no-preference` ile korumalı (Could kriteri). Yeni iki
  `HomePageFilterIntegrationTests` testi dropdown yapısını ve aktif-sayı etiketlerini gerçek
  render edilmiş HTML'e karşı doğruluyor. Testcontainers Postgres'e karşı 193/193 test yeşil,
  `dotnet format --verify-no-changes` temiz, Tailwind `build:css` başarılı. Çalışma
  `fix/gemini-retry-transient-5xx` (açık PR #17, ilgisiz Gemini retry fix'i) yerine ondan ayrı
  `feature/us-018-filter-navbar` dalına taşındı, PR kapsamları karışmasın diye.
- **US-016 — AI destekli kürator notu taslağı önerisi (2026-09-05).** Backlog'un son açık
  hikayesi. `tools/spotify-playlist-fetcher`'a yeni bir `suggest-curator-note <spotifyPlaylistId>`
  verb'i eklendi (ADR-0005): `NEON_READONLY_CONNECTION_STRING` (yeni, `suggest-curator-note.yml`'a
  scope edilmiş bir GitHub secret) ile cache'den tek satırı okuyor, `CuratorNotePromptBuilder`
  yalnızca dört izinli alanı (name/description/track_count/artists) prompt'a koyuyor (tüm satır
  builder'a verilse bile), `GeminiClient` (yeni paket yok, ham `HttpClient` + Gemini
  `generateContent` API) taslağı üretiyor, sonuç yalnızca job summary + `actions/upload-artifact`'a
  yazılıyor — DB'ye veya `content/`'e hiçbir yazma yok. Playlist cache'de yoksa veya
  `is_available=false` ise AI client'ı hiç çağrılmıyor (regresyon testiyle sabitlendi). 2026-09-05:
  Mehmet gerçek $0 maliyet istedi (Anthropic kullanım bazlı ücretlendiriyor); sağlayıcı Google
  Gemini'ye çevrildi (kalıcı ücretsiz katman, ödeme yöntemi gerekmiyor) — bkz. ADR-0005'in dated
  notu. Yeni `suggest-curator-note.yml`
  workflow'u yalnızca `workflow_dispatch`; `GEMINI_API_KEY`/`NEON_READONLY_CONNECTION_STRING`
  izolasyonu `SuggestCuratorNoteWorkflowSecretIsolationTests` ile doğrulandı (ci/deploy/sync
  workflow'larının hiçbiri bu iki secret'a erişemiyor). `actionlint` temiz. `dotnet build/test`:
  182/182 yeşil, `dotnet format --verify-no-changes` temiz.
- **Faz 0 — Spec/mimari onayı.** v0.2 spec, ADR-0002 ve ADR-0003 yazıldı. CLAUDE.md/ADR-0001
  çok-istemcili şablonundan sapma (ADR-0003), Mehmet'in 2026-09-03'teki US-005 devam talimatıyla
  onaylandı. v0.2 taksonomisi (spec bölüm 8 — mood/genre/occasion/era, 4 liste) ve section 7'deki
  iki taslak playlist'in başlık/etiket/özet/kürator notu, Mehmet tarafından 2026-09-05'te nihai
  onaylandı (bkz. US-015'in ilk iki girdisi altında).
- **Faz 4 — CI/CD (uçtan uca doğrulandı).** US-013 + US-014 tamam; Render Blueprint bağlandı, ilk
  deploy `https://thebluesland.onrender.com`'da canlı (health-gated traffic, GHCR image, otomatik
  deploy hook, salt-okunur Neon connection string). Branch protection (6 CI job'u + PR zorunluluğu)
  `main` üzerinde aktif ve GitHub API'sinden doğrulandı (2026-09-05). Render'ın native rollback
  özelliği hâlâ gerçek bir olayla denenmedi (mimari olarak destekleniyor, US-014'ün son AC'si
  bilerek açık bırakıldı).
- **Faz 5 — Launch içeriği tamamlandı, kapsam 8'den 120'ye genişledi.** Mehmet 2026-09-05'te
  orijinal "en az sekiz playlist" launch eşiğini iptal edip sahip olduğu tüm herkese açık
  playlist'lerin yayınlanmasını istedi ("bak benim sahip olduğum tüm playlistleri ve tüm türleri
  istiyorum azar azar değil hepsini" → "Tüm 120'sini yayınla, taksonomiyi genişlet"). Bu kapsamda:
  - Genre taksonomisi 6'dan 16 değere genişletildi (Mehmet onayı, `PlaylistTaxonomy.cs`).
  - `tools/spotify-playlist-fetcher`'a salt-okunur `list-playlists` ve `dump-cache` modları
    eklendi — Mehmet'ten playlist ID istemek yerine araç kendi keşfediyor (SEC-001 kapsamında,
    yeni credential yok).
  - `PlaylistCacheSyncService` per-playlist `SaveChangesAsync` yapacak şekilde düzeltildi (120
    playlist'lik senkronda kısmi hata artık ilerlemeyi kaybettirmiyor); regresyon testi eklendi.
  - **120/120 playlist yayında**: US-015'in ilk ikisi (Erkin Koray, Dear Mr. Fantasy — spec
    section 7, 89-117 kelime), sonra 28'lik blues/blues-rock/anadolu-rock/folk partisi (PR #6),
    sonra kalan 90'ı tek partide (PR #7). Her playlist gerçek senkronize edilmiş Spotify sanatçı
    verisine dayanan tag + kürator notuyla yayınlandı. Küratör notu alt sınırı, 120-playlist
    ölçeğinde pratik olmadığı için 80'den 40 kelimeye indirildi (spec FR-021, 2026-09-05 notu;
    gerçek dağılım 42-117 kelime) — Mehmet'in kararı: "kelime sayısını gevşetip spec'i
    güncelleyelim".
  - Featured cap 4/4'te sabit (Erkin Koray, Dear Mr. Fantasy, Bluesland, Anatolian Domestic
    Products); yeni playlist'lerin hepsi `featured: false`.
  - `validate-content` 120/120 dosyada temiz; `dotnet build`/`dotnet test` 167/167 yeşil.

- **US-014 — Render + Neon production deploy pipeline'ı (uçtan uca doğrulandı).**
  `.github/workflows/deploy.yml` — `main`'e her push'ta US-013'ün doğruladığı aynı `Dockerfile`'ı
  build edip `ghcr.io/mehmetoya/thebluesland`'a hem immutable `:<sha>` hem mutable `:latest`
  etiketiyle push ediyor (yalnızca `GITHUB_TOKEN`, yeni hesap/secret yok), sonra
  `RENDER_DEPLOY_HOOK_URL` secret'ı tanımlıysa Render deploy hook'unu `curl` ile tetikliyor
  (tanımlı değilse hata vermeden uyarıp geçiyor). `.github/render.yaml` (Render Blueprint, agent'ın
  yazma kapsamı repo kökünü kapsamadığı için kasıtlı olarak burada — Render'a bağlanırken "render.yaml
  Path" alanına elle girilmeli): `healthCheckPath: /health/ready` (US-005), `autoDeploy: false`
  (çift deploy yarışını önlemek için), tek env var `ConnectionStrings__SpotifyPlaylistCache`
  (`sync: false` — değer Render Dashboard'unda elle girilir, asla commit edilmez). SEC-001
  (Spotify/AI credential'ı Render'da yok) `DeployWorkflowSecretIsolationTests` ile regresyona
  bağlandı. Mehmet Render Blueprint'i bağladı, connection string'i salt-okunur rolle girdi;
  `https://thebluesland.onrender.com` canlı ve `/health/ready` gerçek serviste doğrulandı. Tek
  gerçekten hiç denenmemiş kalan madde: Render'ın native rollback'i (mimari destekleniyor, bir
  olayla tetiklenip doğrulanmadı). `dotnet build`/`dotnet test`: 167/167 yeşil, `dotnet format
  --verify-no-changes` temiz.
- **US-013 — Pull request CI pipeline'ı.** `ci.yml` altı bağımsız job'a çıkarıldı:
  `content-validation` (US-007, değişmedi), `build-and-test` (restore/Release build/`dotnet format
  --verify-no-changes` her `.csproj` için/Testcontainers unit+integration testler),
  `playwright-smoke` (yeni `tests/TheBluesland.E2ETests` — `WebHostFactory` in-process host'una
  karşı gerçek Chromium ile home page + detay sayfası navigasyonu), `tailwind-build` (yeni
  `src/TheBluesland.Web/package.json` — Tailwind CSS 4 npm build zinciri; ilk pinlenen `4.0.0` bir
  CLI bug'ına çarptı — `Missing field 'negated' on ScannerOptions.sources` — `4.3.3`'e ve committed
  `package-lock.json`+`npm ci`'ye geçilerek çözüldü), `dependency-secret-scan`
  (`dependency-review-action` + `dotnet list package --vulnerable` + `gitleaks-action`), ve
  `docker-build` (yeni non-root, multi-stage `src/TheBluesland.Web/Dockerfile` — context repo kökü,
  Tailwind çıktısını ayrı bir stage'de derleyip publish'e overlay ediyor). Hiçbir job
  `SPOTIFY_CLIENT_ID`/`SPOTIFY_REFRESH_TOKEN`/`NEON_SYNC_CONNECTION_STRING`'i bildirmiyor
  (`CiWorkflowSecretIsolationTests` ile regresyona bağlandı); integration testler Testcontainers'a
  karşı çalışıyor, gerçek Neon'a hiç dokunmuyor. Branch protection toggle'ı (US-007'deki gibi)
  Mehmet'in elle yapacağı tek seferlik adım olarak kaldı. `dotnet build`/`dotnet test`: 152/152
  yeşil; `docker build` ve `npm ci && npm run build:css` ana oturumda gerçekten çalıştırılıp
  doğrulandı (agent sandbox'ında npm/docker/pwsh yasak).
- **US-012 — Güvenlik başlıkları, CSP ve embed URL doğrulaması.** Her yanıta (2xx/404 dahil)
  `Content-Security-Policy`/`X-Content-Type-Options`/`Referrer-Policy`/`Permissions-Policy`
  ekleyen bir middleware (`WebHostFactory`); CSP'nin tek Spotify-özel izni `frame-src
  https://open.spotify.com` (click-to-load embed için), `img-src` ise yalnızca kapak görseli
  CDN'i `i.scdn.co`'yu ekliyor. `SpotifyPlaylistIdFormat` (22 karakter base62), US-006'nın
  build-time doğrulamasıyla aynı kurala dayanıp render zamanında da tekrar uygulanıyor — böylece
  CI'ı bir şekilde atlatan bozuk bir `spotifyPlaylistId`, cache satırı playable görünse bile
  embed/"Open in Spotify" URL'ine hiç dönüşemiyor (defense in depth). Ham HTML sanitization zaten
  US-010'daki Markdig `DisableHtml()` ile karşılanıyordu — yeni kod gerekmedi, yalnızca kapsam
  doğrulandı. Testcontainers Postgres'e karşı 148/148 test yeşil.
- **US-011 — SEO metadata, sitemap ve sosyal kartlar.** Her indexlenebilir sayfada `PageMetadata`
  bileşeni (description/canonical/OG/Twitter, tek `<HeadContent>` slotu altında — Blazor'un
  `HeadOutlet`'inin ayrı `<HeadContent>` bloklarını birleştirmek yerine üzerine yazdığı ampirik
  olarak keşfedildi). Ana sayfanın canonical'ı aktif filtre query-string'inden bağımsız hep `/`.
  `/sitemap.xml` ve `/robots.txt` (yalnızca published, sorgu varyasyonu yok), `WebSite`/
  `CollectionPage`/`BreadcrumbList` JSON-LD (track verisi sızdırmadığı testle sabitlendi). OG görseli
  SixLabors.ImageSharp ile sunucu tarafında üretiliyor (SkiaSharp'a göre native bağımlılıksız,
  Split License uygun); font yoksa arka plan-only'e zarif düşüş. Site URL'i istekten türetiliyor
  (config yok) — Render'ın TLS-sonlandırma riski US-014'e not edildi. Testcontainers Postgres'e
  karşı 137/137 test yeşil.
- **US-010 — Playlist detay sayfası: curator note, cache alanları, Spotify embed.** Curator note
  artık Markdig ile (raw HTML devre dışı) sanitize edilmiş HTML olarak render ediliyor; review'da
  bulunan bir XSS açığı (markdown link syntax'ında `javascript:`/`data:` şeması) ayrıca kapatıldı.
  Click-to-load embed, US-009'daki gibi bilinçli olarak zero-JS statik `?listen=true` query-flag
  yaklaşımıyla yapıldı (Interactive Server eklenmedi). "Open in Spotify" linki, en fazla 3 ilişkili
  playlist (paylaşılan etiket sayısına göre, `RelatedPlaylistRanking`), ve `previousSlugs` için
  gerçek 301 redirect eklendi. Review'da bulunan bir diğer hata (boş `era`'nın yanlışlıkla
  "paylaşılan etiket" sayılması) da düzeltildi — dört yerdeki tag mantığı `PlaylistTags` altında
  birleştirildi. Testcontainers Postgres'e karşı 116/116 test yeşil.
- **US-009 — Ana sayfa: katalog, kartlar ve filtreleme.** Ana sayfa filtresi, spec'in "interactive
  filter island" ifadesine rağmen bilinçli olarak `@rendermode InteractiveServer` kullanmıyor — düz
  `<form method="get">` + `[SupplyParameterFromQuery]`, static SSR'da sunucu tarafında filtreleniyor;
  URL query string tüm filtre durumu (JS/SignalR gerekmiyor). `PlaylistFilter` (OR-içi/AND-arası),
  `PlaylistCatalogueSort` (displayOrder artan, sonra publishedAt azalan) saf fonksiyonlar olarak
  ayrıştırıldı. US-006 validator'a featured-cap-4 kuralı eklendi (yalnızca published dosyalar
  sayılıyor — review'da bulunan bir hata düzeltildi) ve `publishedAt` artık format da doğrulanıyor
  (başka bir review bulgusu). Testcontainers Postgres'e karşı 88/88 test yeşil.
- **US-008 — Blazor Web App iskeleti (static SSR, sağlık kontrolleri, routing).** Ana sayfa
  (`HomePage.razor`, yayınlanmış katalog listesi, filtre yok — US-009), `/about`/`/privacy`/`/terms`
  (yer tutucu editoryal metin), `/playlists/{slug}` ve genel `NotFound` artık gerçek 404 status
  kodu dönüyor (`ResponseStatusCode.razor` bileşeni, iki yerde paylaşılıyor). Batched cache lookup
  (`PlaylistCacheLookup.GetSnapshotsAsync`) eklendi; review'da bulunan bir duplicate-id crash'i
  düzeltildi. Testcontainers Postgres'e karşı 71/71 test yeşil.
- **US-007 — İçerik doğrulamasının CI'a bağlanması.** `.github/workflows/ci.yml` (yeni) —
  `content-validation` job'u, `Program.cs`'e eklenen `validate-content` CLI argümanı üzerinden
  `ContentValidationCli`'yi gerçek `content/playlists/` dizinine karşı çalıştırıyor; ihlalde
  non-zero exit + dosya/alan/kural satırları. Hiçbir secret bildirmez. Branch protection toggle'ı
  (GitHub repo ayarları) Mehmet'in elle yapması gereken tek seferlik adım olarak kaldı. Testcontainers
  Postgres'e karşı 61/61 test yeşil.
- **US-006 — Editoryal içerik şeması ve v0.2 taksonomi doğrulaması.** `src/TheBluesland.Web/Content`
  içinde `PlaylistContentValidator` — `PlaylistContentReader`'dan (render yolu) bağımsız, ayrı bir
  doğrulama yolu; her `content/playlists/*.md` dosyasını gerekli alan/format/aralık kurallarına ve
  onaylı v0.2 taksonomi listelerine (spec 8.2-8.5) karşı doğruluyor, dosyalar arası slug/
  spotifyPlaylistId tekilliğini kontrol ediyor; sonuç dosya+alan+neden taşıyan yapılandırılmış bir
  liste (US-007'nin CI raporlaması buna kolayca oturacak). Testcontainers Postgres'e karşı 55/55
  test yeşil.
- **US-005 — Cache eksik/bayat olduğunda zarif düşüş.** `src/TheBluesland.Web` (Blazor Web App) —
  `PlaylistCacheLookup`, `PlaylistContentHealthCheck`, `PlaylistDetailPage`/`PlaylistCard`
  bileşenleri; DB erişilemese veya cache satırı olmasa bile editoryal içerik 200 ile render
  ediliyor, `/health/ready` yalnızca içerik doğrulamasına bakıyor. ADR-0003 ile CLAUDE.md
  şablonundan sapma resmî onay aldı (Mehmet, 2026-09-03). Testcontainers Postgres'e karşı 43/43
  test yeşil.
- **US-004 — Aylık GitHub Actions senkron workflow'u.** `.github/workflows/sync-spotify.yml` —
  cron + `workflow_dispatch`, secret scope tek job'a sınırlı.
- **US-003 — Spotify Web API senkron aracı.** `tools/spotify-playlist-fetcher` — front-matter
  okuma, OAuth refresh-token exchange, playlist/sanatçı çekme (track-seviyesi veri persist
  edilmiyor), idempotent upsert. Gerçek Testcontainers Postgres'e karşı 27/27 test yeşil.
- **US-001 — Spotify cache tablosu şeması.** `src/TheBluesland.Data` (entity, DbContext, migration),
  gerçek Postgres'e (Testcontainers/OrbStack) karşı doğrulandı.
- **US-002 — Salt-okunur/yazma-yetkili DB rolleri.** `create-spotify-cache-roles.sql` +
  Testcontainers testleriyle rol ayrımı kanıtlandı; README.md'de belgelendi.
- Geliştirme makinesi kurulumu: Homebrew, OrbStack (Docker daemon), `psql` (libpq), `gh` kuruldu —
  Testcontainers tabanlı testler artık gerçek Postgres'e karşı çalışıp doğrulanabiliyor.
- v0.1 spec (`docs/business-technical-specification.v0.1.md`) — artık geçersiz, tarihsel referans
  olarak korunuyor.
- v0.2 spec (`docs/business-technical-specification.md`) — hibrit mimariye göre yeniden yazıldı.
- `docs/adr/0002-spotify-veri-mimarisi.md` — hibrit Spotify Web API + Postgres cache kararı.
- `docs/adr/0003-mimari-kapsam.md` — CLAUDE.md şablonundan sapmanın belgelenmesi.
- `docs/product/backlog.md` — US-001..US-015 implementasyon-sıralı hikaye kırılımı.
