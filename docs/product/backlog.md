# Backlog — TheBluesland

Kaynak: `docs/business-technical-specification.md` (v0.2), `docs/adr/0002-spotify-veri-mimarisi.md`,
`docs/adr/0003-mimari-kapsam.md`.

Sıralama, uygulanabilir bağımlılık zincirini takip eder: DB şeması → senkron aracı → içerik
doğrulama → Blazor scaffold → CI pipeline → deploy → launch içeriği. Her hikaye tek oturumda
bitecek büyüklükte tutulmuştur; daha büyük parçalar (ör. "Blazor scaffold") birden fazla hikayeye
bölünmüştür.

---

## US-001 — Spotify cache tablosu şeması

Kullanıcı olarak geliştirici, `spotify_playlist_cache` tablosunun EF Core entity ve migration'ını
istiyorum ki hem web uygulaması hem senkron aracı aynı şemayı paylaşarak Spotify-kaynaklı alanları
okuyup yazabilsin.

Kabul kriterleri:

- [x] `src/TheBluesland.Data` projesi eklendiğinde, `spotify_playlist_cache` tablosunu şu kolonlarla
      tanımlayan bir EF Core migration üretilmiş olur: `spotify_playlist_id` (PK, text),
      `name`, `description` (nullable), `cover_image_url` (nullable), `track_count` (integer),
      `artists` (text array), `spotify_snapshot_id` (nullable), `synced_at` (timestamptz),
      `is_available` (boolean).
- [x] Migration bir yerel/test Postgres'e uygulandığında hatasız tamamlanır.
- [x] Şemaya track başlığı, track ID, süre, ISRC veya audio-feature alanı eklenmediği bir unit
      test ile doğrulanır (entity'nin property listesi enumere edilip beklenen kolon setiyle
      karşılaştırılır).

**Durum: Tamamlandı.** `dotnet ef migrations add` ile üretilen migration, gerçek Postgres'e
(Testcontainers, OrbStack) karşı doğrulandı.

Kapsam dışı: editoryal içerik tabloları (Markdown/YAML olarak kalıyor), senkron aracının kendisi
(US-003).
Öncelik: Must
Platform: web

---

## US-002 — Salt-okunur ve yazma-yetkili DB rolleri

Kullanıcı olarak geliştirici, Neon üzerinde ayrı bir salt-okunur rol ve bir yazma-yetkili rol
istiyorum ki production web uygulaması yalnızca okuma yapabilsin, senkron aracı ise yazabilsin.

Kabul kriterleri:

- [x] Neon projesinde `spotify_playlist_cache` üzerinde yalnızca SELECT yetkisi olan bir rol ve
      buna karşılık gelen bir connection string tanımlandığında, bu connection string ile yazma
      denemesi (INSERT/UPDATE) reddedilir.
- [x] Yazma-yetkili rolün connection string'i yalnızca GitHub Actions repository secret'ı olarak
      saklanır; README bu ayrımı ve hangi secret'ın nerede kullanıldığını açıklar.

**Durum: Tamamlandı.** Rol/GRANT tanımı `src/TheBluesland.Data/Scripts/create-spotify-cache-roles.sql`
(standart Postgres sözdizimi, Neon'a doğrudan uygulanabilir). Testcontainers ile gerçek Postgres'e
karşı doğrulandı: readonly rol INSERT/UPDATE'de reddediliyor, SELECT çalışıyor; readwrite rol
INSERT yapabiliyor. Ayrım README.md'de belgelendi. Gerçek Neon projesinin açılması ve script'in
orada çalıştırılması Mehmet'in tek seferlik, kod dışı adımı olarak kalıyor.

Kapsam dışı: Render ortam değişkeni olarak connection string'in gerçekten set edilmesi (US-014,
deploy hikayesi).
Öncelik: Must
Platform: web

---

## US-003 — Spotify Web API senkron aracı (`tools/spotify-playlist-fetcher`)

Kullanıcı olarak proje sahibi (Mehmet), `content/playlists/*.md` içindeki her `spotifyPlaylistId`
için Spotify Web API'den ad, açıklama, kapak görseli URL'i, track sayısı ve sanatçı listesini çeken
bir konsol aracı istiyorum ki bu alanları elle güncel tutmak zorunda kalmayayım.

Kabul kriterleri:

- [x] Araç, `content/playlists/*.md` dosyalarındaki her `spotifyPlaylistId` için Spotify Web API'ye
      istek attığında ve başarılı yanıt aldığında, `spotify_playlist_cache` tablosuna bir satır
      upsert eder (`name`, `description`, `cover_image_url`, `track_count`, `artists`,
      `spotify_snapshot_id`, `synced_at`, `is_available = true`).
- [x] Spotify API bir playlist için 404/erişilemez yanıt döndürdüğünde, ilgili satır
      `is_available = false` olarak güncellenir; satır silinmez.
- [x] Araç, hesapladığı `track_count` ve `artists` dışında hiçbir track-seviyesi alanı (başlık, ID,
      süre) hiçbir kalıcı depoya yazmadığı bir regresyon testiyle doğrulanır.
- [x] Aynı fixture verisiyle araç iki kez art arda çalıştırıldığında, her playlist için tek bir
      satır kalır (idempotent upsert), ikinci çalıştırma yeni satır oluşturmaz.
- [x] Araç, kaydedilmiş/mock bir Spotify API yanıtına karşı (gerçek API'ye karşı değil) test edilir.

**Durum: Tamamlandı.** `tools/spotify-playlist-fetcher` — front-matter okuma (YamlDotNet), refresh
token → access token, playlist özeti + paginated track sanatçı toplama (`fields` parametresiyle
daraltılmış, track-seviyesi veri kalıcı hiçbir yere yazılmıyor), idempotent upsert. 27/27 test
gerçek Testcontainers Postgres'e karşı yeşil, tüm HTTP çağrıları mock'landı.

Kapsam dışı: GitHub Actions workflow olarak zamanlanması (US-004), gerçek Spotify hesabıyla
Authorization Code + PKCE token alma akışının UI'ı (bu, Mehmet tarafından tek seferlik, araç dışı
bir adımda yapılır).
Öncelik: Must
Platform: web

