# Spec: story-clustering

Modül: `story-clustering` · Map: [weekly-newsletter.md](weekly-newsletter.md) · Önceki modül:
[news-ingest](weekly-newsletter-news-ingest.md) · Durum: UYGULANDI — 2026-11-10 (ölçüm §3; PR bekliyor)

## Assumptions

1. Kümeleme ayrı bir zamanlanmış job değildir; bir kütüphane bileşenidir. Tüketicisi
   `story-ranking`'dir ve haftada bir, taslak hazırlanırken (pazar 11:15; pencere 10:00'da biter) bellekte çalışır. Bu
   modül DB'ye **yazmaz**, yeni tablo veya migration eklemez. Tek DB erişimi `newsletter_writer`
   rolünün zaten sahip olduğu `SELECT` yetkisiyle `news_article` okumaktır.
2. Girdi yalnızca bizim sakladığımız alanlardır: başlık, feed özeti, kaynak, kategoriler, yayın
   zamanı. Makalenin tam metni çekilmez (map: MVP dışı).
3. "Hikâye" = aynı somut olayı anlatan haberlerin kümesi. Hikâyenin **kapsamı**, onu yazan
   **farklı kaynak** sayısıdır; aynı kaynağın iki haberi kapsamı artırmaz. story-ranking'in
   "kapsam taban" puanı bu sayıdan beslenir.
4. Hacim: 2026-11-10 ölçümüyle günde ~150 yeni haber, yani haftada ≤ ~1.000 haber. Bu boyutta
   O(n²) benzerlik matrisi (≤ 1M çift) tek çekirdekte saniyeler sürer; indeks veya ANN gerekmez.
5. $0 kısıtı: varsayılan yöntem ağ çağrısı yapmaz. Embedding yalnızca aşağıdaki karar kuralı onu
   gerektirirse ve Gemini ücretsiz katmanıyla eklenir.

## Objective

Bir haftalık haber yığınını, her biri tek bir olayı anlatan hikâyelere dönüştürmek ve her
hikâye için kaç bağımsız yayının yazdığını çıkarmak. Böylece "5 haber"in her biri ayrı bir olay
olur, seçim de "kaç kaynak yazdı" sinyaline dayanır.

- story-ranking olarak: aynı olayın 3 kaynaktaki 3 haberini tek bir aday olarak, "3 kaynak"
  bilgisiyle almak istiyorum. Böylece aynı olay ilk 5'i iki kez doldurmaz, çok yazılan olay öne
  çıkar.
- issue-drafting olarak: bir hikâyenin tüm kaynak başlıklarını, özetlerini ve linklerini birlikte
  almak istiyorum. Olgu kartı tek kaynağa değil, olayı yazan bütün kaynaklara dayanmalı.
- Mehmet olarak: kümelerin doğru olup olmadığını kod okumadan, tek bir okunur özetten görmek
  istiyorum. Yanlış birleşme veya kaçan eşleşme hemen göze çarpmalı.

**Gerçek veriden örnekler (2026-11-10, DB'deki 124 haber):**

| Olay | Kaynaklar | Zorluk |
|---|---|---|
| Oasis Hall of Fame törenine katılmayacak | Stereogum, Rolling Stone, Consequence | kolay — ortak kelimeler |
| GTA VI müzik festivali | Pitchfork ("GTA VI"), Consequence ("Grand Theft Auto VI"), NME ("Grand Theft Auto 6") | zor — kısaltma, rakam |
| YouTube yayınları yeniden Billboard listelerinde | MBW, Pitchfork, Stereogum | orta — farklı fiiller |
| P!NK Sphere rezidansı | Consequence ("P!NK"), Rolling Stone ("Pink") | zor — yazım |
| McCartney & Ringo Las Vegas / VetsAid | Stereogum, Rolling Stone | orta |
| **Ayrı kalmalı:** McCartney milyarder (Forbes) | Billboard | aynı sanatçı, başka olay |
| **Belirsiz:** Geese'in Richard Lloyd'a saygı cover'ı | NME | aynı olayın devamı; iki cevap da kabul (T1'de belirsiz işaretlendi, Checkpoint A) |

## Design

### 1. Girdi ve çıktı sözleşmesi

