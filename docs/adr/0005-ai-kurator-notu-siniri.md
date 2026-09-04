# ADR-0005 — AI kürator notu sınırı: playlist-seviyesi cache alanları girdi olabilir, çıktı her zaman taslaktır

Durum: Kabul edildi ve onaylandı — Mehmet, 2026-09-03 tarihinde bu daraltılmış AI kullanımını
açıkça onayladı; Spotify Developer Policy'nin makine-öğrenmesi kısıtlamasından doğan riski bilerek
kabul etti.

## Bağlam

v0.1 ve v0.2 spec'lerinde sabit bir kural vardı: hiçbir Spotify-kaynaklı veri hiçbir AI/ML
modeline girdi olarak verilemez. Bu, spec bölüm 11.2'de ve SEC-008'de "without exception" / "hard
constraint, not configurable" olarak, bölüm 3.3'te ise "no AI analysis of Spotify Content under any
circumstance" olarak yazılıydı. İzin verilen tek AI girdisi Mehmet'in kendi yazdığı taslak kürator
notu metniydi. `docs/adr/0005-...` bu konu için yer ayrılmış ama hiç yazılmamış bir ADR
placeholder'ıydı (spec bölüm 24).

Pratikte bu kısıt, en çok işe yarayacak yardımı imkânsız kılıyordu: boş bir sayfadan kürator notu
yazmaya başlamak. Playlist'in Spotify'daki adı, açıklaması, kaç track içerdiği ve hangi
sanatçılardan oluştuğu, bir ilk taslak önerisi üretmek için yeterli ve zaten playlist'in kendi
herkese açık Spotify sayfasında görünen bilgiler; bunlar aynı zamanda ADR-0002 ile
`spotify_playlist_cache` tablosunda saklamayı kabul ettiğimiz alanlar.

Proje sahibi bu kısıtı bilinçli olarak, dar bir şekilde gevşetmeye karar verdi. Bu ADR, izin
verilen sınırı, çıktının statüsünü, credential izolasyonunu ve önerilen taslak metnin nerede
duracağını kayıt altına alır. Bkz. `docs/adr/0002-spotify-veri-mimarisi.md` (veri sınırı ve
credential deseni), `docs/adr/0003-mimari-kapsam.md` (proje sınırları), spec bölüm 9.4, 11.2, 13
(SEC-001, SEC-008), 20.

## Karar

1. **Girdi kapsamı: yalnızca playlist-seviyesi cache alanları.** Bir AI modeline girdi olarak
   verilebilecek Spotify-kaynaklı veri, `spotify_playlist_cache` tablosundaki tam olarak şu dört
   alandır: `name`, `description`, `track_count`, `artists`. Bunun dışındaki her şey yasak kalır:
   - track başlıkları, track ID'leri, süre, ISRC, per-track sanatçı ataması, audio-feature verisi
     (tempo/energy/valence/…) — bunlar zaten hiçbir yerde saklanmıyor (ADR-0002, madde 2);
   - kapak görseli (byte/görsel olarak) ve `cover_image_url` dahil hiçbir URL;
   - `spotify_snapshot_id`, `is_available`, `synced_at` gibi operasyonel alanlar (AI için anlamları
     yok, prompt'a girmelerinin gerekçesi de yok);
   - **modelin kendisinin herhangi bir Spotify kaynağını çekmesi** — playlist URL'i, Embed URL'i,
     kapak görseli URL'i veya Web API çağrısı vererek modele retrieval yaptırmak. Prompt kapalı bir
     metindir; model dış dünyaya erişmez.

   ADR-0002'nin "track listesi hiçbir zaman kalıcı saklanmaz veya işlenmez" ilkesi bozulmuyor;
   daralan yalnızca SEC-008'dir.

2. **Çıktı her zaman taslaktır; otomatik yayın yok.** AI'nın ürettiği kürator notu önerisi hiçbir
   koşulda `content/playlists/*.md` dosyalarına otomatik yazılmaz ve hiçbir koşulda
   `status: published` olarak yayınlanmaz. Öneri metni yalnızca Mehmet'in okuyup elle (yeniden
   yazarak veya kopyalayarak) gerçek Markdown kürator notuna taşıması için üretilir. Editoryal
   içeriğe giden tek yol, ADR-0002 madde 5'teki PR-tabanlı içerik onay akışı olarak kalır.

