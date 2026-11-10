# Spec: news-ingest

Modül: `news-ingest` · Map: [weekly-newsletter.md](weekly-newsletter.md) · Durum: ONAYLANDI — Mehmet, 2026-11-10

## Assumptions

1. Newsletter hattının tamamı tek bir yeni araçta yaşar: `tools/weekly-newsletter`
   (`spotify-playlist-fetcher` ile aynı desen: console uygulaması, alt komutlar, yalnız GitHub
   Actions'ta veya Mehmet'in makinesinde çalışır). Spotify aracına alt komut olarak eklenmez —
   iki aracın secret'ları ve DB rolleri ayrık kalmalı.
2. Şema, `AnalyticsDbContext` deseniyle `TheBluesland.Data` içinde ayrı bir `NewsletterDbContext`
   ve ayrı `newsletter` Postgres şemasıdır; kendi rolü (`newsletter_writer`) ve kendi connection
   string'i vardır. `TheBluesland.Web` bu context'i kaydetmez, bu şemaya erişimi yoktur.
3. Migration Neon'a ajan tarafından uygulanmaz; Mehmet `dotnet ef database update` komutunu kendisi
   çalıştırır (analytics spec'iyle aynı kural).
4. Feed özeti (RSS `description`/`summary`) yalnızca iç kullanım içindir: kümeleme ve olgu kartı
   girdisi. Hiçbir zaman olduğu gibi yayınlanmaz. Makalenin tam metni çekilmez (map: MVP dışı).
5. $0 kısıtı: GitHub Actions public repo dakikaları ve Neon ücretsiz katmanı yeterlidir (bkz.
   Success Criteria'daki hacim tahmini).

## Objective

Yapılandırmadaki müzik yayınlarının RSS/Atom akışlarından yeni haberleri periyodik olarak çekip
normalize ederek `newsletter` şemasında tekrarsız saklamak. Bu modül hattın ham verisini üretir;
tek tüketicisi `story-clustering`'dir.

- Hat olarak: son çalışmadan bu yana yayınlanan haberleri tekrarsız bir tabloda bulmak istiyorum,
  böylece kümeleme her haberi bir kez işler.
- Mehmet olarak: yeni kaynak eklemek veya bir kaynağı kapatmak için kod değil, yalnızca
  `feeds.yml` değiştirmek istiyorum.
- Mehmet olarak: bir kaynak bozulduğunda diğerlerinin çalışmaya devam etmesini ve hangi kaynağın
  bozulduğunu workflow özetinden tek bakışta görmek istiyorum.

Başlangıç kaynakları (URL'ler ve kullanım koşulları doğrulanacak — Open Questions 1): Billboard,
Pitchfork, Rolling Stone, NME, Stereogum, Consequence, Music Business Worldwide.

**Neden 2 saatte bir (2026-11-10, ilk taslaktaki 4 saatten değişti):** RSS akışları yalnızca son
10–40 öğeyi tutar. T1 ölçümünde en dar pencere Consequence'ta çıktı: 15 öğe ≈ 4,7 saat. 4 saatlik
aralık + GitHub cron gecikmesi bu pencereyi aşabilir. Günde 12 çalışma bu riski kapatır; public
repoda Actions dakikası ücretsizdir.

## Design

### 1. Veri modeli — tek tablo `newsletter.news_article`

| Kolon | Tip | Not |
|---|---|---|
| `id` | bigint identity | PK |
| `source` | text, not null | `feeds.yml`'deki kaynak `id`'si (ör. `billboard`) |
| `title` | text, not null | HTML entity'leri çözülmüş, kırpılmış |
| `url` | text, not null | Feed'deki orijinal bağlantı |
| `normalized_url` | text, not null, **unique** | Tekrar anahtarı (bkz. §2) |
| `summary` | text, null | HTML etiketleri temizlenmiş feed özeti, en fazla 2 000 karakter |
| `categories` | text[], not null, default `{}` | Feed'in `category` etiketleri (story-ranking'in tür tercihi için ucuz sinyal) |
| `published_at` | timestamptz, null | Feed'deki tarih, UTC; yoksa null |
| `fetched_at` | timestamptz, not null | İlk kez çekildiği an, UTC |

Index: `normalized_url` unique; `coalesce(published_at, fetched_at)` üzerinde zaman index'i
(kümeleme "son 7 gün" penceresini okuyacak). Kümeleme sonucu kolonları bu tabloya eklenmez —
o veri `story-clustering`'in kendi tablosunda durur.

Tekrar ekleme `INSERT ... ON CONFLICT (normalized_url) DO NOTHING` ile yapılır; aynı haber ikinci
kez geldiğinde mevcut satır **güncellenmez** (ilk görülen başlık ve `fetched_at` korunur).

### 2. URL normalizasyonu

Saf ve deterministik bir fonksiyon (`ArticleUrlNormalizer`):

- şema `https`'e, host küçük harfe çevrilir; baştaki `www.` atılır;
- fragment (`#...`) atılır; sondaki `/` atılır (kök yol hariç);
- izleme parametreleri atılır: `utm_*`, `fbclid`, `gclid`, `mc_cid`, `mc_eid`, `ref`, `cmpid`;
- kalan query parametreleri ada göre sıralanır.

Farklı yayınların aynı olayı anlatması bu modülün işi değildir (story-clustering).

### 3. Kaynak yapılandırması — `tools/weekly-newsletter/feeds.yml`

```yaml
feeds:
  - id: billboard            # kebab-case, değişmez; news_article.source buraya bağlanır
    name: Billboard
    url: https://www.billboard.com/feed/
    enabled: true
```

Başlangıçta doğrulanır: `id` tekil ve kebab-case, `url` mutlak `https`. Geçersiz yapılandırmada
araç hiçbir feed çekmeden hata kodu döner (yarım yapılandırmayla sessizce çalışmaz).

### 4. Çalışma akışı ve hata davranışı

`ingest` alt komutu:

1. `feeds.yml`'i okur, doğrular, `enabled: true` olanları alır.
2. Feed'leri **sırayla değil, sınırlı paralellikte** (en fazla 4) çeker; feed başına 20 sn timeout.
   HTTP isteği tanımlayıcı bir `User-Agent` ile gider (`TheBlueslandWeekly/1.0 (+https://<site>)`).
3. Bir feed'in HTTP hatası, timeout'u veya ayrıştırılamayan XML'i yalnız o feed'i başarısız sayar;
   diğerleri işlenir. Tek bir bozuk öğe (ör. bağlantısız) yalnız o öğeyi atlar.
4. Başarılı feed'lerin öğeleri normalize edilip tek seferde eklenir.
5. 8 haftadan eski satırlar silinir (Open Questions 3).
6. Feed başına satır içeren bir tablo GitHub job summary'sine yazılır: kaynak, durum, çekilen öğe,
   yeni eklenen, hata mesajı.

Çıkış kodu: en az bir feed başarılıysa `0`; tüm feed'ler başarısızsa veya yapılandırma geçersizse
`1`. Tek feed'in başarısızlığı workflow'u kırmızıya çevirmez (her 2 saatte bir sahte alarm
olmasın diye); süreklilik izi job summary'dedir. Bir kaynağın art arda bozuk kalması
`story-ranking`/`issue-review` aşamasında PR açıklamasında görünür hâle gelecek (o modülün spec'i).

### 5. Rol ve secret izolasyonu

- `src/TheBluesland.Data/Scripts/create-newsletter-role.sql`: `newsletter_writer` rolü; yalnız
  `newsletter` şemasında USAGE + tablolarda SELECT/INSERT/DELETE (+ identity sequence).
  `spotify_playlist_cache` ve `page_view_events` üzerinde hiçbir yetkisi yoktur.
  `create-analytics-role.sql` başlık desenini ve `RETURNING` dersini aynen uygular.
- Secret: `NEON_NEWSLETTER_CONNECTION_STRING` — yalnız newsletter workflow'larına scope edilir;
  `ci.yml`, `deploy.yml`, `sync-spotify.yml`, `suggest-curator-note.yml` bu secret'a referans vermez.
  Render'a hiç girmez.

### 6. Workflow — `.github/workflows/newsletter-ingest.yml`

`schedule: cron "17 */2 * * *"` (saat başı yoğunluğundan kaçmak için 17. dakika) +
`workflow_dispatch`. `permissions: contents: read`. `concurrency: group: newsletter-db`,
`cancel-in-progress: false` (sonraki modüllerin job'larıyla aynı grup).

## Tech Stack

- .NET 10 / C# 14, central package management (`Directory.Packages.props`)
- EF Core 10 + Npgsql (mevcut sürümler), Neon PostgreSQL
- RSS/Atom ayrıştırma: `System.ServiceModel.Syndication` — **yeni paket, onay gerekli** (Open
  Questions 2)
- YAML: `YamlDotNet` (zaten kurulu)
- Test: xUnit + Shouldly + Testcontainers.PostgreSql (zaten kurulu)

## Commands

```
Build:     dotnet build TheBluesland.slnx
Test:      dotnet test tests/TheBluesland.UnitTests/TheBluesland.UnitTests.csproj --filter "FullyQualifiedName~WeeklyNewsletter|FullyQualifiedName~NewsletterRole|FullyQualifiedName~NewsletterIngestWorkflow"
Ingest:    dotnet run --project tools/weekly-newsletter/TheBluesland.WeeklyNewsletter.csproj -- ingest
Migration: dotnet ef migrations add <Name> --context NewsletterDbContext --project src/TheBluesland.Data --output-dir Migrations/NewsletterDb
Format:    dotnet format TheBluesland.slnx --verify-no-changes
```

## Project Structure

```
tools/weekly-newsletter/
  TheBluesland.WeeklyNewsletter.csproj   → ref: TheBluesland.Data (Spotify aracına ref yok)
  Program.cs                             → alt komut yönlendirme (ingest; sonra rank/draft/send)
  feeds.yml                              → kaynak listesi
  Ingest/                                → FeedConfig(Loader), RssFeedClient, ArticleUrlNormalizer,
                                           NewsIngestService, IngestReport
src/TheBluesland.Data/
  NewsletterDbContext.cs (+ Factory)     → yalnız newsletter şeması
  Entities/NewsArticle.cs (+ Configuration)
  Migrations/NewsletterDb/
  Scripts/create-newsletter-role.sql
tests/TheBluesland.UnitTests/
  WeeklyNewsletter/                      → normalizer, feed config, RSS client, ingest servisi testleri
  Fixtures/newsletter-feeds/             → gerçek kaynaklardan alınmış örnek RSS/Atom XML'leri
  NewsArticleSchemaTests.cs, NewsletterRoleTests.cs
  Workflows/NewsletterIngestWorkflowSecretIsolationTests.cs
.github/workflows/newsletter-ingest.yml
```

`TheBluesland.Web` hiçbir dosyası değişmez.

## Code Style

Mevcut araç deseni: primary constructor'lar, `sealed`, `record` veri tipleri, her async metotta
`CancellationToken`, interface yalnız test için gerçek bir sınır varsa (HTTP). Yorumlar spec/ADR
bölümüne bağlanır.

```csharp
public sealed record FeedItem(string Source, string Title, Uri Url, DateTimeOffset? PublishedAt,
    string? Summary, IReadOnlyList<string> Categories);

public sealed class NewsIngestService(
    RssFeedClient feedClient,
    IDbContextFactory<NewsletterDbContext> dbFactory,
    TimeProvider clock)
{
    public async Task<IngestReport> RunAsync(IReadOnlyList<FeedConfig> feeds, CancellationToken ct)
    {
        var results = await Parallel.ForEachAsync(...); // en fazla 4, feed başına try/catch
        // HttpRequestException / TaskCanceledException (timeout) / XmlException -> FeedResult.Failed
        ...
    }
}
```

`TimeProvider` test edilebilir saat için kullanılır (`fetched_at`, 8 hafta temizliği).

## Testing Strategy

- **Birim (ağsız):** `ArticleUrlNormalizer` tablo tabanlı testler; `feeds.yml` doğrulaması
  (tekrarlı id, `http` URL, boş liste); `RssFeedClient` — `Fixtures/newsletter-feeds/` altındaki
  gerçek RSS 2.0 ve Atom örnekleri, mevcut `FakeHttpMessageHandler` ile; HTML temizleme ve 2 000
  karakter kırpma; tarihsiz öğe → `PublishedAt` null.
- **Hata izolasyonu:** bir feed timeout, biri bozuk XML, biri 500 döndüğünde diğerlerinin satırları
  yazılıyor ve rapor üçünü adıyla `Failed` gösteriyor; tümü başarısızken çıkış kodu `1`.
- **Entegrasyon (Testcontainers):** aynı fixture iki kez ingest edilince ikinci çalışma 0 satır
  ekliyor; `utm_` parametreli ve parametresiz aynı URL tek satır; 8 haftadan eski satır siliniyor.
- **Rol (Testcontainers):** `NewsletterRoleTests` — `newsletter_writer` gerçek
  `NewsletterDbContext` üzerinden insert/select/delete yapabiliyor; `spotify_playlist_cache` ve
  `page_view_events` tablolarına okuma/yazma yapamıyor (`AnalyticsRoleTests` deseni).
- **Workflow:** `NEON_NEWSLETTER_CONNECTION_STRING` yalnız `newsletter-ingest.yml`'de geçiyor;
  mevcut `Ci`/`Deploy` izolasyon testlerine bu secret eklenir.
- Testler gerçek RSS adreslerine istek atmaz.

## Boundaries

- **Always:** her async metotta `CancellationToken`; tarihler UTC; ingest idempotent; bir feed'in
  hatasını raporlayıp devam etmek; teslimden önce ilgili testler + tam build/test bir kez.
- **Ask first:** yeni NuGet paketi (Syndication dahil); `newsletter` şeması dışında şema değişikliği;
  mevcut workflow'lara (`ci.yml`/`deploy.yml` izolasyon testleri hariç) dokunmak; makale tam metni
  çekmek; kaynak listesine telif/kullanım koşulu belirsiz bir kaynak eklemek.
- **Never:** migration'ı Neon'a uygulamak (Mehmet uygular); `spotify_playlist_cache` veya
  `page_view_events`'e erişmek; `TheBluesland.Web`'e newsletter bağımlılığı eklemek; connection
  string'i commit'lemek; kaynak URL'lerini koda gömmek; feed özetini yayınlanacak metne olduğu gibi
  taşımak; başarısız testi onaysız silmek.

## Success Criteria

- [ ] Kaynaklar `tools/weekly-newsletter/feeds.yml`'de; kaynak eklemek/kapatmak kod değişikliği
      gerektirmiyor.
- [ ] Her haber için §1'deki alanlar saklanıyor; tüm zamanlar UTC.
- [ ] Aynı fixture art arda iki kez ingest edilince ikinci çalışma 0 satır ekliyor.
- [ ] `utm_*`/`fbclid` parametreli, `www.`'li, sonda `/`'li ve `http` varyantları tek satıra düşüyor.
- [ ] Bir feed timeout/bozuk XML/5xx döndürdüğünde diğer feed'ler yazılıyor; job summary'de hata
      kaynak adıyla görünüyor.
- [ ] Çıkış kodu: ≥1 feed başarılı → `0`; hepsi başarısız veya yapılandırma geçersiz → `1`.
- [ ] `newsletter_writer` diğer şemalardaki tablolara erişemiyor (Testcontainers ile kanıtlı).
- [ ] Workflow 2 saatte bir ve `workflow_dispatch` ile çalışıyor; secret başka workflow'da geçmiyor.
- [ ] 7 kaynağın ingest'i runner'da 60 sn altında bitiyor (paralellik 4, timeout 20 sn → en kötü
      durumda ~40 sn ağ + build hariç).
- [ ] Hacim: ~7 kaynak × ~30 haber/gün × 8 hafta ≈ 12 000 satır (birkaç MB) — Neon ücretsiz
      katmanında ihmal edilebilir.

## Kararlar (Mehmet, 2026-11-10)

1. **Kaynak URL'leri:** implementasyonun ilk adımında doğrulanıp `feeds.yml`'e yazılır; özet/başlık
   davranışı fixture'larla belgelenir. Kullanım koşulları belirsiz kaynak çıkarsa Mehmet'e sorulur.
2. **`System.ServiceModel.Syndication`:** onaylandı; `Directory.Packages.props`'a eklenir.
3. **Saklama:** 8 hafta.

## Open Questions

Yok.
