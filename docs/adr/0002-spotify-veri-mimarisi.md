# ADR-0002 — Spotify veri mimarisi: hibrit cache (Web API + Postgres, aylık senkron)

Durum: Kabul edildi

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
(`ANTHROPIC_API_KEY` yalnızca `suggest-curator-note.yml` workflow'una scope edilir). AI önerisi
`spotify_playlist_cache` tablosuna yazılmaz — bu tablo hâlâ yalnızca senkron aracı tarafından
yazılan bir cache'tir.
