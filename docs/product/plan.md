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

- **Faz 2 — İçerik doğrulama (aktif).** US-006 (şema + taksonomi doğrulama) sırada — backend-dev'e
  atandı. Ardından US-007 (CI'a bağlama).
- **Faz 3 — Blazor scaffold.** US-008 (iskelet + sağlık kontrolleri), US-009 (ana sayfa/filtre),
  US-010 (detay sayfası), US-011 (SEO/sitemap/sosyal kart), US-012 (güvenlik başlıkları/CSP).
- **Faz 4 — CI/CD.** US-013 (PR pipeline'ı), US-014 (Render+Neon deploy pipeline'ı).
- **Faz 5 — Launch içeriği.** US-015 (sekiz playlist'in editoryal tamamlanması) — Faz 1-4 ile
  paralel yürütülebilir, ama public launch bunu bekler.

## Tamamlanan

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
