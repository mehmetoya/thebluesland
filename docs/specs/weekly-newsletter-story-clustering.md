# Spec: story-clustering

Modül: `story-clustering` · Map: [weekly-newsletter.md](weekly-newsletter.md) · Önceki modül:
[news-ingest](weekly-newsletter-news-ingest.md) · Durum: ONAY BEKLİYOR — 2026-11-10

## Assumptions

1. Kümeleme ayrı bir zamanlanmış job değildir; bir kütüphane bileşenidir. Tüketicisi
   `story-ranking`'dir ve haftada bir, taslak hazırlanırken (pazar 10:00) bellekte çalışır. Bu
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
| **Ayrı kalmalı:** Geese'in Richard Lloyd'a saygı cover'ı | NME | ölüm haberine bağlı ama ayrı olay |

## Design

### 1. Girdi ve çıktı sözleşmesi

```csharp
// Girdi: [from, to) aralığında etkin zamanı (published_at ?? fetched_at) düşen haberler.
public sealed record ClusterInput(long Id, string Source, string Title, string? Summary,
    IReadOnlyList<string> Categories, DateTimeOffset EffectiveAt, string Url);

// Çıktı: her haber tam olarak bir hikâyede. Tek haberlik hikâyeler de çıktıda yer alır.
public sealed record Story(
    string Key,                          // en küçük article id'si ("s-1234"): aynı girdide aynı key
    IReadOnlyList<ClusterInput> Articles, // EffectiveAt'e göre eskiden yeniye
    IReadOnlyList<string> Sources,       // farklı kaynaklar, feeds.yml sırasıyla
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen);
// SourceCount => Sources.Count
```

Hafta penceresini çağıran (story-ranking) belirler; bu modül yalnızca `[from, to)` alır.

### 2. Yöntem: TF-IDF kosinüs benzerliği + zaman penceresi + ortalama bağlantı

Deterministiktir ve ağ çağrısı yapmaz. Varsayılan yöntem budur.

1. **Metin:** başlık iki kez yazılır (ağırlık), ardından özetin ilk 300 karakteri eklenir.
   Metin küçük harfe çevrilir, aksanlar kaldırılır (`Céline` → `celine`), noktalama atılır.
   Kısaltma tablosu küçük ve sabittir (`grand theft auto` → `gta`, roma rakamı `vi`/`6` gibi
   yalnızca gold set'te kanıtlanan durumlar için). Genel bir eş-anlamlı sözlüğü **yoktur**.
2. **Ağırlıklandırma:** tek kelime ve iki kelimelik öbekler kullanılır, IDF o haftanın
   haberlerinden hesaplanır. Böylece haftanın her yerinde geçen "tour", "new", "album" gibi
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

- [ ] Gold set'te çift bazında precision ≥ 0.95, recall ≥ 0.80 (veya karar kuralına göre
      embedding görevi açılmış).
- [ ] Yukarıdaki iki "ayrı kalmalı" örneği ayrı hikâyelerde.
- [ ] Aynı girdi her çalıştırmada byte byte aynı raporu üretir.
- [ ] 1.000 haberlik sentetik hafta < 5 sn'de kümelenir.
- [ ] `newsletter-preview` workflow'u production verisiyle çalışır; özet tek bakışta okunur.
- [ ] Tam `dotnet build` + `dotnet test` yeşil; yeni paket yok, migration yok.

## Kararlar (onay bekliyor)

1. **Önce TF-IDF, embedding yalnızca ölçüm isterse** (§3 karar kuralı).
2. **Sonuçlar DB'ye yazılmaz;** kümeleme her hafta bellekte yeniden yapılır.
3. **`newsletter-preview.yml`** (yalnızca elle tetiklenir) eklenir; Mehmet kümeleri job
   summary'de görür.

## Open Questions

- Yok. Yukarıdaki üç karar onaylanırsa plana geçilir. Gold set etiketleri T1'de Mehmet'e
  gösterilir (tek tablo, yaklaşık 2 dakikalık kontrol).
