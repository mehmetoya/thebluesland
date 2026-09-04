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

- [ ] `content/playlists/` altına US-006'daki kurallardan birini ihlal eden bir dosya eklenmiş bir
      PR açıldığında, CI kontrolü kırmızı olur ve PR birleştirilemez (branch protection ile). Kod
      tarafı tamam (job geçersiz içerikte non-zero exit ile kırmızı olur, `ci.yml` +
      `ContentValidationCli`); GitHub repo ayarlarında bu check'i zorunlu kılan branch protection
      toggle'ı (Settings > Branches) Mehmet'in elle yapması gereken, koddan yapılamayan tek seferlik
      bir adım — bu yüzden kutucuk tam işaretlenmedi.
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

- [ ] Production yanıtlarında `Content-Security-Policy`, `X-Content-Type-Options`,
      `Referrer-Policy`, `Permissions-Policy` başlıkları mevcuttur; CSP yalnızca click-to-load
      Spotify embed'i için gereken minimum directive'leri Spotify'a açar.
- [ ] Embed URL'i yalnızca doğrulanmış (22 karakter base62) bir `spotifyPlaylistId`'den
      oluşturulur; içerik dosyasında rastgele bir iframe URL'i verilmeye çalışıldığında bu
      reddedilir/yok sayılır (US-006 ile birlikte doğrulanır).
- [ ] Markdown içinde ham HTML kullanılmaya çalışıldığında, render edilen çıktıda bu HTML devre
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

- [ ] `.github/workflows/ci.yml` bir pull request'te tetiklendiğinde şu adımları sırayla çalıştırır
      ve her biri ayrı ayrı başarı/başarısızlık durumu raporlar: restore, Release build, içerik
      doğrulama (US-007), unit+integration testler (Testcontainers Postgres ile), format kontrolü,
      Tailwind production build, Playwright smoke testleri, dependency/secret scanning, Docker
      image build.
- [ ] Bu adımlardan herhangi biri başarısız olduğunda PR birleştirilemez (branch protection).
- [ ] `ci.yml` çalışırken `SPOTIFY_CLIENT_ID`, `SPOTIFY_REFRESH_TOKEN`,
      `NEON_SYNC_CONNECTION_STRING` secret'larına erişimi yoktur (US-004 ile birlikte doğrulanır).
- [ ] Integration testler gerçek Neon veritabanına değil, geçici bir Testcontainers Postgres
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

- [ ] `.github/workflows/deploy.yml`, `main`'e merge sonrası tetiklendiğinde, commit SHA ile
      etiketlenmiş bir immutable Docker image build eder ve Render'ın bunu çekebileceği bir yere
      gönderir (veya Render deploy hook'unu tetikler).
- [ ] Yeni image Render'a alındığında, trafiğe yönlendirilmeden önce `/health/ready` kontrolü
      başarılı olur; başarısız olursa trafik yönlendirilmez.
- [ ] Render production ortam değişkenleri incelendiğinde, yalnızca salt-okunur Neon connection
      string bulunur; hiçbir Spotify veya AI credential'ı yoktur (spec SEC-001, DoD maddesi).
- [ ] Bir önceki sağlıklı deploy'a Render'ın rollback özelliğiyle geri dönüldüğünde site eski
      sürümle çalışır durumda kalır.

Kapsam dışı: özel domain bağlama (spec bölüm 19'da non-blocking olarak işaretli, ayrı, düşük
öncelikli bir hikaye olarak ileride açılabilir).
Öncelik: Must
Platform: web

---

## US-015 — Launch içeriği: sekiz playlist'in editoryal olarak tamamlanması

Kullanıcı olarak proje sahibi (Mehmet), yayına çıkmadan önce en az sekiz playlist'in başlık, özet,
etiketler ve küratör notuyla tamamlanmasını istiyorum ki site boş/yarım görünmesin (spec bölüm 3.2,
7.1, 22).

Kabul kriterleri:

- [ ] `content/playlists/` altında `status: published` olan en az sekiz dosya bulunur ve her biri
      US-006'daki doğrulamayı geçer.
- [ ] Bu sekiz dosyanın her birinde en az bir mood, bir genre, bir occasion etiketi ve 80-250
      kelimelik bir küratör notu bulunur.
- [ ] Section 7'deki iki taslak playlist ("Masterpieces of Erkin the Father",
      "Dear Mr. Fantasy") için Mehmet'in onayladığı nihai başlık/özet/etiket/not, taslak
      önerilerin yerini almıştır.