3. **Credential izolasyonu: mevcut Spotify deseniyle aynı.** `ANTHROPIC_API_KEY` yalnızca bir
   GitHub Actions repository secret'ı olarak yaşar ve yalnızca öneri workflow'una scope edilir.
   Production Render ortamına asla girmez; `TheBluesland.Web` hiçbir AI sağlayıcısıyla konuşmaz ve
   hiçbir AI SDK'sına referans vermez. SEC-001'in "production'da hiç Spotify/AI credential'ı yok,
   yalnızca salt-okunur bir DB connection string var" ilkesi aynen korunur.

4. **Öneri, ayrı ve elle tetiklenen bir GitHub Actions workflow'udur
   (`suggest-curator-note.yml`), aylık senkron job'ının parçası değil.** `workflow_dispatch` ile,
   girdi olarak bir `spotifyPlaylistId` alarak çalışır. Aylık cron'a bağlanmaz: öneri her ay her
   playlist için gerekli değil, yalnızca yeni bir not yazılırken bir kez gerekli.

5. **Öneri metni veritabanına yazılmaz ve web uygulamasına hiç ulaşmaz; workflow'un çıktısı
   olarak kalır.** Öneri, workflow'un job summary'sine ve bir build artifact'ına (Markdown dosyası)
   yazılır. `spotify_playlist_cache` tablosuna yeni kolon eklenmez, yeni tablo açılmaz, migration
   gerekmez. Bunun sonucu olarak **web uygulamasının bu metni public bir sayfada render etme
   ihtimali mimari olarak sıfırdır** — veri web'in eriştiği hiçbir store'da bulunmaz. Bu bir
   kodlama disiplini değil, yapısal bir kısıttır.

6. **Öneri aracının Spotify'a erişimi yoktur.** Aracın tek veri kaynağı Neon üzerindeki
   `spotify_playlist_cache` tablosuna yapılan salt-okunur bir sorgudur ve bu sorgu tam olarak madde
   1'deki dört kolonu seçer. Araca Spotify credential'ı verilmez. Böylece madde 1'in yasak listesi
   büyük ölçüde mekanik olarak garanti altına alınır: track-seviyesi veri hiçbir yerde saklanmadığı
   ve araç Spotify'a hiç bağlanamadığı için prompt'a girecek bir track verisi fiziksel olarak
   mevcut değildir.

## Gerekçe

1. **Gevşetme, gerçek ihtiyaca en dar cevap.** Yardım gereken yer boş sayfa problemi; bunun için
   playlist adı, açıklaması, track sayısı ve sanatçı listesi yeterli. Track-seviyesi veriye izin
   vermek ne gerekli ne de mevcut (ADR-0002 zaten saklamıyor), dolayısıyla sınırı tam bu dört alanda
   çizmek hem yeterli hem de en az riskli.
2. **Risk asimetrisi düşük.** Bu dört alan, playlist'in kendi herkese açık Spotify sayfasında zaten
   görünüyor; çıktı ise yayına doğrudan gitmiyor, insan incelemesinden geçiyor. Kabul edilen risk
   Spotify Developer Policy'nin ML kısıtlamasının geniş yorumudur — bu risk spec bölüm 21'e
   eklenmeli ve proje sahibi tarafından bilinerek kabul edilmiştir.
3. **Depolamamak, saklamanın her türünden basit ve güvenli.** Öneri metni, çalışma zamanı verisi
   değil; tek tüketicisi bir insan ve tek seferlik. Onu bir store'a koyduğumuz anda "web bunu
   public'e sızdırmasın" diye korunması gereken yeni bir yüzey doğar. Store'a hiç koymazsak
   korunacak bir şey de olmaz.
