# Plan — TheBluesland

Kaynak: `docs/product/backlog.md`, `docs/business-technical-specification.md` (v0.2).

## Aktif

- **Faz 0 — Spec/mimari onayı.** v0.2 spec, ADR-0002 ve ADR-0003 yazıldı. CLAUDE.md/ADR-0001
  çok-istemcili şablonundan sapma (ADR-0003), Mehmet'in 2026-09-03'teki US-005 devam talimatıyla
  onaylandı. Açık kalan onaylar:
  - v0.2 taksonomisi (spec bölüm 8) — nihai değer listesi.
  - Section 7'deki iki taslak playlist için önerilen başlık/etiket/not — nihai onay.

## Sıradaki

Sıra, `docs/product/backlog.md`'deki bağımlılık zincirini takip eder.

- **Faz 3 — Blazor scaffold (sırada).** US-008 (iskelet + sağlık kontrolleri), US-009 (ana sayfa/filtre),
  US-010 (detay sayfası), US-011 (SEO/sitemap/sosyal kart), US-012 (güvenlik başlıkları/CSP).
- **Faz 4 — CI/CD.** US-013 (PR pipeline'ı), US-014 (Render+Neon deploy pipeline'ı).
- **Faz 5 — Launch içeriği.** US-015 (sekiz playlist'in editoryal tamamlanması) — Faz 1-4 ile
  paralel yürütülebilir, ama public launch bunu bekler.

## Tamamlanan

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
