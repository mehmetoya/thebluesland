# Spec: issue-drafting

Modül: `issue-drafting` · Map: [weekly-newsletter.md](weekly-newsletter.md) · Önceki modül:
[story-ranking](weekly-newsletter-story-ranking.md) · Sınır: [ADR-0006](../adr/0006-weekly-newsletter-ai-metin-siniri.md)
· Ses: [docs/weekly/ses-rehberi.md](../weekly/ses-rehberi.md) · Durum: UYGULANDI — 2026-11-10 (PR bekliyor; ilk gerçek taslak production'da çalıştırılacak)

## Assumptions

1. Bu modül taslağı **dosya olarak üretir**; PR açmaz ve zamanlanmış çalışmaz. Bunlar
   `issue-review` modülünün işidir (map). Bu modülün kendi workflow'u yalnızca elle tetiklenir ve
   taslağı job summary'de ve indirilebilir bir artifact olarak gösterir.
2. Yazım Gemini ücretsiz katmanıyla yapılır: haftada 2 çağrı (TR ve EN), her biri birkaç bin
   token. `suggest-curator-note.yml` ile aynı API ve aynı varsayılan model kullanılır
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
  dahil), o dilin bölümü, olgu kartı ve kurallar.
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
| **TR/EN eşitliği** | Bir dilin haberinde olup diğerinde olmayan rakam ve özel ad | Uyarı (eksik olan dile) |
| **Zaman sözcüğü** | "bir yıl/a year", "geçen hafta/last week", "aylar önce/months ago", "on yıl/decades", haftanın günleri kartta yoksa | Uyarı |
| **Geçmiş/gelecek** | Haber, gönderim tarihinden önceki bir tarihi (son 60 gün) anıp gelecek zaman kullanıyorsa (-acak/-ecek, will) | Uyarı (en iyi çaba) |
| **Kalıp "Neden önemli"** | "öncü bir grup", "geniş kitle", "doğrudan belirliyor", "kendine yer buluyor", "hâlâ bu mirasla", "proving", "reach well beyond", "pioneering", "coveted" | Uyarı |
| "Bu hafta" | Sayı önceki haftayı anlatır; "bu hafta/this week" yazılmaz | Uyarı |
| Outro | YZ ya da yapay zekâ geçen cümle silinir (kod notu kendisi ekler); outro boş kalırsa dilin standart kapanışı | Temizlenir ve uyarı verilir |
| Konu / önizleme | konu ≤ 60, önizleme ≤ 90 karakter; konu " · TBL Weekly" ile biter; önizleme konuyu tekrarlamaz; giriş TR "Beş haber, beş dakika." / EN "Five stories, five minutes." içerir | Uyarı |
| Kopya | Özetten 8+ kelimelik aynı dizi | Uyarı |
| Abartı | Ses rehberinin yasakladığı sıfatlar (efsane, inanılmaz, "tarihi bir", legendary, iconic…) | Uyarı |

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