4. **Elle tetikleme, doğru maliyet ve doğru zamanlama.** Notu Mehmet yazarken istiyor; ayda bir,
   sekiz playlist için otomatik öneri üretmek hem gereksiz token maliyeti hem de kimsenin okumadığı
   çıktı üretir.
5. **Mevcut desenle tutarlı.** "Dışa dönük ve credential gerektiren iş = ayrı bir GitHub Actions
   workflow, production runtime'a girmez" deseni ADR-0002'de zaten kurulmuş ve çalışıyor; AI için
   ikinci bir desen icat etmiyoruz.

## Alternatifler

- **`spotify_playlist_cache` tablosuna nullable `ai_suggested_curator_note` +
  `ai_suggested_note_generated_at` kolonları eklemek (değerlendirilen öneri):** Reddedildi. Bu
  tablo web uygulamasının okuduğu tablodur; alan oraya girdiği anda EF entity'sine, dolayısıyla
  web'in erişim alanına girer ve "public sayfada asla render edilmesin" kısıtı kod
  disiplinine/review'a bağımlı hâle gelir (projeksiyon alışkanlığı, DTO mapping, geniş `SELECT`).
  Neon ücretsiz tier'ında kolon-seviyesi yetki ile bunu güvenilir şekilde engellemek de pratik
  değil (SEC-001 zaten tablo-seviyesi salt-okunur role dayanıyor). Ayrıca yayına gitmeyecek bir
  metin için migration ve şema borcu doğurur. Yani en ucuz görünen seçenek, en çok korunması
  gereken yüzeyi üretiyor.
- **Ayrı bir tablo (`ai_curator_note_suggestion`):** Reddedildi (bugün için). Kolon alternatifinin
  sızma riskini çözer — web'in okuduğu tabloya dokunmaz — ama bir migration, bir entity, bir
  yazma-yetkili connection ve bir saklama/temizlik politikası maliyeti getirir; karşılığında
  bugünkü tek ihtiyaç ("Mehmet metni bir kez okusun") için hiçbir ek fayda sağlamaz. **Bu, ihtiyaç
  ikinci kez somutlaşırsa (öneri geçmişini karşılaştırmak, birden fazla revizyonu saklamak,
  gelecekteki V2 admin arayüzünden görmek) seçilecek yoldur** — o durumda bile öneri metni
  `spotify_playlist_cache` tablosuna kolon olarak eklenmez.
- **Öneriyi doğrudan bir taslak PR olarak açmak (bot, `content/playlists/*.md` içine
  `status: draft` yazar):** Reddedildi. Mevcut PR onay akışını yeniden kullanması cazip ama repoya
  yazma yetkisi olan bir bot, bot-üretimi commit'ler ve "AI metni yanlışlıkla `published` olarak
  merge edilir" senaryosu için ek koruma gerektirir. Karar 2'nin ("otomatik yayın yok") en net
  garantisi, AI çıktısının repoya hiç dokunmamasıdır. İhtiyaç olursa artifact'tan PR'a geçmek
  additive bir adımdır.