---

## US-004 — Aylık GitHub Actions senkron workflow'u

Kullanıcı olarak proje sahibi (Mehmet), senkron aracının ayda bir otomatik çalışmasını ve
credential'larının yalnızca GitHub Actions'ta yaşamasını istiyorum ki production web uygulamasının
hiçbir Spotify credential'ı olmasın.

Kabul kriterleri:

- [x] `.github/workflows/sync-spotify.yml` dosyası eklendiğinde, aylık bir `cron` tetikleyicisi ve
      ayrıca manuel `workflow_dispatch` tetikleyicisi tanımlıdır.
- [x] Workflow, `SPOTIFY_CLIENT_ID`, `SPOTIFY_REFRESH_TOKEN`, `NEON_SYNC_CONNECTION_STRING`
      secret'larını yalnızca kendi job'ı içinde kullanır; bu secret'lar `ci.yml` veya `deploy.yml`
      workflow'larından erişilebilir değildir (repo secret scope'u ile doğrulanır).
- [x] Senkron aracı bir hata ile (kimlik doğrulama hatası, beklenmeyen API yanıtı, DB'ye
      erişilemiyor) sonlandığında, workflow run'ı başarısız (non-zero exit) olarak işaretlenir ve
      GitHub Actions arayüzünde görünür bir hata durumu oluşur.
- [x] Workflow loglarında hiçbir secret değeri (token, connection string) açık metin olarak
      görünmez.

**Durum: Tamamlandı.** `.github/workflows/sync-spotify.yml` — cron `0 3 1 * *` (UTC) + `workflow_dispatch`,
tek `sync` job'ı, üç secret yalnızca bu job'ın `env:` bloğunda. Hata durumunda otomatik başarısız
işaretleme, US-003'teki `Program.cs`'in yakalanmamış exception'da non-zero exit vermesine dayanıyor
(ekstra kod gerekmedi). YAML sözdizimi Ruby YAML parser ile doğrulandı. `ci.yml`/`deploy.yml` henüz
yok (US-013/US-014) — secret'ların GitHub repo ayarlarında bu workflow'a scope edilmesi Mehmet'in
tek seferlik, kod dışı adımı olarak kalıyor (dosya içinde not edildi).

Kapsam dışı: senkron aracının iş mantığı (US-003), production deploy pipeline'ı (US-014).
Öncelik: Must
Platform: web

---

## US-005 — Cache eksik/bayat olduğunda zarif düşüş (graceful degradation)

Kullanıcı olarak ziyaretçi, bir playlist'in Spotify-kaynaklı verisi henüz senkronize edilmemiş veya
Spotify'da artık bulunamıyor olsa bile, playlist'in editoryal sayfasını sorunsuz görmek istiyorum
ki kırık bir deneyimle karşılaşmayayım.

Kabul kriterleri:

- [x] Yayınlanmış bir playlist'in `spotify_playlist_cache` içinde henüz satırı olmadığında, kart ve
      detay sayfası hata vermeden, yalnızca editoryal alanlarla (başlık, özet, mood/genre/occasion,
      curator note) render edilir.
- [x] İlgili cache satırı `is_available = false` olduğunda, detay sayfası editoryal içeriği
      göstermeye devam eder ve iframe yerine anlaşılır bir "player şu anda kullanılamıyor" mesajı
      gösterir; sayfa tek içerik olarak kırık bir iframe sunmaz.
- [x] Veritabanına hiç erişilemediğinde (bağlantı hatası), sayfa yine de editoryal içerikle 200
      yanıtı döner; 500 hatası oluşmaz.
- [x] `/health/ready` uç noktası, veritabanına erişilemese bile "ready" durumunu bildirir (yalnızca
      içerik doğrulamasının başarılı olup olmadığına bakar), bu davranış bir entegrasyon testiyle
      doğrulanır.

Kapsam dışı: canlı (istek anında) Spotify API çağrısı — mimari gereği bu asla yapılmaz.
Öncelik: Must
Platform: web

---

## US-006 — Editoryal içerik şeması ve v0.2 taksonomi doğrulaması

Kullanıcı olarak proje sahibi (Mehmet), her playlist Markdown dosyasının gerekli alanları içerdiğini
ve mood/genre/occasion/era değerlerinin onaylı v0.2 listelerinden (spec bölüm 8) geldiğini otomatik
olarak doğrulamak istiyorum ki hatalı içerik yayına sızmasın.

Kabul kriterleri:

- [x] `schemaVersion`, `slug`, `spotifyPlaylistId`, `title`, `summary`, `moods`, `genres`,
      `occasions`, `era`, `publishedAt` (yayınlanmış içerik için), `status` alanlarından biri
      eksik veya kural dışı olduğunda (ör. `spotifyPlaylistId` 22 base62 karakter değilse, `title`
      3-80 karakter aralığı dışındaysa) doğrulama başarısız olur ve nedeni belirten bir hata mesajı
      üretir.
- [x] `moods`/`genres`/`occasions`/`era` içinde spec bölüm 8.2-8.5'teki onaylı listelerde olmayan
      bir değer geçtiğinde doğrulama başarısız olur.
- [x] İki farklı dosyada aynı `slug` veya aynı `spotifyPlaylistId` bulunduğunda doğrulama başarısız
      olur.
- [x] `status: draft` olan bir dosya `publishedAt` alanını atladığında doğrulama başarısız olmaz
      (draft muaf), ama geçerli bir `spotifyPlaylistId` ve `slug` yine de zorunludur.