```csharp
// Girdi: [from, to) aralığında etkin zamanı (published_at ?? fetched_at) düşen haberler.
public sealed record ClusterInput(long Id, string Source, string Title, string? Summary,
    string Url, DateTimeOffset EffectiveAt);

// Çıktı: her haber tam olarak bir hikâyede. Tek haberlik hikâyeler de çıktıda yer alır.
public sealed record Story(
    string Key,                          // en küçük article id'si ("s-1234"): aynı girdide aynı key
    IReadOnlyList<ClusterInput> Articles, // EffectiveAt'e göre eskiden yeniye
    IReadOnlyList<string> Sources);      // farklı kaynaklar, olayı ilk yazandan başlayarak
// SourceCount => Sources.Count; FirstSeen/LastSeen => ilk/son haberin zamanı
// Hikâyeler: kaynak sayısı azalan, sonra FirstSeen, sonra key.
```

Hafta penceresini çağıran (story-ranking) belirler; bu modül yalnızca `[from, to)` alır.

### 2. Yöntem: TF-IDF kosinüs benzerliği + zaman penceresi + ortalama bağlantı

Deterministiktir ve ağ çağrısı yapmaz. Varsayılan yöntem budur.

1. **Metin:** başlık iki kez yazılır (ağırlık), ardından özetin ilk 300 karakteri eklenir.
   Metin küçük harfe çevrilir, aksanlar kaldırılır (`Céline` → `celine`), noktalama atılır.
   Kısaltma tablosu yalnızca gold set'te kanıtlanan durumları içerir: `grand theft auto` → `gta`
   ve `p!nk` → `pink`. `6` → `vi` denendi ve etkisiz çıktığı için eklenmedi. Genel bir eş-anlamlı
   sözlüğü **yoktur**.
2. **Ağırlıklandırma:** yalnızca tek kelimeler kullanılır, IDF o haftanın haberlerinden
   hesaplanır. İki kelimelik öbekler ölçüldü ve çıkarıldı: farklı cümlelerle yazılmış başlıklarda
   recall'u düşürdüler (aynı precision'da 0.96'dan 0.76'ya). Böylece haftanın her yerinde geçen "tour", "new", "album" gibi
   kelimelerin ağırlığı kendiliğinden düşer.
3. **Bağlantı kuralı:** iki haber aynı hikâyedir, eğer:
   - kosinüs benzerliği ≥ `T` ise,
   - **ve** etkin zamanları arasındaki fark ≤ 96 saat ise,
   - **ve** iki haber aynı kaynaktan **değilse**. Aynı kaynağın iki haberi doğrudan bağlanmaz,
     ancak başka bir kaynak üzerinden aynı kümeye düşebilir. Bu kural "Billboard'un wah-wah pedalı
     listesi" ile "wah-wah mucidi öldü" haberinin birleşmesini engeller.
4. **Kümeleme:** ortalama bağlantılı (average-linkage) birleştirme `T` eşiğinde durur. Tek
   bağlantı (single-link) kullanılmaz: zincirleme etkisiyle "McCartney milyarder" ile "McCartney
   & Ringo"yu ortak bir üçüncü haber üzerinden birleştirebilir.
5. `T` ve kısaltma tablosu kodda sabit değil, `tools/weekly-newsletter/clustering.yml`
   dosyasında durur. Kalibrasyon kod değiştirmeden yapılır (feeds.yml deseni).

### 3. Gold set ve ölçüm — yöntem seçimi kanıta dayanır

- **Gold set:** bu haftanın 124 gerçek haberi (başlık + özet + kaynak + zaman),
  `Fixtures/newsletter-clustering/2026-W46.json` dosyasında. Her haber için elle verilmiş
  `expected` hikâye etiketi bulunur. Etiketleri ben önerir, Mehmet onaylar (T1). Kararsız durumlar
  (Olivia Rodrigo – Robbie Williams – "Christmas Wrapping" zinciri, NewJeans sözleşmesi ve HYBE
  yanıtı) dosyada `ambiguous` olarak işaretlenir ve skora girmez.
- **Metrik:** çift bazında precision ve recall. Bir çift, "aynı hikâye" olarak etiketlenmiş iki
  haberdir.
