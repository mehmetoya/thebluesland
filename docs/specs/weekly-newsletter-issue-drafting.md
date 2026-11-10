# Spec: issue-drafting

Modül: `issue-drafting` · Map: [weekly-newsletter.md](weekly-newsletter.md) · Önceki modül:
[story-ranking](weekly-newsletter-story-ranking.md) · Sınır: [ADR-0006](../adr/0006-weekly-newsletter-ai-metin-siniri.md)
· Ses: [docs/weekly/ses-rehberi.md](../weekly/ses-rehberi.md) · Durum: UYGULANDI — 2026-11-10 (PR bekliyor; ilk gerçek taslak production'da çalıştırılacak)

## Assumptions

1. Bu modül taslağı **dosya olarak üretir**; PR açmaz ve zamanlanmış çalışmaz. Bunlar
   `issue-review` modülünün işidir (map). Bu modülün kendi workflow'u yalnızca elle tetiklenir ve
   taslağı job summary'de ve indirilebilir bir artifact olarak gösterir.
2. Yazım Gemini ücretsiz katmanıyla yapılır: dil başına bir yazım çağrısı (TR ve EN), her biri
   birkaç bin token; ek olarak otomatik düzeltme ve son kontrol çağrıları (bölüm 3a, 3b; çağrı
   bütçesi bölüm 3c). `suggest-curator-note.yml` ile aynı API ve aynı varsayılan model kullanılır
   (`GEMINI_MODEL`, boşsa araçtaki varsayılan).
3. Gemini istemcisi `spotify-playlist-fetcher` içindekiyle aynı desende ama **ayrı** yazılır
   (~100 satır). Newsletter aracı Spotify aracına referans veremez (proje kuralı) ve yeni ortak
   proje açılmaz (CLAUDE.md). Bilinçli küçük bir tekrardır.
4. Kaynak linkleri, başlık sırası, AI notu ve dosya yapısı **kodda** üretilir. Model yalnızca
   metin alanlarını yazar. Böylece link uydurma ve yapı bozma ihtimali sıfırdır.

## Objective

story-ranking'in seçtiği 5 hikâye ve 3 yedek için ses rehberine uygun TR ve EN metinleri yazmak
ve sayıyı `content/weekly/<yıl>-W<hafta>.{tr,en}.md` olarak üretmek. Mehmet'in işi yalnızca
birkaç cümleyi düzeltmek olmalı.

- Mehmet olarak: taslağı okunur Markdown olarak görmek, olgu kartı dışına çıkan her cümlenin
  işaretlenmiş olmasını istiyorum.
- Mehmet olarak: bir haberi yedekle değiştirmek istediğimde yedeğin metninin de hazır olmasını
  istiyorum. Yeniden çalıştırmak zorunda kalmamalıyım.
- issue-delivery ve weekly-archive olarak: her hafta aynı yapıda, makinece okunabilir bir dosya
  istiyorum.

## Design

### 1. Olgu kartı (prompt'un tek veri girdisi)

Her hikâye için şunlar girer: hikâye anahtarı, tür ve puan dökümü, üye haberlerin kaynak adı,
yayın tarihi, başlığı ve özeti (ilk 800 karakter; `clustering.yml` `summaryCharacters`, 2026-11-10'da 300'den çıkarıldı: kısa özetle model "Neden önemli"yi uyduruyordu). Haberin zamanı `published_at ?? fetched_at`tır; RSS'te `pubDate`/`published` yoksa `updated` kullanılır, yani yayın tarihi güncelleme tarihine tercih edilir (kod değişikliği gerekmedi). **URL'ler prompt'a girmez**, çünkü model link
yazmayacak. 8 hikâye girer (5 seçim + 3 yedek).

### 2. Model çağrısı

- Dil başına bir çağrı yapılır. Prompt şunlardan oluşur: ses rehberinin tamamı (örnek sayı
  dahil), o dilin bölümü, olgu kartı ve kurallar. Örnek sayı **kurgusaldır** (uydurma sanatçı ve
  yerler); prompt "örnekteki hiçbir ifade, cümle parçası ya da olgu taslağa taşınmaz" der. Zaman
  kuralı: pencere ile gönderim tarihi arasında tarihlenen olay duyuru olarak geçmiş zamanla verilir,
  sonucu yazılmaz; başlamamış şey gelecek zamanla kalır. Her haber kim (kimlikle) / ne / ne zaman /
  nerede ve başlıklardaki özel adları taşır; roller kartın dediği gibi yazılır; iki makale çelişirse
  ayrıntı yazılmaz. "Neden önemli" başlık ve gövdede olmayan yeni bir olgu ekler.
- **Hafif model varyantı:** yedek model yazarken `Build(..., lite: true)` kullanılır: örnek sayı
  çıkarılır (rehberin ton/TR/EN bölümleri kalır), kurallar olgu kartından **sonra** kısa numaralı
  bir kontrol listesi olur ve sıcaklık 0.2'dir (varsayılan model 0.4).
- Yanıt `responseSchema` ile **JSON**'dur:
  `{ intro, outro, subject, preview, stories: [{ key, headline, body, whyItMatters }] }`.
  `subject` (≤ 60 karakter, iki haber kancası, sonu " · TBL Weekly") e-posta konusu, `preview` (≤ 90
  karakter, konuyu tekrarlamaz) gelen kutusu satırıdır.
- Prompt gönderim tarihini taşır ("The issue is sent on …; önceki olaylar geçmiş zaman; 'bu hafta'
  yazma"). Her iki dil aynı olguları taşır; yalnızca üslup farklıdır. "Neden önemli" kartta yazan
  somut bir sonuç ya da karşılaştırmadır; kartta olmayan arka plan bilgisi yasaktır (ADR-0006 madde 3).
- Sıcaklık düşüktür (0.4). Hata durumunda istemci 429/5xx için bir kez bekleyip yeniden dener
  (fetcher deseni).
- Varsayılan model yoğunluk veya kota nedeniyle (503/429) yanıt vermezse taslak ücretsiz katmandaki
  `gemini-flash-lite-latest` ile yazılır; rapor hangi modelin yazdığını gösterir. Elle seçilen model
  değiştirilmez. (2026-11-10, ilk production çalıştırmalarından sonra eklendi.)
- Yedek model sessizce kabul edilmez: dil başına varsayılan model önce bir kez daha, bir bekleme
  (90 sn) sonrası denenir; yine yanıt vermezse yedek yazar ve iş özeti ile PR açıklaması
  "[DÜŞÜK MODEL]" işaretini taşır, workflow PR başlığına da ekler (2026-W47 incelemesi, ED-1).

### 3. Deterministik denetim (kod, yanıt geldikten sonra)

| Denetim | Kural | Sonuç |
|---|---|---|
| Şema | 8 hikâyenin her key'i tam bir kez | Bozuksa bir kez yeniden iste, yine bozuksa çalışma başarısız |
| Uzunluk | başlık ≤ 70 karakter, gövde ≤ 3 cümle ve ≤ 450 karakter, "neden önemli" ≤ 2 cümle ve ≤ 260 karakter (onaylı örnek sayı 2 kısa cümle kullanıyor) | Uyarı |
| Link/biçim | URL, `[`, `]`, `<`, emoji, `!` yok | Temizlenir ve uyarı verilir |
| **Rakam** | Metindeki her sayı (yıl, yaş, tarih, tutar) **o haberin** olgu kartında geçmeli (başka haberin kartı yetmez) | Uyarı, cümle işaretlenir |
| **Özel ad** | Büyük harfle başlayan her ad o haberin kartında geçmeli. Yalnızca alanın ilk kelimesi muaftır; sonraki cümle başları yalnızca ad gibi görünüyorsa (kısaltma ya da ek almış ad) denetlenir | Uyarı |
| **Eksik** | Haberin başlıklarının çoğunluğunda geçen rakam, kısaltma ve (özette de büyük harfle geçen) adlar metinde olmalı; "Grand Theft Auto" baş harfleriyle (GTA) karşılanır | Uyarı |
| **TR/EN eşitliği** | Bir dilin haberinde olup diğerinde olmayan rakam ve özel ad. Başlıktan yalnızca aynı dildeki metinde ya da kart özetlerinde cümle ortasında büyük harfle geçen ve kartta küçük harfle yazılmamış sözcükler alınır; "The/St" gibi ekler atılır, bitişik büyük harfli sözcükler tek ad sayılır. Haber başına **tek** uyarı hepsini listeler; "eksik" diye zaten bildirilen öğe tekrarlanmaz | Uyarı (eksik olan dile) |
| **Yedek yazılmadı** | Model yedeği atlayıp kod kart metnini koyduysa o haberde başka denetim çalışmaz | Tek uyarı |
| **"Neden önemli" tekrarı** | "Neden önemli"deki ≥ 4 harfli içerik sözcüklerinin ≥ %70'i başlık+gövdede geçiyorsa | Uyarı |
| **Örnek kopyası** | Ses rehberinin örnek sayısından 8+ kelimelik aynı dizi (`DraftCommand` örnek bölümünü verir) | Uyarı |
| **Giriş** | Girişte üçten fazla haberin adı geçiyorsa | Uyarı |
| **Zaman sözcüğü** | "bir yıl/a year", "geçen hafta/last week", "aylar önce/months ago", "on yıl/decades", haftanın günleri kartta yoksa | Uyarı |
| **Geçmiş/gelecek** | Haber, gönderim tarihinden önceki bir tarihi (son 60 gün) anıp gelecek zaman kullanıyorsa (-acak/-ecek, will). Tarih "November 14th" / "14th November" gibi sıra ekiyle de okunur. Haberin **kart** metni gönderimden önceki 14 gün içinde bir tarih söylüyor ve metin tarihsiz de olsa gelecek zaman kullanıyorsa ayrı uyarı | Uyarı (en iyi çaba) |
| **Kalıp "Neden önemli"** | "öncü bir grup", "geniş kitle", "doğrudan belirliyor", "kendine yer buluyor", "hâlâ bu mirasla", "pek çok/birçok müzisyen", "many musicians", "a favorite of", "yeniden yer alacak", "proving", "reach well beyond", "pioneering", "coveted" | Uyarı |
| "Bu hafta" | Sayı önceki haftayı anlatır; "bu hafta/this week" yazılmaz | Uyarı |
| Outro | YZ ya da yapay zekâ geçen cümle silinir (kod notu kendisi ekler); outro boş kalırsa dilin standart kapanışı | Temizlenir ve uyarı verilir |
| Konu / önizleme | konu ≤ 60 (aşarsa uyarı yayımlanacak biçimi gösterir: son kanca ayracında — ", " "; " " ve " " and " — kesilir, "…" yok), önizleme ≤ 90 karakter; konu " · TBL Weekly" ile biter; önizleme konuyu tekrarlamaz; giriş TR "Beş haber, beş dakika." / EN "Five stories, five minutes." içerir | Uyarı |
| Kopya | Özetten 8+ kelimelik aynı dizi. Başlık bir kaynak başlığıyla eşleşiyorsa tek düşük öncelikli not ("headline matches X's headline"); gövde/neden önemli kopyaları uyarı kalır | Uyarı |
| Abartı | Ses rehberinin yasakladığı sıfatlar (efsane, inanılmaz, "tarihi bir", legendary, iconic…) | Uyarı |

PR açıklamasının "Neye bakmalı" bölümü ayrıca, bir haberin özetleri farklı "Ay gün" tarihleri
söylüyorsa "dates differ across sources: Jan 27 (Stereogum) / Jan 29 (UCR, Consequence)" satırını
taşır. Ingest, özetin başındaki başlık tekrarını ve sondaki "başlık + yazar adı" kuyruğunu
(ör. Consequence "… Paolo Ragusa") temizler.

### 3a. Otomatik düzeltme (`DraftRepair`, 2026-11-10)

Mehmet metne dokunmadan yalnız okuyup merge etsin diye, bir dil taslağı çözümlenip denetlendikten
sonra **düzeltilebilir** uyarı varsa dil başına **en fazla bir** ek Gemini isteği yapılır: modelin kendi
JSON yanıtı + olgu kartı + düz uyarı listesi gider, aynı şemada tam JSON istenir (ses rehberi örneği
gitmez, kopyalanacak bir şey yok; Spotify/abone verisi yok). Düzeltilebilir sayılanlar: "neden önemli"
gövdeyi tekrar ediyor, kalıp ifade, örnek sayı kopyası, geçmiş/gelecek zaman ya da zaman sözcüğü, konu
(60) ya da önizleme (90) sınırı aşımı, konuda " · TBL Weekly" eksik, önizleme konuyu tekrarlıyor.
Eksik yedek, TR/EN eşitliği ve "eksik" uyarıları bu geçişte düzeltilmez.

- Onarılmış yanıt çözümlenir ve yeniden denetlenir; **yalnız çözümlenirse ve toplam uyarı sayısı kesin
  olarak azsa** kabul edilir. Eşitlikte ya da kötüleşmede ilk taslak kalır.
- Onarım isteği başarısız olursa (kota, HTTP, biçim) ilk taslak kalır; PR açıklamasında
  "Otomatik düzeltme: başarısız (...); ilk taslak korundu" yazar. Onarım çalışmayı **asla** düşürmez.
  Onarım isteği yalnız bir kez yeniden denenir (HTTP düzeyinde 2 deneme; yazımda 4).
- PR açıklaması ve iş özeti dil başına "Otomatik düzeltme: X uyarı → Y uyarı" ya da "gerekmedi"
  yazar (sayılar TR/EN eşitliği denetiminden önceki sayılardır).
- Onarım, dili yazan modelle yapılır (yedek model yazdıysa yedek model).
- Prompt kuralları (`whyItMatters`): gövdede **olmayan** bağlam ekler (tarih, sonuç, neye
  bakılacağı/neyin dinleneceği), yalnız olgu kartından; gövdeyi yeniden anlatmaz, bariz olanı
  söylemez, önem iddiası yok. Konu: model kendisi ≤ 60 karakter yazar; sınır " · TBL Weekly"
  (13 karakter) dahil verilir, yani kancalara en çok 47 karakter kalır. Hafif model prompt'unda aynı
  kurallar vardır.

### 3b. Son kontrolcü ve otomatik yayın (`DraftChecker`, `PublishDecision`, 2026-11-10 akşam)

İnsan incelemesi kalktığı için (ADR-0006 madde 2) onarımdan sonra her dil için **bir** Gemini
çağrısı "son kontrolcü" rolünde çalışır. Girdi: olgu kartı + yayımlanacak metnin düz hâli (konu,
önizleme, giriş, 5 haber, kapanış; yedekler ve kaynak linkleri girmez, prompt'a URL girmez). Kontrol:
her iddia kartta var mı, uydurma tarih/rakam/ad yok mu, zaman kipi, "neden önemli" yeni bir şey
söylüyor mu, doğal TR/EN, konu/önizleme sığıyor mu, örnek ya da özet kopyası yok mu. Çıktı JSON
`{ pass: bool, problems: [..] }`. `pass: true` ama sorun listesi doluysa geçmedi sayılır.
Kontrolcü çağrısı başarısız olursa (kota, HTTP, biçim) **geçmedi** sayılır. Kontrolcü, dili yazan modelle
çalışır (ek model yok; kota riski büyürse yedek modele geçirilebilir, şimdilik ayrı model kullanılmaz).

**Engelleyen doğrulama problemleri** (yayımlanan metinde; yedekler sayılmaz): kartta olmayan rakam,
kartta olmayan zaman sözcüğü, gönderim tarihinden önceki olay için gelecek zaman, özetten ya da
örnek sayıdan kopya. Üslup uyarıları (kalıp ifade, tekrar, uzunluk, kısaltma, eksik öğe, TR/EN
eşitliği, özel ad sezgisi) engellemez; uydurma ad kontrolü kontrolcüye bırakılır.

**Karar** (`review/<hafta>.publish.json`, `{ autoMerge, reasons }`): iki dil de kontrolden geçti, engelleyen
problem yok **ve** hiçbir dil hafif yedek modelle yazılmadı ise `autoMerge: true`. Bir dil geçmezse (ya da
engelleyen problem varsa) o dil için **bir** kez "yeniden yaz + onar + kontrol" dönüşü denenir (aynı model,
aynı prompt); ikinci taslak geçerse o kalır, geçmezse ilk taslak ve gerekçeleri kalır. Kontrolcü
yanıt veremediyse yeniden yazılmaz (aynı kesintiye daha fazla kota harcanmaz). Hiçbir durumda
üçüncü deneme yoktur ve hiçbiri çalışmayı düşürmez.

**Workflow** (`newsletter-draft.yml`, `open-pr`): karar `true` ve `WEEKLY_BOT_TOKEN` varsa PR (draft
olmayan) açılır ve `gh pr merge --auto --merge` etkinleştirilir; zorunlu CI kontrolleri merge'ü
bekletir, branch protection ve admin merge'e dokunulmaz. Aksi hâlde draft PR açık kalır, `needs-human`
etiketi ve gerekçeleri sayan bir yorum eklenir; merge edilmezse o hafta gönderim yoktur. Secret yoksa
yalnız uyarı verilir (eski yol: draft PR, elle merge). Sebep: `GITHUB_TOKEN` olayları başka workflow'ları
tetiklemez, PR'da CI hiç koşmaz ve merge deploy/gönderimi başlatmaz (doğrulandı: GitHub docs,
"Triggering a workflow" ve `GITHUB_TOKEN` sayfaları). Branch `main` gerisinde kalırsa (strict
protection) otomatik merge beklemede kalabilir; runbook'a bak.

### 3c. Gemini çağrı bütçesi (çalışma başına)

Sayım, `generateContent` isteği = `GenerateJsonAsync` çağrısı başına HTTP isteği. Her istek içinde 429/5xx
için yeniden deneme vardır (yazım: en çok 4 HTTP denemesi; onarım ve kontrolcü: en çok 2).

| Durum | İstek (dil başına) | İki dil |
|---|---|---|
| Normal: yazım + (düzeltme gerekirse) onarım + kontrol | 2–3 | **4–6** |
| Bir dil geçmedi: + yeniden yazım + onarım? + kontrol | 5–6 | en çok 8–9 (diğer dil temiz) |
| Her iki dil bir dönüş daha | 5–6 | **10–12** |
| Bozuk yanıt yeniden isteği (yazım başına +1), yedek model yolu (varsayılan model 2 kez + yedek) | yazım 2'ye kadar × 3 aşama = en çok 6 | üstündeki satırlara eklenir; en kötü durum dil başına 12 çağrı, toplam **24** (HTTP yeniden denemeleri hariç) |

Ayrıca pazar 11:00 UTC güvenlik cron'u ve elle tetikleme PR yoksa taslağı yeniden üretir. Ücretsiz katman
**resmî** günlük/dakikalık sınırları bu repoda belgelenmemiş ve burada doğrulanmadı: **doğrulanmadı**
(sayı uydurulmaz; modelin kendi sayfasından teyit edilmeli). Kontrolcü yedek modele alınmadı: yedek
model yalnızca yazımda ve yalnız varsayılan model yanıt vermezse devreye girer.

Uyarılar dosyaya **girmez**. Job summary'de ve PR açıklamasında (üstte toplam sayı, altta tablo) hikâye hikâye
listelenir. Mehmet'in neye bakacağını bilmesi için vardır; taslağı engellemez.

### 4. Dosya biçimi — `content/weekly/2026-W46.tr.md`

```markdown
---
issue: 2026-W46
language: tr
title: "TheBluesland Weekly · 46. hafta"
subject: "Richard Lloyd öldü, YouTube Billboard'a dönüyor · TBL Weekly"   # isteğe bağlı
preview: "Oasis Rock Hall törenine gitmiyor, GTA VI için Miami'de festival var."   # isteğe bağlı
sendDate: 2026-11-16
window: { from: 2026-11-08T07:00:00Z, to: 2026-11-15T07:00:00Z }
aiAssisted: true
stories:
  - key: s-20
    sources:
      - { name: Pitchfork, url: "https://pitchfork.com/…" }
      - { name: Stereogum, url: "https://stereogum.com/…" }
---

Bu hafta Television'ın gitaristine veda ettik, …

## Television'ın kurucularından Richard Lloyd hayatını kaybetti

Tom Verlaine, Billy Ficca ve Richard Hell'le birlikte …

*Neden önemli:* Television öncü bir rock grubuydu …

Kaynaklar: [Pitchfork](…) · [Stereogum](…) · [NME](…)

…

Haftaya pazartesi yine buradayız. İyi dinlemeler.

```

- `subject` ve `preview` isteğe bağlıdır (eski dosyalar geçerli kalır); varsa `validate-weekly` 60/90
  karakter sınırını zorlar ve issue-delivery e-posta konusu/önizlemesi olarak kullanır; yoksa konu
  `title`, önizleme girişin ilk cümlesidir. Web arşivi bilinmeyen front matter alanlarını yok sayar.
- **YZ notu:** merge yalnızca onaydır, bu yüzden not "düzenlendi" demez: "yazıldı; editör seçti ve
  onayladı". Notun eski sürümü taşıyan dosyalar geçerli sayılır.
- **Hafta numarası** gönderim pazartesisinin ISO haftasıdır. Pencere varsayılan olarak o
  pazartesiden önceki pazar 10:00 (Europe/Istanbul) ile ondan 7 gün öncesi arasıdır. Çalışma
  pazar 11:15'te (Gemini kotası sıfırlandıktan sonra) başlasa da pencere 10:00'da biter. Elle
  çalıştırmada `--from/--to` ile verilebilir. `draft` adımı, pencereyi okumadan hemen önce
  `ingest` çalıştırır (GitHub schedule'ı güvenilmez); ingest başarısız olursa yalnız uyarı verilir.
- **Yedeklerin metni** dosyaya girmez; `review/<hafta>.backups.{tr,en}.md` yan dosyasına yazılır.
  issue-review bunları PR açıklamasına taşır; `review/` klasörü dala hiç commit'lenmez
  (issue-review spec §5, `validate-weekly` bunu zorlar).
- Bu biçim weekly-archive ve issue-delivery için sözleşmedir: front matter alanları onların
  spec'lerinde yalnızca eklenerek değişebilir.

### 5. `draft` alt komutu ve workflow

```
dotnet run --project tools/weekly-newsletter -- draft [--from …] [--to …] [--out content/weekly]
```

- Akış: pencere okunur → kümelenir → sıralanır → olgu kartı oluşur → TR ve EN çağrıları yapılır →
  denetimler çalışır → dosyalar yazılır → rapor basılır.
- Çıkış kodları: dosyalar yazıldıysa 0 (uyarı olsa bile). Model iki kez bozuk yanıt verdiyse,
  kota dolduysa veya yapılandırma eksikse 1.
- **`newsletter-draft.yml`:** (ilk sürüm; issue-review buna pazar zamanlamasını ve PR açan ikinci
  job'u ekledi) yalnızca elle tetiklenir, `contents: read`. Dosyaları artifact
  olarak yükler ve job summary'ye basar. `GEMINI_API_KEY` ve `NEON_NEWSLETTER_CONNECTION_STRING`
  yalnızca çalıştırma adımına verilir. Secret izolasyon testleri `GEMINI_API_KEY`'in yalnızca
  `suggest-curator-note.yml` ve `newsletter-draft.yml`'de geçmesine izin verecek şekilde
  güncellenir.

## Tech Stack

.NET 10, mevcut paketler (YamlDotNet front matter için, System.Text.Json yanıt için). Yeni
paket, tablo ve migration yok.

## Project Structure

```
tools/weekly-newsletter/Drafting/{FactCard, DraftPromptBuilder, GeminiClient, DraftResponse,
  DraftValidator, IssueMarkdownWriter, IssueWeek, DraftCommand, DraftReportFormatter}.cs
tests/TheBluesland.UnitTests/WeeklyNewsletter/Drafting/*Tests.cs
.github/workflows/newsletter-draft.yml
docs/weekly/ses-rehberi.md            (onaylı ses; prompt'a girer)
```

## Testing Strategy

Gerçek Gemini çağrısı testte **yapılmaz**; `HttpMessageHandler` sahte yanıt verir.

- **Prompt:** olgu kartında URL yok; Spotify alanı yok; 8 hikâye var; ses rehberi tam metin olarak
  var (ADR-0006 madde 1 regresyon testi).
- **Denetçi:** her kural için olumlu ve olumsuz örnek. W46 örnek sayısının kendisi sıfır uyarı
  vermeli; ses rehberindeki örnek, denetçinin kalibrasyonudur.
- **Yazıcı:** front matter round-trip, link sırası, AI notu, ISO hafta ve pencere hesabı (yıl
  dönümü dahil).
- **Komut:** sahte Gemini + Testcontainers ile uçtan uca: iki dosya, yedek yan dosyası, rapor,
  çıkış kodları.
- **Workflow:** secret izolasyonu, `contents: read`, input'ların script'e enterpolasyonla girmemesi.

## Boundaries

- **Always:** ADR-0006 girdi listesi; linkler ve AI notu kodda; uyarılar görünür.
- **Ask first:** prompt'a yeni veri türü eklemek; ücretli model; taslağı doğrudan `main`'e
  yazmak.
- **Never:** Spotify verisi; abone verisi; tam metin çekmek; uyarıları gizlemek.

## Success Criteria

- [ ] `newsletter-draft` production verisiyle çalışır ve iki dosya + yedek dosyası üretir.
- [ ] Mehmet'in değerlendirmesi: ilk gerçek taslakta TR metinde en fazla birkaç cümle düzeltme
      gerekir. Ölçüm: PR'daki diff, issue-review ile otomatik izlenir.
- [x] Ses rehberindeki örnek sayı denetçiden sıfır uyarıyla geçer. (Kalibrasyon iki kuralı örneğe uydurdu: "neden önemli" ≤ 2 cümle, kapanış ≤ 2 cümle; "TV" özel ad sayılmaz.)
- [ ] Tam build ve test yeşil; yeni paket yok.

## Kararlar (onaylandı, 2026-11-10)

1. **Ses rehberi ve örnek sayı** ([docs/weekly/ses-rehberi.md](../weekly/ses-rehberi.md)) tonun
   referansıdır.
2. **TR hitap "sen".**
3. **Yedekler de yazılır** (dil başına 8 hikâye), ama sayıya girmez.
4. **Uyarılar engellemez, görünür olur:** rakam, özel ad ve kopya denetimleri taslağı durdurmaz,
   Mehmet'e neye bakacağını söyler.