Kapsam dışı: taksonomi listesinin kendisinin genişletilmesi/değiştirilmesi (içerik değişikliği,
kod değişikliği değil).
Öncelik: Must
Platform: web

---

## US-007 — İçerik doğrulamasının CI'a bağlanması

Kullanıcı olarak proje sahibi (Mehmet), her pull request'te içerik doğrulamasının otomatik
çalışmasını istiyorum ki hatalı bir playlist dosyası `main`'e birleşmeden önce yakalansın.

Kabul kriterleri:

- [x] `content/playlists/` altına US-006'daki kurallardan birini ihlal eden bir dosya eklenmiş bir
      PR açıldığında, CI kontrolü kırmızı olur ve PR birleştirilemez (branch protection ile). Kod
      tarafı tamam (job geçersiz içerikte non-zero exit ile kırmızı olur, `ci.yml` +
      `ContentValidationCli`); Mehmet branch protection'ı GitHub UI'dan açtı — `main` üzerinde
      6 zorunlu status check + PR şartı GitHub API'sinden doğrulandı (2026-09-05).
- [x] Geçerli bir playlist dosyası eklenmiş bir PR açıldığında, içerik doğrulama adımı yeşil olur.
- [x] CI logunda hangi dosyanın hangi kuralı ihlal ettiği açıkça görünür (dosya adı + alan adı +
      beklenen kural).

Kapsam dışı: build/test/lint gibi diğer CI adımları (US-013).
Öncelik: Must
Platform: web

---

## US-008 — Blazor Web App iskeleti (static SSR, sağlık kontrolleri, routing)

Kullanıcı olarak ziyaretçi, siteye ilk kez geldiğimde temel sayfaların (ana sayfa, playlist detayı,
about, privacy, terms) doldurulmuş içerikle, JavaScript olmadan bile açılmasını istiyorum ki site
her koşulda erişilebilir olsun.

Kabul kriterleri:

- [x] `src/TheBluesland.Web` projesi eklendiğinde, `/`, `/playlists/{slug}`, `/about`, `/privacy`,
      `/terms` route'ları tanımlıdır ve varsayılan render modu static SSR'dır.
- [x] JavaScript devre dışı bırakıldığında (veya Interactive Server bağlantısı koparıldığında),
      ana sayfa ve playlist detay sayfası hâlâ okunabilir içerik döner (boş sayfa veya sonsuz
      yükleniyor durumu oluşmaz).
- [x] `/health/live` ve `/health/ready` uç noktaları tanımlıdır; `/health/live` her zaman 200 döner,
      `/health/ready` içerik yüklenip doğrulandığında 200 döner.
- [x] Var olmayan bir `/playlists/{slug}` istendiğinde 404 sayfası döner (500 değil).

Kapsam dışı: filtre etkileşimi (US-009), Spotify embed (US-010), SEO metadata (US-011).
Öncelik: Must
Platform: web

---

## US-009 — Ana sayfa: katalog, kartlar ve filtreleme

Kullanıcı olarak ziyaretçi, playlist'leri mood/genre/occasion/era'ya göre filtrelemek istiyorum ki
aradığım ruh haline uygun playlist'i hızlıca bulayım.

Kabul kriterleri:

- [x] Ana sayfa açıldığında, yayınlanmış tüm playlist'ler `displayOrder` sonra `publishedAt`
      azalan sırasına göre listelenir.
- [x] Aynı boyut içinde (ör. iki mood) birden fazla değer seçildiğinde sonuçlar OR mantığıyla
      birleştirilir; farklı boyutlar arasında (ör. mood + occasion) AND mantığıyla birleştirilir.
- [x] Filtre seçimi yapıldığında URL query string'i güncellenir; bu URL doğrudan açıldığında aynı
      filtre durumu geri yüklenir.
- [x] Hiçbir playlist aktif filtrelerle eşleşmediğinde, "sonuç yok" mesajı ve tek tıkla filtre
      sıfırlama seçeneği gösterilir.
- [x] Dört playlist "featured" olarak işaretlendiğinde beşinci bir playlist featured olarak
      eklenmeye çalışıldığında içerik doğrulama bunu reddeder (US-006 ile birlikte).

Kapsam dışı: playlist detay sayfası (US-010).
Öncelik: Must
Platform: web

---

## US-010 — Playlist detay sayfası: curator note, cache alanları, Spotify embed

Kullanıcı olarak ziyaretçi, bir playlist detay sayfasında küratör notunu okumak ve Spotify'da
dinlemeye başlamak istiyorum ki playlist'in ne olduğunu anlayıp doğrudan dinleyebileyim.

Kabul kriterleri:

- [x] Detay sayfası açıldığında, curator note (Markdown'dan render edilmiş, sanitize edilmiş HTML),
      mood/genre/occasion/era etiketleri ve (varsa) cache'ten gelen track sayısı ve kapak görseli
      gösterilir.
- [x] Sayfa ilk yüklendiğinde Spotify iframe'i DOM'da yoktur; ziyaretçi "dinle" öğesine tıkladığında
      iframe eklenir (click-to-load).
- [x] "Open in Spotify" bağlantısı, ilgili playlist'in `https://open.spotify.com/playlist/{id}`
      adresine işaret eder.
- [x] Sayfanın altında, aynı ziyaretçinin gördüğü playlist'i içermeyen, en fazla üç ilişkili
      playlist (paylaşılan etiket sayısına göre) gösterilir.
- [x] `previousSlugs` içinde eski bir slug bulunduğunda, o eski slug'a yapılan istek yeni slug'a
      kalıcı (301) yönlendirilir.

Kapsam dışı: cache eksikliğinde davranış (US-005'te tanımlı ve burada da geçerli, ayrı test
edilmez, US-005 testleri kapsar).
Öncelik: Must
Platform: web

---

## US-011 — SEO metadata, sitemap ve sosyal kartlar

Kullanıcı olarak proje sahibi (Mehmet), her sayfanın arama motorlarında ve sosyal medyada doğru
göründüğünü istiyorum ki paylaşılan bağlantılar profesyonel görünsün ve site keşfedilebilir olsun.

Kabul kriterleri:

- [x] Her indexlenebilir sayfa (`/`, `/playlists/{slug}`, `/about`, `/privacy`, `/terms`) benzersiz
      `<title>`, meta description, canonical URL, Open Graph ve Twitter/X card meta etiketleri
      döner.
- [x] `/sitemap.xml` yalnızca `status: published` olan playlist'leri içerir; draft içerik ve
      filtreli query-string varyasyonları sitemap'te yer almaz.
- [x] Playlist detay sayfasının Open Graph görseli TheBluesland'e ait, üretilmiş bir 1200×630
      görseldir; Spotify'ın kapak görseli URL'i doğrudan `og:image` olarak kullanılmaz.
- [x] Playlist detay sayfasının structured data'sı (`CollectionPage`/ilgili şema) hiçbir track
      başlığı listelemez.

Kapsam dışı: `/feed.xml` (launch için opsiyonel, ayrı hikaye olarak V1.1'e ertelenebilir).
Öncelik: Must
Platform: web

---

## US-012 — Güvenlik başlıkları, CSP ve embed URL doğrulaması

Kullanıcı olarak proje sahibi (Mehmet), sitenin yalnızca kendi kaynaklarını ve gerekli minimum
Spotify izinlerini yüklediğinden emin olmak istiyorum ki güvenlik riski ve içerik enjeksiyonu
önlensin.

Kabul kriterleri:

- [x] Production yanıtlarında `Content-Security-Policy`, `X-Content-Type-Options`,
      `Referrer-Policy`, `Permissions-Policy` başlıkları mevcuttur; CSP yalnızca click-to-load
      Spotify embed'i için gereken minimum directive'leri Spotify'a açar.
- [x] Embed URL'i yalnızca doğrulanmış (22 karakter base62) bir `spotifyPlaylistId`'den
      oluşturulur; içerik dosyasında rastgele bir iframe URL'i verilmeye çalışıldığında bu
      reddedilir/yok sayılır (US-006 ile birlikte doğrulanır).
- [x] Markdown içinde ham HTML kullanılmaya çalışıldığında, render edilen çıktıda bu HTML devre
      dışı bırakılmış/temizlenmiş olarak görünür.

Kapsam dışı: rate limiting, WAF (MVP kapsamında değil).
Öncelik: Must
Platform: web

---

## US-013 — Pull request CI pipeline'ı

Kullanıcı olarak geliştirici, her pull request'te build, test, format, içerik doğrulama ve Docker
image build adımlarının otomatik çalışmasını istiyorum ki `main`'e yalnızca doğrulanmış kod
birleşsin.

Kabul kriterleri:

- [x] `.github/workflows/ci.yml` bir pull request'te tetiklendiğinde şu adımları sırayla çalıştırır
      ve her biri ayrı ayrı başarı/başarısızlık durumu raporlar: restore, Release build, içerik
      doğrulama (US-007), unit+integration testler (Testcontainers Postgres ile), format kontrolü,
      Tailwind production build, Playwright smoke testleri, dependency/secret scanning, Docker
      image build.
- [x] Bu adımlardan herhangi biri başarısız olduğunda PR birleştirilemez (branch protection). Kod
      tarafı tamam (her job kendi başarı/başarısızlığını raporlar); Mehmet branch protection'ı açtı
      — `main`'de content-validation, build-and-test, playwright-smoke, tailwind-build,
      dependency-secret-scan, docker-build'in altısı da zorunlu (GitHub API'sinden doğrulandı,
      2026-09-05).
- [x] `ci.yml` çalışırken `SPOTIFY_CLIENT_ID`, `SPOTIFY_REFRESH_TOKEN`,
      `NEON_SYNC_CONNECTION_STRING` secret'larına erişimi yoktur (US-004 ile birlikte doğrulanır).
- [x] Integration testler gerçek Neon veritabanına değil, geçici bir Testcontainers Postgres
      instance'ına karşı çalışır.

Kapsam dışı: deploy adımı (US-014), aylık senkron workflow'u (US-004).
Öncelik: Must
Platform: web

---

## US-014 — Render + Neon production deploy pipeline'ı

Kullanıcı olarak proje sahibi (Mehmet), `main`'e birleşen değişikliklerin otomatik olarak Render'a
deploy edilmesini ve gerekirse önceki sürüme dönülebilmesini istiyorum ki yayına almak manuel ve
riskli olmasın.

Kabul kriterleri:

- [x] `.github/workflows/deploy.yml`, `main`'e merge sonrası tetiklendiğinde, commit SHA ile
      etiketlenmiş bir immutable Docker image build eder ve Render'ın bunu çekebileceği bir yere
      gönderir (veya Render deploy hook'unu tetikler). Render Blueprint bağlandı, ilk deploy
      `https://thebluesland.onrender.com`'da canlı doğrulandı; her sonraki merge'de Deploy workflow'u
      yeşil (son doğrulama: PR #7, 2026-09-05).
- [x] Yeni image Render'a alındığında, trafiğe yönlendirilmeden önce `/health/ready` kontrolü
      başarılı olur; başarısız olursa trafik yönlendirilmez. `render.yaml`'daki
      `healthCheckPath: /health/ready` Render'da uçtan uca doğrulandı — servis "Live" durumunda.
- [x] Render production ortam değişkenleri incelendiğinde, yalnızca salt-okunur Neon connection
      string bulunur; hiçbir Spotify veya AI credential'ı yoktur (spec SEC-001, DoD maddesi). Mehmet
      `ConnectionStrings__SpotifyPlaylistCache`'i salt-okunur `spotify_cache_readonly` rolüyle Render
      Dashboard'unda elle girdi; site canlı ve cache'i doğru okuyor.
- [ ] Bir önceki sağlıklı deploy'a Render'ın rollback özelliğiyle geri dönüldüğünde site eski
      sürümle çalışır durumda kalır. Mimari olarak destekleniyor (her deploy immutable SHA-tagged
      image, Render'ın native rollback özelliği koddan bağımsız çalışır); gerçek serviste henüz
      denenmedi.

Kapsam dışı: özel domain bağlama (spec bölüm 19'da non-blocking olarak işaretli, ayrı, düşük
öncelikli bir hikaye olarak ileride açılabilir).
Öncelik: Must
Platform: web

---

## US-015 — Launch içeriği: sahip olunan tüm playlist'lerin editoryal olarak tamamlanması

Kullanıcı olarak proje sahibi (Mehmet), yayına çıkmadan önce en az sekiz playlist'in başlık, özet,
etiketler ve küratör notuyla tamamlanmasını istiyorum ki site boş/yarım görünmesin (spec bölüm 3.2,
7.1, 22).

> **2026-09-05 kapsam genişletmesi:** Mehmet, sekiz playlist'lik launch eşiğini "azar azar değil
> hepsini" talimatıyla iptal edip sahip olduğu tüm 120 herkese açık playlist'in yayınlanmasını
> istedi ("Tüm 120'sini yayınla, taksonomiyi genişlet"). Aşağıdaki kriterler bu genişletilmiş
> kapsamı yansıtacak şekilde güncellendi; orijinal 8 playlist eşiği artık geçerli değil.

Kabul kriterleri:

- [x] `content/playlists/` altında `status: published` olan **120/120** dosya bulunur (sahip
      olunan tüm herkese açık playlist'ler) ve her biri US-006'daki doğrulamayı geçer.
- [x] Yayınlanan dosyaların her birinde en az bir mood, bir genre, bir occasion etiketi ve
      FR-021'in genişletilmiş 40-250 kelimelik aralığına uyan bir küratör notu bulunur (bkz.
      spec FR-021'in 2026-09-05 notu — 80 kelimelik eski taban, 120-playlist ölçeğinde
      pratik değildi). Gerçek dağılım: 42-117 kelime.
- [x] Section 7'deki iki taslak playlist ("Masterpieces of Erkin the Father",
      "Dear Mr. Fantasy") için Mehmet'in onayladığı nihai başlık/özet/etiket/not, taslak
      önerilerin yerini almıştır (2026-09-05, PR #1).
- [x] Sync workflow (US-004) 120 playlist'in tamamı için en az bir kez başarıyla çalışmış ve her
      biri için `spotify_playlist_cache` içinde bir satır (veri dolu, hepsi `is_available = true`)
      oluşmuştur.

Kapsam dışı: gelecekte hesaba eklenecek yeni playlist'lerin otomatik keşfi/yayını (bu, ayrı bir
gelecek hikâye olur — mevcut `list-playlists`/draft-seed akışı manuel tetiklenir).
Öncelik: Must
Platform: web

---

## US-016 — AI destekli kürator notu taslağı önerisi

Kullanıcı olarak proje sahibi (Mehmet), Spotify'dan çekilen bir playlist için elle tetiklediğim bir
araçtan AI tarafından üretilmiş bir kürator notu taslağı istiyorum ki boş bir sayfadan başlamak
yerine bir ilk taslağı düzenleyerek yayına hazırlayabileyim (`docs/adr/0005-ai-kurator-notu-siniri.md`).

Kabul kriterleri:

- [x] `workflow_dispatch` ile bir `spotifyPlaylistId` girdisi verildiğinde, araç
      `spotify_playlist_cache` tablosundan yalnızca `name`, `description`, `track_count`, `artists`
      alanlarını okur ve Google Gemini API'ye yalnızca bu dört alanı girdi olarak gönderir; prompt
      builder'a tüm cache satırı (`is_available`, `synced_at`, `spotify_snapshot_id`,
      `cover_image_url`, `spotify_playlist_id` dahil) verildiğinde üretilen prompt metninde bu dört
      alan dışındaki hiçbir değerin geçmediği bir regresyon testiyle doğrulanır
      (`CuratorNotePromptBuilderTests`).
- [x] Üretilen taslak metin hiçbir veritabanı tablosuna/kolonuna yazılmaz ve hiçbir koşulda
      `content/playlists/*.md` dosyasına otomatik yazılmaz; yalnızca workflow'un job summary'sine ve
      bir build artifact'ına (Markdown dosyası, `actions/upload-artifact`) yazılır.
- [x] Verilen `spotifyPlaylistId`, `spotify_playlist_cache` içinde bulunamadığında (henüz senkronize
      edilmemiş) veya `is_available = false` olduğunda, araç anlamlı bir hata mesajıyla başarısız
      olur; AI'ya boş veya eksik veri gönderilmez (`CuratorNoteSuggestionServiceTests` - sahte bir
      AI client'ı çağrılırsa test'i patlatacak şekilde, her iki dal için de doğrulandı).
- [x] `GEMINI_API_KEY`, yalnızca `suggest-curator-note.yml` workflow'una scope edilmiş bir GitHub
      Actions repository secret'ıdır; `ci.yml`, `deploy.yml`, `sync-spotify.yml` workflow'larından bu
      secret'a erişim yoktur (`SuggestCuratorNoteWorkflowSecretIsolationTests` ile doğrulandı; repo
      ayarlarında secret'ı bu workflow'a scope etmek Mehmet'in elle yapacağı tek seferlik adım,
      US-004/US-007 ile aynı desen).