- **Karar kuralı (onaylanırsa bağlayıcı):**
  - Gold set'te **precision ≥ 0.95** ve **recall ≥ 0.80** ise yalnızca TF-IDF ile devam edilir;
    embedding eklenmez.
  - Recall < 0.80 ise ayrı bir görevde `gemini-embedding-001` embedding'i eklenir. Ücretsiz
    katman kullanılır, haftada en fazla ~10 batch istek atılır. Benzerlik iki skorun en yükseği
    olur. TF-IDF fallback olarak kalır: Gemini cevap vermezse hafta yine kümelenir ve özette ⚠️
    görünür.
  - Precision < 0.95 olursa `T` yükseltilir. Yanlış birleşme, kaçan eşleşmeden daha pahalıdır:
    iki ayrı olay tek haber gibi yazılır.
- Gold set testi CI'da her build'de çalışır. Eşik veya tablo değişikliği skoru düşürürse test
  kırılır.

**Ölçüm (2026-11-10, 2026-W46, 124 haber):** `T = 0.22` ile **precision 1.00, recall 0.96**.
Karar kuralı TF-IDF'te kalmayı söylüyor; embedding eklenmedi.

- **Kaçan tek eşleşme:** Sepultura'nın iki haberi (kosinüs 0.219).
- **Geese:** belirsiz sayılmazsa precision 0.92 olur. O durumda iki "yanlış birleşme"nin ikisi de
  Geese saygı cover'ının Richard Lloyd hikâyesine katılmasıdır.
- **Eşik aralığı dar:** `T = 0.18`'de bir yanlış birleşme başlıyor, `0.26`'da recall 0.76'ya
  düşüyor.
- **Tek haftalık veri, genelleme kanıtlanmadı.** Bir sonraki tam hafta (2026-W47) etiketlenip
  ikinci gold set olarak eklenecek. Skor düşerse eşik yeniden ayarlanacak veya embedding görevi
  açılacak.

### 4. Okunur rapor — `cluster` alt komutu

Bu modülün tek "UI"si budur. `ingest` özetiyle aynı dili kullanır.

```
dotnet run --project tools/weekly-newsletter -- cluster [--from 2026-11-03] [--to 2026-11-10]
```

Çıktı Markdown'dır ve GitHub job summary'de okunur:

```
## Story clusters · 2026-11-03 → 2026-11-10

124 articles → 101 stories · 17 covered by 2+ sources · method: TF-IDF (T = 0.42)

| # | Story (earliest headline) | Sources | Articles |
|---:|---|---|---:|
| 1 | Oasis Won't Attend Their Rock & Roll Hall Of Fame Induction | Stereogum · Rolling Stone · Consequence | 3 |
| 2 | GTA VI Is Putting on a Music Festival | Pitchfork · Consequence · NME | 3 |
…

<details><summary>Single-source stories (84)</summary> … </details>
```

- Çok kaynaklı hikâyeler kaynak sayısına göre (eşitlikte en erken haber) sıralanır. Tek
  kaynaklılar katlanmış bölümde durur.
- Her hikâyenin altında üye başlıkları ve linkleri vardır. Yanlış bir birleşmeyi görmek için
  satıra bakmak yeter.
- Feed kaynaklı metin `IngestReportFormatter.Cell` ile aynı kurallarla escape edilir. Bu kod
  paylaşılan bir yardımcıya taşınır.
- Workflow: `.github/workflows/newsletter-preview.yml` (yalnızca `workflow_dispatch`).
  `NEON_NEWSLETTER_CONNECTION_STRING` yalnızca çalıştırma adımına verilir. Haftalık hat kurulana
  kadar Mehmet kümeleri buradan görür; sonra bu adım haftalık workflow'a taşınır.

## Tech Stack

.NET 10 / C# 14 ve mevcut paketler (YamlDotNet, EF Core + Npgsql). **Yeni paket yok.** TF-IDF ve
kümeleme ~150 satırlık saf C# ile yazılır; ML kütüphanesi eklenmez.

## Commands

```
Build:    dotnet build TheBluesland.slnx
FOCUSED:  dotnet test tests/TheBluesland.UnitTests/TheBluesland.UnitTests.csproj --filter "FullyQualifiedName~WeeklyNewsletter"
Full:     dotnet test TheBluesland.slnx
Preview:  NEON_NEWSLETTER_CONNECTION_STRING=… dotnet run --project tools/weekly-newsletter -- cluster
```

## Project Structure

