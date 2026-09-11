# ADR-0002 — Spotify veri mimarisi: hibrit cache (Web API + Postgres, aylık senkron)

Durum: Kabul edildi

2026-09-07 güncellemesi (US-023, Mehmet'in açık kararı): Yalnız `mixed-era` taşıyan playlist'ler
Spotify albüm yayın tarihlerinden hesaplanan dönemleri otomatik kullanır. Sync, sanatçıları
okuduğu aynı sayfalı istekte dönemleri hesaplar; yalnız `computed_eras` etiket dizisini cache'e
yazar. Ham tarihler/track kayıtları saklanmaz. Açıkça atanmış dönemler önceliklidir; yetersiz
veride editoryal etiket kalır. Bu istisna, aşağıdaki ilk kararın era alanını elle yönetme şartını
daraltır; ayrıntılar spec 8.6 ve `docs/automatic-eras.md` içinde.

## Bağlam

v0.1 spec'i "embed-first" ilkesi üzerine kuruluydu: veritabanı yok, Spotify Web API entegrasyonu
yok, tüm playlist verisi (editoryal + Spotify'dan görünen ad/kapak/track sayısı) elle,
version-controlled Markdown/YAML içinde tutuluyordu. Bu, Spotify politika riskini ve operasyonel
karmaşıklığı minimize ediyordu ama her playlist için Mehmet'in Spotify'daki gerçek ad, açıklama,
kapak görseli ve track sayısını elle kopyalayıp güncel tutmasını gerektiriyordu.

Proje sahibi bu kararı bilinçli olarak tersine çevirdi: Spotify Web API doğrudan entegre edilecek,
Spotify-kaynaklı alanlar bir veritabanında saklanacak ve ayda bir otomatik senkronize edilecek.
Editoryal alanlar (mood/genre/occasion/era/curator note/slug) değişmeden Markdown/YAML'de,
version-controlled ve elle yönetilen içerik olarak kalıyor.

Bu ADR, ortaya çıkan hibrit mimarinin somut kararlarını ve reddedilen alternatifleri kayıt altına
alır. Bkz. `docs/business-technical-specification.md` bölüm 9.4, 11, 12, 18.4, 19.

## Karar

1. **Veri ayrımı, `spotifyPlaylistId` üzerinden join.** Editoryal alanlar hâlâ
   `content/playlists/*.md` içinde, elle yazılıyor. Spotify-kaynaklı alanlar (`name`,
   `description`, `cover_image_url`, `track_count`, `artists`, `spotify_snapshot_id`, `synced_at`,
   `is_available`) `spotify_playlist_cache` tablosunda, yalnızca senkron aracı tarafından yazılıyor.
   Web uygulaması bu tabloyu **yalnızca okuyor**.

2. **Track listesi hiçbir zaman kalıcı saklanmıyor.** Senkron aracı Spotify Web API'den playlist'in
   track'lerini okur (track sayısını ve sanatçı listesini hesaplamak için) ama bunları bellekte
   işleyip atar; hiçbir track başlığı, track ID'si, süre, ISRC veya audio-feature verisi veritabanına
   yazılmaz. Bu, v0.1'in "track listesi kalıcı saklanamaz" ilkesinin daraltılmış ama korunmuş
   hâlidir — yasak olan track-seviyesi veridir, playlist-seviyesi metadata değil.

3. **Senkron ayda bir çalışır, in-process `BackgroundService` DEĞİL, ayrı bir GitHub Actions
   scheduled workflow'dur (`sync-spotify.yml`).** Gerekçe: Render'ın ücretsiz instance'ı sürekli
   ayakta kalmıyor (inaktivite sonrası uyku moduna geçiyor); bir uygulama-içi zamanlayıcı bu
   koşullar altında güvenilmez şekilde tetiklenir veya hiç tetiklenmez. GitHub Actions cron,
   uygulamanın çalışıp çalışmadığından bağımsız, garanti şekilde ayda bir çalışır ve ücretsizdir
   (public repo). Ayrıca bu ayrım, Spotify credential'larının production runtime'a hiç girmemesini
   doğal olarak sağlar (bkz. madde 4).

4. **Credential izolasyonu: Spotify ve DB-yazma credential'ları yalnızca GitHub Actions repository
   secrets'ında yaşar.** `SPOTIFY_CLIENT_ID`, `SPOTIFY_REFRESH_TOKEN` (Authorization Code + PKCE ile
   önceden, tek seferlik, Mehmet'in kendi hesabıyla elde edilmiş) ve `NEON_SYNC_CONNECTION_STRING`
   yalnızca `sync-spotify.yml` workflow'una scope edilmiş secret'lardır. Production Render ortamı
   yalnızca bir salt-okunur (mümkünse Neon role ile scope edilmiş) Postgres connection string'i
   tutar; hiçbir Spotify veya AI credential'ı içermez. Bu, `ci.yml` ve `deploy.yml`'in de bu
   secret'lara erişememesi anlamına gelir.

5. **Senkron kaynağı editoryal içeriktir, tersi değil.** Senkron aracı hangi playlist'lerin var
   olduğuna kendi keşfetmez; `content/playlists/*.md` içindeki `spotifyPlaylistId` değerlerini okur
   ve yalnızca bunlar için Spotify'dan veri çeker. Bu, editoryal onay sürecinin (PR ile playlist
   ekleme) tek playlist-ekleme yolu olarak kalmasını garanti eder.

6. **Hosting: Render (web) + Neon (Postgres) + GitHub Actions (cron), $0/ay.** v0.1'in Azure
   Container Apps kararı geçersiz kılındı.

## Alternatifler ve neden reddedildiler

### Mimari alternatifler

- **Durum quo (v0.1, embed-only, DB yok):** Reddedildi — proje sahibinin kararı. Spotify-kaynaklı
  alanların elle güncel tutulması, playlist sayısı arttıkça (özellikle Spotify tarafında playlist
  adı/kapak/track sayısı değiştikçe) ölçeklenmiyor.
- **Senkron'u web uygulaması içinde bir `BackgroundService`/zamanlanmış görev olarak çalıştırmak:**
  Reddedildi. Render'ın ücretsiz tier'ı sürekli ayakta durmayı garanti etmiyor (cold start / uyku),
  bu yüzden uygulama-içi bir zamanlayıcı ayda bir güvenilir şekilde tetiklenmeyebilir. Ayrıca bu,
  Spotify credential'larının production runtime ortamına (Render env vars) girmesini gerektirirdi —
  SEC-001'in "production'da Spotify credential'ı yok" ilkesini ihlal ederdi.
  GitHub Actions cron, çalışma garantisini ve credential izolasyonunu aynı anda çözüyor.
  Trade-off: senkron gecikmesi (bir sonraki cron'a kadar en fazla ~30 gün) kabul edilen bir risktir
  (spec bölüm 21).
- **Track listesini de kalıcı saklamak (tam bir Spotify aynası):** Reddedildi, sabit kısıt. Spotify
  Developer Policy'nin ML/içerik kısıtlaması ayrı bir konu olsa da, track listesinin kalıcı
  saklanması "Spotify Content'in bağımsız bir kopyasını tutma" riskini taşır ve MVP'nin ihtiyacı
  yok — track sayısı ve sanatçı listesi yeterli.

### Hosting alternatifleri (brief'ten)

- **Railway:** Reddedildi — gerçek/kullanılabilir bir ücretsiz plan yok.
- **Netlify / Vercel:** Reddedildi — kalıcı bir .NET server process çalıştıramıyorlar (yalnızca
  statik hosting / kısa ömürlü function'lar); Blazor Interactive Server'ın gerektirdiği kalıcı
  SignalR bağlantısını destekleyemezler.
- **Azure Container Apps (v0.1'in orijinal kararı):** Reddedildi — Mehmet'in ödeme yapacak bütçesi
  yok; free-tier limitleri, Blazor Interactive Server'ın kalıcı SignalR bağlantı ihtiyacına rahatça
  uymuyor.

## Sonuçlar

- Yeni bir paylaşılan proje ortaya çıkıyor: `src/TheBluesland.Data` (EF Core entity + migration),
  hem `TheBluesland.Web` (okuyucu) hem `tools/spotify-playlist-fetcher` (yazıcı) tarafından
  referans alınıyor. Bu, CLAUDE.md'deki çok-istemcili `Domain`/`Shared` ayrımıyla karıştırılmamalı
  — bkz. ADR-0003. Tek gerekçesi: iki bağımsız process'in aynı DB şemasını paylaşması gerekiyor.
- Yeni bir operasyonel yüzey: aylık cron job'ının başarısız olup olmadığının izlenmesi gerekiyor
  (GitHub Actions workflow failure bildirimleri yeterli, ayrı bir monitoring aracı MVP'de gerekli
  değil).
- Yeni bir kabul edilen risk: veri en fazla ~30 gün bayat olabilir (spec FR-024, bölüm 21). Bu,
  ziyaretçiye yönelik editoryal deneyimi bozmaz çünkü editoryal alanlar (curator note, tag'ler)
  zaten anlık günceldir; yalnızca Spotify-kaynaklı yardımcı alanlar (kapak, track sayısı) gecikmeli
  olabilir.
- Neon ve Render'ın ücretsiz tier limitleri (cold start, olası compute-hour sınırları) kabul
  edilmiş MVP trade-off'larıdır; mimari her iki sağlayıcıya da kilitli değildir (spec bölüm 19).
- SEC-001 revize edildi: production'da "hiç credential yok" değil, "hiç Spotify/AI credential'ı
  yok, yalnızca salt-okunur bir DB connection string var" hâline geldi.

## Sonraki karar notu (2026-09-03)

`docs/adr/0005-ai-kurator-notu-siniri.md`, bu ADR'ın veri sınırını **değiştirmez** ama SEC-008'i
daraltır: bu ADR'ın madde 1'inde sayılan alanlardan yalnızca dördü (`name`, `description`,
`track_count`, `artists`) AI'ya girdi olarak verilebilir hâle geldi. Bu ADR'ın madde 2'si (track
listesi hiçbir zaman kalıcı saklanmaz veya işlenmez) ve madde 4'ü (credential izolasyonu) aynen
geçerlidir; ADR-0005 madde 4'ün desenini AI sağlayıcı key'i için tekrar eder
(`GEMINI_API_KEY` yalnızca `suggest-curator-note.yml` workflow'una scope edilir). AI önerisi
`spotify_playlist_cache` tablosuna yazılmaz — bu tablo hâlâ yalnızca senkron aracı tarafından
yazılan bir cache'tir.

## Sonraki karar notu (2026-09-09) — sync-spotify.yml artık repo'ya yazabiliyor

`sync-spotify.yml`, bu projedeki **ilk** `contents: write`/`pull-requests: write` iznine sahip
workflow oldu (`docs/specs/auto-unpublish-private-playlists.md`). Gerekçe: bir playlist Spotify'da
private yapıldığında, editoryal `content/playlists/*.md` dosyasının `status: published` alanı
otomatik `draft`'a çevriliyor — çünkü yayın durumu daha önce yalnızca elle değiştirilebiliyordu ve
Spotify'ın kendi görünürlük bayrağını hiç takip etmiyordu (gerçek bir olayla keşfedildi:
`my-shazam-tracks`).

Bu, ADR-0005'in "otomasyon içeriğe asla sessizce karar vermez" ilkesini **çiğnemiyor**: ADR-0005
üretken (AI tarafından yazılan) içerik hakkındaydı, bu ise deterministik bir gerçek kontrolü
(Spotify `public` alanı false ise dosyayı draft yap) — yargı gerektirmiyor, tek yönlü (yalnızca
published→draft, asla tersi), ve `main`'e doğrudan push yerine PR açıp CI geçince otomatik merge
ediyor (branch protection bypass edilmiyor). `ci.yml` ve `deploy.yml` hâlâ `contents: read`.

Kabul edilen yeni operasyonel bağımlılık: bu workflow'un `gh pr merge --auto` adımının çalışması
için repo ayarlarında "Allow auto-merge" (Settings > General) açık olmalı — kod dışı, tek seferlik
bir adım, SEC-001/varolan secret-scoping notlarıyla aynı sınıfta. Ayrıca bu workflow'un kendi
`GITHUB_TOKEN`'ıyla PR açabilmesi için "Allow GitHub Actions to create and approve pull requests"
(Settings > Actions > General > Workflow permissions) açık olmalı — gerçek bir olayla keşfedildi
(2026-09-10, `the-songs-i-want-to-be` private yapılınca ilk otomatik çalışma bu adımda başarısız
oldu; PR #70 ile elle düzeltildi, ardından ayar açıldı).

## Sonraki karar notu (2026-09-10) — Spotify rate limit dayanıklılığı

İki gerçek olayla keşfedildi: `sync-spotify.yml` bazen Spotify'ın 429 yanıtı istemci içi 120
saniyelik yeniden deneme bütçesinin çok üzerinde bir bekleme istediğinde (gözlemlenen bir örnekte
~83000 saniye, yaklaşık 23 saat) tamamen hata verip çıkıyordu — `SpotifyPlaylistClient` bu süre
kadar job içinde beklemek yerine (30 dakikalık job timeout'unu aşacağı için) hata fırlatmayı tercih
ediyor. Bu iki dayanıklılık kararını doğurdu:

1. **`PlaylistCacheSyncService.SyncAsync` artık playlist'leri en son senkronize edilenden en eskiye
   değil, en eski `synced_at`'tan en yeniye doğru işliyor** (hiç senkronize edilmemiş olanlar en
   önde). Zaten her playlist ayrı ayrı kaydediliyordu (`SaveChangesAsync` döngü içinde) — bu, bir
   çalışma yarıda kesildiğinde önceki ilerlemenin kaybolmadığını garanti ediyordu ama hangi
   playlist'lerin "sırada" kaldığını belirlemiyordu. Yeni sıralamayla, bir çalışma rate limit
   yüzünden yarıda kesilirse bir sonraki çalışma otomatik olarak tam da yetişilemeyen playlist'lerle
   başlıyor — ayrı bir "hold/pending" durumu veya yeni bir tablo/alan eklemeden, var olan
   `synced_at` alanından doğal olarak çıkıyor.
2. **Yeni `retry-rate-limited-sync.yml` workflow'u**, `sync-spotify.yml`'in son çalışmasını 3 saatte
   bir kontrol ediyor; hata mesajı tam olarak bilinen rate-limit metnini içeriyorsa ve Spotify'ın
   istediği bekleme süresi geçmişse `sync-spotify.yml`'i otomatik yeniden tetikliyor. Başka bir
   sebepten başarısız olursa (build hatası, izin sorunu vb.) dokunmuyor — bu, gerçek bir hatanın
   sonsuza kadar sessizce yeniden denenip insana hiç görünmemesini engelliyor. Bu workflow hiçbir
   Spotify/DB credential'ı tutmuyor, yalnızca `actions: write` (workflow tetikleme) izni var.

Bu, madde 5'i (senkron kaynağı editoryal içeriktir) veya madde 2'yi (track listesi kalıcı
saklanmaz) değiştirmiyor — yalnızca hangi sırayla ve ne zaman senkron denendiğiyle ilgili,
operasyonel bir dayanıklılık kararı.

## Sonraki karar notu (2026-09-11) — production web app artık ikinci bir DB credential'ı tutuyor

`docs/specs/visitor-and-playlist-click-analytics.md`: Mehmet ziyaretçi sayısı ve playlist
tıklamalarını (`page_view`/`spotify_click`) ölçmek istedi; üçüncü taraf analytics script'i veya CSP
gevşetmesi yerine kendi Postgres'ine (Neon) sunucu tarafında loglama seçildi. Bu, **madde 4'ün
"production yalnızca salt-okunur bir connection string tutar" ifadesini daraltıyor**: web app artık
ikinci, tamamen ayrık bir yazma credential'ı da tutuyor.

Bu, madde 4'ün gerekçesini (Spotify/AI credential'ı hiç production'a girmesin) **ihlal etmiyor** —
yeni credential'ın `spotify_playlist_cache`'le hiçbir ilgisi yok, hiçbir Spotify/AI erişimi yok.
Sınır aynı titizlikle korundu, sadece iki ayrı yazma yüzeyi oldu:

- Yeni, ayrı bir `AnalyticsDbContext` (bkz. `TheBluesland.Data`), `TheBlueslandDbContext`'ten tamamen
  bağımsız — aynı context'e yeni bir `DbSet` eklenmedi, çünkü bu iki connection string'in (biri
  salt-okunur cache, biri yazma-yetkili analytics) aynı context üzerinden ayrıştırılmasını
  imkansız kılardı.
- Yeni `analytics_writer` rolü (`create-analytics-role.sql`) yalnızca `page_view_events` tablosuna
  `INSERT` yapabiliyor — o tabloyu bile `SELECT` edemiyor (Mehmet kendi admin oturumuyla okuyor),
  ve `spotify_playlist_cache`'e hiçbir erişimi yok. Testcontainers'lı bir test
  (`AnalyticsRoleTests.cs`) bunun dördünü de gerçek bir Postgres'e karşı kanıtlıyor — yalnızca
  yorum satırı değil.
- Hiçbir ham IP adresi saklanmıyor, hiçbir cookie kullanılmıyor: `visitor_hash = SHA256(pepper +
  UTC-tarih + ip + user-agent)` — tarih bileşeni yüzünden aynı ziyaretçi her gün farklı hash'e sahip
  oluyor, bu da günlük yaklaşık tekil ziyaretçi sayısını (`COUNT(DISTINCT visitor_hash)`) ham veriyi
  hiç saklamadan mümkün kılıyor.
- Yazma yolu tamamen "fire-and-forget": analytics DB'si erişilemez olduğunda sayfa sunumu hiç
  etkilenmiyor (cache okuma yolunun mevcut graceful-degradation ilkesinin bir yazma sürümü).