- **Öneriyi web uygulamasına gömmek (Mehmet'in girip gördüğü bir sayfa):** Reddedildi. Production'a
  AI credential'ı ve/veya kimlik doğrulama gerektirir; ikisi de SEC-001'i ve MVP kapsamını (spec
  bölüm 3.3, V2) ihlal eder.
- **Statükoyu korumak (hiçbir Spotify verisi AI'ya girmez):** Reddedildi — proje sahibinin kararı.
  Yardımın en çok gerektiği yeri kapalı tutuyor ve karşılığında koruduğu şey (zaten herkese açık
  dört alanın modelden saklanması) sınırlı bir kazanç.

## Sonuçlar

- Spec bölüm 3.3, 11.2, 13 (SEC-008), 20 (V1.2) ve 24 bu kararı yansıtacak şekilde revize edildi;
  SEC-008 artık bir "no exception" yasağı değil, izin verilen dört alanı ve yasak alanları sayan bir
  sınır tanımıdır.
- Yeni bir yasak, tek başına test edilebilir hâlde ortaya çıkıyor: **prompt oluşturan kod, madde
  1'deki dört alan dışında hiçbir alanı prompt'a koyamaz.** Bu, spec bölüm 17.4'teki
  "hiçbir track-seviyesi alan DB'ye yazılmıyor" regresyon testinin kardeşi olarak bir birim testiyle
  korunmalıdır (prompt builder'a tüm cache satırı verilir; çıktı metninde yalnızca izin verilen
  alanların geçtiği doğrulanır).
- Web tarafında yeni bir kısıt doğmuyor çünkü yeni bir alan doğmuyor: `TheBluesland.Web` AI ile
  ilgili hiçbir şey görmez, hiçbir AI paketine referans vermez. Bir gelecekteki PR bu ADR'ı
  bilmeden AI önerisini DB'ye taşımaya kalkarsa, bu ADR'ın yeniden değerlendirilmesi gerekir.
- Yeni bir kabul edilen risk: Spotify Developer Policy'nin ML/içerik kısıtlaması, playlist-seviyesi
  metadata'nın AI'ya verilmesini de kapsayacak şekilde yorumlanabilir. Azaltıcı unsurlar: girdi
  yalnızca dört herkese açık alan, çıktı yayına doğrudan gitmiyor, kullanım aylık değil elle ve
  seyrek. Bu risk gerçekleşirse geri dönüş maliyeti düşüktür — tek bir workflow ve bir secret
  silinir; ne şema ne web kodu etkilenir.
- **Prompt injection, dar ve kabul edilen bir risktir.** Prompt'a giren `description` alanı
  Spotify-kaynaklı, üçüncü taraf bir metindir. Bugün risk düşük çünkü öneri aracı non-agentic:
  Spotify'a erişemiyor, DB'ye/`content/`'e/PR'a yazamıyor, ve çıktısı her koşulda insan onayından
  geçen bir taslak. `description` içine yerleştirilmiş düşman bir talimat en kötü ihtimalle işe
  yaramaz bir taslak üretir, herhangi bir otomatik aksiyon tetiklemez. Bu koruma aracın
  non-agentic kalmasına bağlıdır — araca yazma veya retrieval yetkisi verilirse bu ADR yeniden
  değerlendirilmelidir.
- **Build artifact'ı gizli değildir, bu bir netleştirmedir, gevşetme değil.** Repo public
  olduğu için workflow'un ürettiği job summary ve build artifact'ı, Mehmet henüz okuyup
  onaylamadan da GitHub Actions arayüzünden herkese açık görülebilir. Madde 5'teki "web
  uygulamasının bu metni render etme ihtimali sıfırdır" garantisi yalnızca canlı siteyi kapsar —
  "kimse göremez" anlamına gelmez. Girdi zaten yalnızca public Spotify verisinden türediği için
  bunun bir gizlilik riski yoktur; yalnızca yanlış anlaşılmasın diye burada açıkça yazılmıştır.
- `ANTHROPIC_API_KEY` yeni bir GitHub Actions secret'ı olarak eklenir ve yalnızca
  `suggest-curator-note.yml` workflow'una scope edilir; `ci.yml`, `deploy.yml` ve
  `sync-spotify.yml` bu secret'a erişmez (ADR-0002 madde 4'ün ayna kuralı).
- Bu ADR yalnızca kürator notu **önerisini** kapsar. Otomatik mood/genre/occasion/era etiketleme,
  track önerme veya Spotify içeriğinden çıkarım yapma hâlâ yasaktır (spec bölüm 11.2); bunlar için
  yeni bir ADR gerekir.