- [x] Araç Spotify Web API'ye hiçbir istek atmaz ve hiçbir Spotify credential'ı almaz; tek veri
      kaynağı Neon'daki salt-okunur bağlantıdır (US-002'deki `spotify_cache_readonly` rolü,
      `NEON_READONLY_CONNECTION_STRING` secret'ı üzerinden — Render'ın env var'ıyla aynı rol/değer,
      ayrı bir secret olarak tutulur çünkü Render'ın ortam değişkenlerine GitHub Actions'tan
      erişilemez).
- [x] Workflow loglarında `GEMINI_API_KEY` değeri açık metin olarak görünmez (kod hiçbir yerde
      `Console.WriteLine`/log ile yazmıyor; ayrıca GitHub Actions kayıtlı secret değerlerini otomatik
      maskeler).

Kapsam dışı: otomatik mood/genre/occasion/era etiketleme, track önerisi veya Spotify içeriğinden
herhangi bir çıkarım (ADR-0005 madde 1, spec bölüm 11.2 — hâlâ yasak); taslağın otomatik olarak PR'a
dönüştürülmesi; aylık `sync-spotify.yml` job'una entegrasyon (bu araç bağımsız ve yalnızca elle
tetiklenir).
Bağımlılık: US-002 (salt-okunur DB rolü), `docs/adr/0005-ai-kurator-notu-siniri.md`.
Öncelik: Should
Platform: web