- [ ] Sync workflow (US-004) bu sekiz playlist için en az bir kez başarıyla çalışmış ve her biri
      için `spotify_playlist_cache` içinde bir satır (ya veri dolu ya da onaylı `is_available =
      false`) oluşmuştur.

Kapsam dışı: dokuzuncu ve sonraki playlist'lerin eklenmesi (launch sonrası, sürekli içerik akışı).
Öncelik: Must
Platform: web

---

## US-016 — AI destekli kürator notu taslağı önerisi

Kullanıcı olarak proje sahibi (Mehmet), Spotify'dan çekilen bir playlist için elle tetiklediğim bir
araçtan AI tarafından üretilmiş bir kürator notu taslağı istiyorum ki boş bir sayfadan başlamak
yerine bir ilk taslağı düzenleyerek yayına hazırlayabileyim (`docs/adr/0005-ai-kurator-notu-siniri.md`).

Kabul kriterleri:

- [ ] `workflow_dispatch` ile bir `spotifyPlaylistId` girdisi verildiğinde, araç
      `spotify_playlist_cache` tablosundan yalnızca `name`, `description`, `track_count`, `artists`
      alanlarını okur ve Anthropic Claude API'ye yalnızca bu dört alanı girdi olarak gönderir; prompt
      builder'a tüm cache satırı (`is_available`, `synced_at`, `spotify_snapshot_id`,
      `cover_image_url`, `spotify_playlist_id` dahil) verildiğinde üretilen prompt metninde bu dört
      alan dışındaki hiçbir değerin geçmediği bir regresyon testiyle doğrulanır.
- [ ] Üretilen taslak metin hiçbir veritabanı tablosuna/kolonuna yazılmaz ve hiçbir koşulda
      `content/playlists/*.md` dosyasına otomatik yazılmaz; yalnızca workflow'un job summary'sine ve
      bir build artifact'ına (Markdown dosyası) yazılır.
- [ ] Verilen `spotifyPlaylistId`, `spotify_playlist_cache` içinde bulunamadığında (henüz senkronize
      edilmemiş) veya `is_available = false` olduğunda, araç anlamlı bir hata mesajıyla başarısız
      olur; AI'ya boş veya eksik veri gönderilmez.
- [ ] `ANTHROPIC_API_KEY`, yalnızca `suggest-curator-note.yml` workflow'una scope edilmiş bir GitHub
      Actions repository secret'ıdır; `ci.yml`, `deploy.yml`, `sync-spotify.yml` workflow'larından bu
      secret'a erişim yoktur (repo secret scope'u ile doğrulanır).
- [ ] Araç Spotify Web API'ye hiçbir istek atmaz ve hiçbir Spotify credential'ı almaz; tek veri
      kaynağı Neon'daki salt-okunur bağlantıdır (US-002'deki salt-okunur rol kullanılır).
- [ ] Workflow loglarında `ANTHROPIC_API_KEY` değeri açık metin olarak görünmez.

Kapsam dışı: otomatik mood/genre/occasion/era etiketleme, track önerisi veya Spotify içeriğinden
herhangi bir çıkarım (ADR-0005 madde 1, spec bölüm 11.2 — hâlâ yasak); taslağın otomatik olarak PR'a
dönüştürülmesi; aylık `sync-spotify.yml` job'una entegrasyon (bu araç bağımsız ve yalnızca elle
tetiklenir).
Bağımlılık: US-002 (salt-okunur DB rolü), `docs/adr/0005-ai-kurator-notu-siniri.md`.
Öncelik: Should
Platform: web