```
tools/weekly-newsletter/
  clustering.yml                    → T, zaman penceresi, kısaltma tablosu
  Clustering/ClusterInput.cs, Story.cs
  Clustering/TextNormalizer.cs      → küçük harf, aksan, noktalama, kısaltma
  Clustering/TfIdfVectorizer.cs     → seyrek vektör, hafta-içi IDF
  Clustering/StoryClusterer.cs      → bağlantı kuralı + average-linkage
  Clustering/ClusterCommand.cs      → DB okuma + rapor
  Clustering/ClusterReportFormatter.cs
  Reporting/MarkdownCell.cs         → IngestReportFormatter'dan çıkarılan escape yardımcısı
tests/TheBluesland.UnitTests/WeeklyNewsletter/Clustering/*Tests.cs
tests/TheBluesland.UnitTests/Fixtures/newsletter-clustering/2026-W46.json
.github/workflows/newsletter-preview.yml
```

## Code Style

news-ingest'teki gibi: sealed record'lar, TimeProvider, `GeneratedRegex` ve zaman aşımı, saf
fonksiyonlar (`StoryClusterer.Cluster(inputs, options)` DB'ye dokunmaz), I/O yalnızca
`ClusterCommand`'da. Determinizm zorunludur: aynı girdi her zaman aynı hikâyeleri, aynı
sırayla ve aynı key'lerle üretir. Eşitlikler id ile kırılır; `Dictionary` sırasına güvenilmez.

## Testing Strategy

xUnit + Shouldly. Önce test yazılır (TDD).

- **Normalizer:** aksan, noktalama, kısaltma, `P!NK` gibi uç durumlar.
- **Vectorizer:** IDF, iki kelimelik öbekler, boş özet.
- **Clusterer:** bağlantı kuralının her üç koşulu (eşik, 96 saat, aynı kaynak); average-linkage'ın
  zinciri kırdığını gösteren sentetik üçlü; determinizm (girdi karıştırılınca çıktı aynı);
  tek haberli girdi; boş girdi.
- **Gold set:** `2026-W46.json` üzerinde precision ≥ 0.95, recall ≥ 0.80. Eşik regresyonu korur.
- **Rapor:** escape, sıralama, katlanmış bölüm.
- **Hata yakalama:** EF'in bağlantı hatasını `InvalidOperationException` ile sarması da yakalanır (`DatabaseErrors`).
- **ClusterCommand:** Testcontainers Postgres ile `[from, to)` sınırları ve
  `published_at ?? fetched_at` kuralı.
- Workflow secret izolasyon testi yeni workflow'u da otomatik tarar.

## Boundaries

- **Always:** saf ve deterministik çekirdek; eşikler yapılandırmada; gold set testi yeşil.
- **Ask first:** embedding eklemek (karar kuralı tetiklese bile ayrı görev, Mehmet görür); yeni
  paket; DB'ye kümeleme sonucu yazmak.
- **Never:** Spotify verisi; makale tam metni çekmek; feed özetini dışarıya yayınlamak; gold set'i
  geçmek için etiketleri değiştirmek (etiket değişikliği ayrı commit ve gerekçeyle yapılır).

## Success Criteria

- [x] Gold set'te çift bazında precision ≥ 0.95, recall ≥ 0.80 (veya karar kuralına göre
      embedding görevi açılmış).
- [x] Gold set tuzakları ayrı hikâyelerde: McCartney milyarder, Pink Floyd, iki Taylor Swift haberi, wah-wah listesi.
- [x] Aynı girdi her çalıştırmada byte byte aynı raporu üretir.
- [x] 1.000 haberlik sentetik hafta < 5 sn'de kümelenir.
- [ ] `newsletter-preview` workflow'u production verisiyle çalışır; özet tek bakışta okunur.
- [x] Tam `dotnet build` + `dotnet test` yeşil (633, Release); yeni paket yok, migration yok.

## Kararlar (onaylandı, 2026-11-10)

1. **Önce TF-IDF, embedding yalnızca ölçüm isterse** (§3 karar kuralı).
2. **Sonuçlar DB'ye yazılmaz;** kümeleme her hafta bellekte yeniden yapılır.
3. **`newsletter-preview.yml`** (yalnızca elle tetiklenir) eklenir; Mehmet kümeleri job
   summary'de görür.

## Open Questions

- Yok. Yukarıdaki üç karar onaylanırsa plana geçilir. Gold set etiketleri T1'de Mehmet'e
  gösterilir (tek tablo, yaklaşık 2 dakikalık kontrol).