---

## US-017 — Occasion taksonomisini geniş kütüphaneye göre genişlet

Kullanıcı olarak proje sahibi (Mehmet), 120 playlist'lik geniş kütüphanede curator notlarında
tekrar eden ama hiçbir mevcut Occasion etiketine tam oturmayan iki gerçek kullanım senaryosunu
(`focus`, `dancing`) filtrelenebilir hâle getirmek istiyorum.

> **2026-09-06 kapsam daraltması (US-020 bulgusu):** Başlangıçta mood/occasion/era'nın üçünün de
> genre gibi genişletilmesi öngörülmüştü. US-020'nin 120 dosyalık taraması, mood ve era'da
> genişletme için gerçek bir gerekçe bulamadı (her iki boyut da mevcut 5 değerle sağlıklı dağılım
> gösteriyor); yalnızca Occasion'da curator notunda açıkça geçen ama etiketlenemeyen iki tema
> (`focus`, `dancing`) tespit edildi. Bu hikaye buna göre yalnızca Occasion'a daraltıldı.
>
> **2026-09-06 isim onayı:** Mehmet, `focus` ve `dancing` isimlerini olduğu gibi onayladı
> (alternatif olarak sorulan `concentration`/`party` değil).

Kabul kriterleri:

- [x] `focus` ve `dancing` değerleri `PlaylistTaxonomy.Occasions`'a eklendiğinde, içerik doğrulayıcı
      (US-006) güncel listeyi kullanır ve CI (US-007) buna göre doğrular.
- [x] Genişletme sonrası, mevcut 120 playlist'in hiçbiri artık geçersiz sayılan bir occasion değeri
      taşımaz (regresyon: tüm katalog doğrulamadan geçmeye devam eder).
- [x] Yeni değerler eklendiği bir PR açıldığında CI yeşil kalır; onaylanmamış bir değer hâlâ
      reddedilir.
- [ ] (Could) US-020'de isimlendirilen 6+4 aday dosyanın occasion etiketi, Mehmet uygun görürse
      yeni değerle güncellenir (ör. `focus.md`'nin occasion'ı `headphones`'tan `focus`'a taşınır) —
      bu bir içerik PR'ı, kod değişikliği değil.

**Durum: İlk üç kriter tamamlandı (2026-09-06).** `PlaylistTaxonomy.Occasions`'a `focus`/`dancing`
eklendi, spec bölüm 8.4 genre'nin 8.3'teki desenine uygun güncellendi. Testcontainers Postgres'e
karşı 188/188 test yeşil (gerçek 120 published + 1 draft dosya dahil). Dördüncü (Could) kriter —
10 aday dosyanın occasion etiketinin yeni değerlere taşınması — henüz yapılmadı, ayrı bir içerik
PR'ı olarak istenirse yapılabilir.

Kapsam dışı: Mood/Era genişletmesi (US-020 bulgusuna göre gerekçe yok, ayrı bir ihtiyaç ortaya
çıkarsa yeniden değerlendirilir).
Bağımlılık: US-020 (aday liste analizi — tamamlandı).
Öncelik: Should
Platform: web

---

## US-018 — Filtreleri navbar'a taşı (dropdown deseni)

Kullanıcı olarak ziyaretçi, filtreleri sayfanın üstünü kaplayan büyük bir inline form yerine kompakt
bir navbar'dan kullanmak istiyorum ki playlist listesine daha hızlı ulaşayım ve sayfa daha ferah
görünsün.

Kabul kriterleri:

- [x] Ana sayfa açıldığında Mood/Genre/Occasion/Era filtreleri, mevcut inline `<form>` bloğu yerine
      üst navbar'da dört ayrı dropdown/pill kontrolü olarak görünür.
- [x] Bir dropdown açıldığında yalnızca o boyutun seçenekleri görünür; diğerleri kapalı kalır.
- [x] Seçim yapıldığında URL query string güncellenir ve liste filtrelenir; paylaşılan bir linkte
      aynı filtre durumu geri yüklenir (US-009 AC3 korunur).
- [x] Aktif filtre sayısı navbar'da görünür bir göstergeyle belirtilir (ör. "Genre (2)").
- [x] JavaScript devre dışıyken filtreleme tamamen bozulmaz — en az temel bir seç/uygula akışı
      çalışmaya devam eder (US-009'un zero-JS static-SSR ilkesi korunur, spec 12.3).
- [x] (Could — düşük öncelik) Dropdown açma/kapama geçişinde sade bir mikro-etkileşim/animasyon
      vardır; `prefers-reduced-motion` saygı gösterilir (spec 10.2). Bu bir tasarım/CSS detayıdır,
      yukarıdaki fonksiyonel kriterleri bloklamaz.

**Durum: Tamamlandı.** Eski hep-açık `<fieldset>` yığını, dört bağımsız native `<details>`/
`<summary>` dropdown'una (`.filter-navbar` — `HomePage.razor`) dönüştürüldü. `<details>` native
olduğu için her dropdown yalnızca kendi panelini gösterir ve açma/kapama JavaScript gerektirmez
(AC2/AC5 aynı anda karşılanıyor); alttaki `<form method="get">`/query-string mekanizması US-009'dan
değişmeden korundu (AC3). Her `<summary>`, aktif seçim sayısını "Dimension (N)" biçiminde gösteriyor
(`DimensionLabel` helper'ı, AC4). Ok ikonunun açık/kapalı dönüşü `prefers-reduced-motion: no-preference`
ile korumalı bir CSS transition (Could kriteri). Yeni `HomePageFilterIntegrationTests` testleri
dropdown yapısını ve aktif-sayı etiketlerini gerçek render edilmiş HTML'e karşı doğruluyor;
Testcontainers Postgres'e karşı 193/193 test yeşil, `dotnet format --verify-no-changes` temiz.

Kapsam dışı: mobil için tam-ekran filtre deneyimi (bkz. US-021, ayrı hikâye).
Öncelik: Should
Platform: web

---

## US-019 — Playlist kataloğunu kaydırdıkça kademeli yükle

Kullanıcı olarak ziyaretçi, 100+ playlist'lik kataloğu tek seferde devasa bir sayfa yerine aşağı
kaydırdıkça akıcı yüklenen bir liste olarak görmek istiyorum ki sayfa hızlı açılsın.

Kabul kriterleri:

- [x] Ana sayfa (aktif filtrelerle) ilk yüklemede yalnızca ilk N (ör. 24) eşleşen playlist'i render
      eder; eşleşen sayı N'den fazlaysa bir "daha fazla yükle" mekanizması vardır.
- [x] Ziyaretçi listenin sonuna yaklaştığında (veya "Daha fazla göster" bağlantısına tıkladığında)
      bir sonraki N playlist eklenir.
- [x] JavaScript olmadan da progressive enhancement ile en azından "Daha fazla göster" linki
      üzerinden tüm katalog adım adım erişilebilir kalır.
- [x] Filtre değiştiğinde sayfalama sıfırlanır.
- [x] URL kaçıncı sayfada olunduğunu yansıtır, derin link paylaşıldığında aynı miktarda içerik geri
      yüklenir.

**Durum: Tamamlandı.** Yeni saf `PlaylistCataloguePage` sınıfı (`Content/PlaylistCataloguePage.cs`),
US-009'daki `PlaylistFilter` ile aynı desende: `page` N, ilk N × 24 (`PageSize`) playlist'i kümülatif
gösterir — pencereli bir "N..2N" dilimi değil, böylece paylaşılan bir `?page=3` linki "Daha fazla
göster"e iki kez tıklanmış hâliyle aynı miktarda içerik üretir (AC5). "Show more" düz bir `<a>` linki
(`HomePage.razor`, `.load-more`), aktif mood/genre/occasion/era query parametrelerini koruyup `page`'i
bir artırıyor — JavaScript'e ihtiyaç yok (AC2/AC3). Sayfalama sıfırlama (AC4) ekstra kod gerektirmedi:
`page`, filtre navbar formunda gizli bir alan olarak taşınmıyor, bu yüzden formun düz GET submit'i
zaten tüm query string'i (page dahil) filtre alanlarıyla değiştiriyor. Yeni `content-playlists-pagination`
fixture seti (26 dosya, PageSize+2) ile 3 entegrasyon testi + `PlaylistCataloguePage` için 8 saf
birim testi eklendi. Testcontainers Postgres'e karşı 204/204 test yeşil, `dotnet format
--verify-no-changes` temiz, Tailwind `build:css` başarılı.

Kapsam dışı: IntersectionObserver/animasyon detayları; client-side/API tabanlı pagination (proje
sunucu tarafı render'a sadık kalmalı, US-008/US-009 ile tutarlı).
Öncelik: Should
Platform: web

---

## US-020 — Mood/Occasion/Era için aday taksonomi listesi analizi

Kullanıcı olarak proje sahibi (Mehmet), mevcut 120 playlist'in curator notu/özet metinlerinin
taranarak mood/occasion/era için genişletilmiş bir aday değer listesi önerilmesini istiyorum ki
US-017'deki genişletme keyfi değil, gerçek kütüphaneyi yansıtan bir temele dayansın (genre'nin
2026-09-05'teki genişletilme sürecindeki gibi).

Kabul kriterleri:

- [x] `content/playlists/*.md` içindeki tüm curator notu/özet metinleri taranıp, mevcut 5 değerlik
      mood/occasion/era listelerinde karşılığı olmayan tekrar eden betimleyici kelime/temalar
      çıkarılır.
- [x] Çıktı, her aday değer için kaç playlist'te geçtiğini gösteren, Mehmet'in tek tek
      onaylayabileceği bir öneri listesi (ayrı bir doküman veya bu backlog girdisinin altına not)
      olarak sunulur.
- [x] Analiz, yeni bir taksonomi değeri dayatmaz — yalnızca aday sunar; nihai onay Mehmet'e aittir
      (spec 8.6 taksonomi governance ilkesiyle tutarlı).

**Durum: Tamamlandı (2026-09-06).** 120/120 `content/playlists/*.md` dosyasının front-matter
etiketleri ve curator notu/özet metinleri tarandı.

- **Mood ve Era için genişletme gerekçesi bulunamadı.** Her dosya mevcut 5 mood değerinden 1-2'sini,
  mevcut 5 era değerinden tam olarak birini kullanıyor; her iki listenin de tüm değerleri gerçek
  kullanımda ve dağılım makul (mood: melancholic 43, warm 62, energetic 46, raw 37, nostalgic 44 —
  toplamda 232/120 dosya ≈ dosya başına 1.93; era: mixed-era 94, 2000s-present 11, pre-1970 10,
  1970s 4, 1980s-1990s 1). Curator notu metninde de bu iki boyut için tekrar eden, etikete
  dönüşmemiş bir tema kümesi çıkmadı. **Öneri: US-017'nin kapsamını yalnızca Occasion'a daraltmak
  — mood/era'yı bugün genişletmeye gerekçe yok.**
- **Occasion'da iki gerçek aday bulundu**, curator notunda açıkça adı geçtiği hâlde mevcut 5 değerin
  (`late-night`, `night-drive`, `road-trip`, `slow-evening`, `headphones`) hiçbirine tam
  oturmadıkları için en yakın etikete "sıkıştırılmışlar":
  - **`focus`** (odaklanma/arka plan dinlemesi) — en az 6 dosyada açıkça bu temayla tarif ediliyor:
    `focus.md` ("A concentration playlist... built to sit underneath work"), `be-comfortable.md`
    ("Background music... designed to lower a room's tempo"), `no-more-words.md` ("put it on and
    forget it's there"), `coffee-circle.md` ("run in the background of a slow conversation"),
    `mag.md` ("soundtrack a whole quiet week"), `weekly-intricate.md` ("revisited on purpose rather
    than by accident").
  - **`dancing`** (dans/parti) — en az 4 dosyada: `dancing.md` ("an actual dance floor"),
    `sad-dance.md` ("For dancing alone in a dark room"), `funkers.md` ("Impossible to sit still
    through"), `saint-patrick-s-day-slainte.md` ("built for a crowd").

Nihai onay Mehmet'e ait (spec 8.6). Ham tarama scratchpad'te (`all-playlists.txt`, kalıcı değil,
tekrar üretilebilir) yapıldı; kalıcı içerik dosyalarına dokunulmadı.

Kapsam dışı: adayların `PlaylistTaxonomy`'ye eklenmesi (bkz. US-017); genre listesinin tekrar
gözden geçirilmesi (zaten genişletildi).
Öncelik: Should
Platform: content

---

## US-021 — Mobil tam-ekran filtre deneyimi

Kullanıcı olarak mobil ziyaretçi, dört filtre boyutunu dar bir ekranda navbar dropdown'ları yerine
tam ekranı kaplayan bir panelde kullanmak istiyorum ki küçük ekranda filtreleme rahat ve okunabilir
olsun.

Kabul kriterleri:

- [x] Dar ekranda (mobil breakpoint) navbar'daki filtre kontrollerinin yerini tam ekranı kaplayan bir
      filtre paneli/sayfası alır; tüm dört boyut (Mood/Genre/Occasion/Era) tek panelde birlikte
      görünür.
- [x] Panel açıldığında arka plandaki playlist listesi kaydırılamaz hâle gelir (odak filtre
      panelindedir); "Uygula" ve "Temizle" eylemleri belirgin şekilde erişilebilir.
- [x] Panel, US-018'deki aynı URL query-string mekanizmasını kullanır — masaüstü navbar'ından
      girilen bir filtreli link mobilde de aynı sonucu üretir ve tersi.
- [x] JavaScript devre dışıyken mobilde de en az temel bir seç/uygula akışı çalışmaya devam eder
      (US-018 AC5 ile aynı ilke).

**Durum: Tamamlandı.** Mevcut dört `<details class="filter-dropdown">` (US-018) hiç değişmedi;
tüm form bir `<details class="filter-mobile-panel">` ile sarmalandı (`HomePage.razor`) — tek DOM
ağacı iki breakpoint'e de hizmet ediyor, JS yok. İki CSS tekniği: (1) desktop'ta author CSS,
`<details>` UA stylesheet'inin "[open] değilse içeriği gizle" kuralını (author-origin, UA-origin'i
specificity'den bağımsız her zaman yener) ezip formu her zaman gösteriyor — dış sarmalayıcı işlevsiz
kalıyor, sadece iç dört dropdown önemli (US-018'den değişmedi); (2) `max-width: 40rem`'de bu
işlevsizlik kapanıyor, dış `<summary>` gerçek bir "Filters (N)" toggle'ına dönüşüyor ve formu
`position:fixed` tam ekran panel olarak açığa çıkarıyor (AC1), iç dört dropdown da panel içinde
zorla açık tutuluyor (dördü birden görünür, ayrı ayrı dokunma gerekmiyor). `body:has(.filter-mobile
-panel[open]) { overflow: hidden; }` arka plan kaydırmasını CSS-only kilitliyor (AC2) —
`:has()` desteklemeyen bir tarayıcıda bile tam ekran panel görsel olarak listeyi zaten kaplıyor.
Yeni `.filter-clear` linki (`href="/"`) hem masaüstü hem mobilde Uygula'nın yanında duruyor (AC2).
AC3/AC4 ekstra kod gerektirmedi: aynı `<form>`/query-string, JS yok. 2 yeni entegrasyon testi
(`HomePageFilterIntegrationTests`) sarmalayıcıyı ve kombine "Filters (N)" sayacını, Clear linkinin
varlığını doğruluyor. Testcontainers Postgres'e karşı 206/206 test yeşil, `dotnet format
--verify-no-changes` temiz, Tailwind `build:css` başarılı. Gerçek bir mobil tarayıcıda görsel
doğrulama yapılmadı (bu oturumda ağ/tarayıcı erişimi kısıtlıydı) — CSS mantığı yorum satırlarında
gerekçelendirildi, ancak Mehmet'in gerçek bir cihazda/DevTools'ta bir kez göz atması önerilir.

Kapsam dışı: masaüstü navbar tasarımı (US-018); filtre animasyon detayları (US-018 kapsamında ele
alınabilir).
Bağımlılık: US-018 (aynı URL query-string mekanizmasını paylaşır).
Öncelik: Could
Platform: web
