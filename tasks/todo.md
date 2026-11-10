# Todo — news-ingest (TheBluesland Weekly)

Plan: [tasks/plan.md](plan.md) · Spec: [docs/specs/weekly-newsletter-news-ingest.md](../docs/specs/weekly-newsletter-news-ingest.md)

Odaklı test komutu (aşağıda `FOCUSED`):

```
dotnet test tests/TheBluesland.UnitTests/TheBluesland.UnitTests.csproj --filter "FullyQualifiedName~WeeklyNewsletter|FullyQualifiedName~NewsletterRole|FullyQualifiedName~NewsArticleSchema|FullyQualifiedName~Workflows"
```

## Phase 1: Foundation

- [x] **T1: Kaynakları doğrula, `feeds.yml` ve fixture'ları oluştur** — S
  - Sonuç (2026-11-10): 7/7 kaynak 200 + geçerli RSS 2.0; hiçbiri Atom değil. Billboard ve
    Consequence'ta müzik-only akış seçildi. Fixture'lardan tam metin (`content:encoded`) çıkarıldı.
    En dar pencere Consequence: 15 öğe ≈ 4,7 saat (çalışma aralığı kararı Checkpoint A'da).
  - Description: 7 kaynağın gerçek RSS/Atom URL'ini bul ve bir kez çek; yanıt biçimini (RSS 2.0/Atom,
    özet var mı, kategori var mı, tarih biçimi) not et; her kaynaktan 3–5 öğelik kırpılmış örneği
    fixture olarak kaydet; `feeds.yml`'i yaz.
  - Acceptance:
    - [ ] `feeds.yml`'de 7 kaynak; erişilemeyen/koşulu belirsiz olan `enabled: false` + yorumla gerekçe
    - [ ] Her etkin kaynak için `Fixtures/newsletter-feeds/<id>.xml` gerçek yanıttan kırpılmış
    - [ ] Özet/başlık-only davranışı `feeds.yml` yorumunda belgeli
  - Verify: Mehmet listeyi görür (Checkpoint A); XML dosyaları `xmllint --noout` ile geçerli
  - Dependencies: Yok
  - Files: `tools/weekly-newsletter/feeds.yml`, `tests/TheBluesland.UnitTests/Fixtures/newsletter-feeds/*.xml`

- [x] **T2: Araç iskeleti + `feeds.yml` yükleyici/doğrulayıcı** — M
  - Description: Yeni console projesi (`TheBluesland.WeeklyNewsletter`), slnx ve test projesine
    referans; `Program.cs` alt komut yönlendirmesi (`ingest` şimdilik yalnız config yükler);
    `FeedConfigLoader` YAML okur ve doğrular.
  - Acceptance:
    - [ ] Tekrarlı `id`, kebab-case olmayan `id`, `http`/göreli URL, boş liste → anlamlı hata, çıkış 1
    - [ ] Geçerli config → yalnız `enabled: true` kaynaklar döner
    - [ ] Projenin Spotify aracına ve Web'e referansı yok
  - Verify: `FOCUSED`; `dotnet build TheBluesland.slnx`
  - Dependencies: T1 (gerçek `feeds.yml` ile smoke testi)
  - Files: `tools/weekly-newsletter/TheBluesland.WeeklyNewsletter.csproj`, `Program.cs`,
    `Ingest/FeedConfig.cs`, `TheBluesland.slnx`, `tests/.../TheBluesland.UnitTests.csproj`,
    `tests/.../WeeklyNewsletter/FeedConfigLoaderTests.cs`

### Checkpoint A
- [x] `FOCUSED` yeşil, build temiz (22 test; yerelde `-p:NuGetAudit=false` ile — bkz. NuGet audit notu)
- [x] Mehmet T1 kaynak listesini onayladı (2026-11-10; çalışma aralığı 2 saat)

## Phase 2: Core

- [x] **T3: `RssFeedClient` (RSS 2.0 + Atom → `FeedItem`)** — M
  - Sonuç: hata sözleşmesi HTTP → `HttpRequestException`, zaman aşımı → `TimeoutException`, okunamayan
    feed → `XmlException` (Syndication'ın `NotSupportedException`'ı dahil normalize edildi). Bozuk tarih
    öğeyi düşürmez, yalnız `PublishedAt` null olur (`DateTimeParser` kancası). DTD/entity çözümlenmez.
  - Description: `System.ServiceModel.Syndication` paketini ekle; `HttpClient` ile çek (20 sn timeout,
    tanımlayıcı User-Agent), ayrıştır; HTML temizle, entity çöz, özeti 2 000 karaktere kırp;
    kategorileri al; tarih yoksa null; bağlantısız öğeyi atla.
  - Acceptance:
    - [ ] T1'deki her fixture beklenen öğe sayısını ve alanları üretiyor
    - [ ] Tek bozuk öğe yalnız kendini düşürüyor; bozuk XML → `XmlException` (çağırana)
    - [ ] WordPress eki ("The post … appeared first on …") özetten atılıyor; yalnız ekten oluşan
          özet (Stereogum) → `Summary` null
    - [ ] Atom: gerçek kaynak yok, küçük sentetik `atom-sample.xml` fixture'ı ile destek kanıtlı
    - [ ] Testler `FakeHttpMessageHandler` kullanıyor, gerçek ağ yok
  - Verify: `FOCUSED`
  - Dependencies: T1, T2
  - Files: `Directory.Packages.props`, `tools/weekly-newsletter/*.csproj`, `Ingest/RssFeedClient.cs`,
    `Ingest/FeedItem.cs`, `tests/.../WeeklyNewsletter/RssFeedClientTests.cs`

- [ ] **T4: `ArticleUrlNormalizer`** — S
  - Description: Spec §2 kurallarını uygulayan saf fonksiyon.
  - Acceptance:
    - [ ] `http`/`https`, `www.`, büyük harf host, sondaki `/`, fragment, `utm_*`/`fbclid`/`gclid`/
          `mc_cid`/`mc_eid`/`ref`/`cmpid` varyantları aynı sonuca iniyor
    - [ ] Kalan query parametreleri sıralı; kök yol `/` korunuyor
  - Verify: `FOCUSED` (tablo tabanlı `[Theory]`)
  - Dependencies: T2
  - Files: `Ingest/ArticleUrlNormalizer.cs`, `tests/.../WeeklyNewsletter/ArticleUrlNormalizerTests.cs`

- [ ] **T5: `NewsletterDbContext` + `NewsArticle` + migration** — M
  - Description: `newsletter` şeması, spec §1 kolonları, `normalized_url` unique,
    `coalesce(published_at, fetched_at)` index'i; design-time factory; ilk migration
    `Migrations/NewsletterDb/` altına.
  - Acceptance:
    - [ ] Migration Testcontainers Postgres'e temiz uygulanıyor; tablo `newsletter` şemasında
    - [ ] Aynı `normalized_url` ile ikinci insert unique ihlali veriyor
    - [ ] `TheBlueslandDbContext` ve `AnalyticsDbContext` snapshot'ları değişmedi
  - Verify: `FOCUSED` (`NewsArticleSchemaTests`)
  - Dependencies: Yok (T2 ile paralel olabilir)
  - Files: `src/TheBluesland.Data/NewsletterDbContext.cs`, `NewsletterDbContextFactory.cs`,
    `Entities/NewsArticle.cs`, `Entities/NewsArticleConfiguration.cs`, `Migrations/NewsletterDb/*`,
    `tests/.../NewsArticleSchemaTests.cs`

- [ ] **T6: `create-newsletter-role.sql` + `NewsletterRoleTests`** — S
  - Description: `newsletter_writer` rolü — yalnız `newsletter` şemasında USAGE, SELECT/INSERT/DELETE,
    sequence kullanımı; başlık `create-analytics-role.sql` desenini izler (parola placeholder + rotate).
  - Acceptance:
    - [ ] Rol, gerçek `NewsletterDbContext` üzerinden insert (RETURNING dahil), select, delete yapabiliyor
    - [ ] `spotify_playlist_cache` ve `page_view_events`'e okuma/yazma reddediliyor
    - [ ] Script iki kez çalıştırılınca hata vermiyor
  - Verify: `FOCUSED` (`NewsletterRoleTests`, Testcontainers)
  - Dependencies: T5
  - Files: `src/TheBluesland.Data/Scripts/create-newsletter-role.sql`, `tests/.../NewsletterRoleTests.cs`

- [ ] **T7: `NewsIngestService` — paralel çekim, hata izolasyonu, idempotent insert** — M
  - Description: Etkin feed'leri en fazla 4 paralel çeker; her feed'in hatası (`HttpRequestException`,
    `TimeoutException`, `XmlException` — T3 sözleşmesi) yalnız o feed'i `Failed` yapar; öğeleri normalize edip
    `ON CONFLICT (normalized_url) DO NOTHING` ile yazar; `fetched_at` `TimeProvider`'dan.
    Sonuç: feed başına (çekilen, yeni eklenen, durum, hata) içeren `IngestReport`.
  - Acceptance:
    - [ ] Aynı fixture seti iki kez → ikinci çalışma 0 yeni satır; mevcut satır güncellenmiyor
    - [ ] Timeout + bozuk XML + 500 dönen üç feed varken diğerlerinin satırları yazılıyor, rapor üçünü adıyla `Failed` gösteriyor
    - [ ] Aynı haberin `utm_` varyantı tek satır
  - Verify: `FOCUSED` (Testcontainers entegrasyon)
  - Dependencies: T3, T4, T5
  - Files: `Ingest/NewsIngestService.cs`, `Ingest/IngestReport.cs`,
    `tests/.../WeeklyNewsletter/NewsIngestServiceTests.cs`

### Checkpoint B
- [ ] `FOCUSED` yeşil; fixture → DB uçtan uca çalışıyor
- [ ] Değişiklikler task başına commit'lendi

## Phase 3: Çalıştırma ve teslim

- [ ] **T8: `ingest` komutu — retention, rapor, çıkış kodları** — S
  - Description: `Program.cs`'te `ingest`'i bağla (`NEON_NEWSLETTER_CONNECTION_STRING`'den context);
    servis sonrası 8 haftadan eski satırları sil; `IngestReport`'u Markdown tablo olarak stdout'a
    (workflow `GITHUB_STEP_SUMMARY`'ye `tee`'ler); çıkış kodu kuralı.
  - Acceptance:
    - [ ] ≥1 feed başarılı → 0; hepsi başarısız veya config geçersiz → 1; connection string yoksa → 1 + açık mesaj
    - [ ] `fetched_at` + 8 haftadan eski (published_at'ı coalesce'lenmiş) satırlar siliniyor, yeniler kalıyor
    - [ ] Rapor tablosunda her kaynak bir satır
  - Verify: `FOCUSED`; yerelde Testcontainers'a karşı `dotnet run ... -- ingest` (fixture'lı sahte değil, gerçek ağ ile tek seferlik smoke — Mehmet'in izniyle)
  - Dependencies: T7
  - Files: `tools/weekly-newsletter/Program.cs`, `Ingest/IngestReportFormatter.cs`,
    `Ingest/ArticleRetention.cs`, `tests/.../WeeklyNewsletter/IngestCommandTests.cs`

- [ ] **T9: `newsletter-ingest.yml` + secret izolasyon testleri** — S
  - Description: `cron "17 */2 * * *"` + `workflow_dispatch`, `contents: read`,
    `concurrency: newsletter-db` (cancel-in-progress: false), restore/build/run, summary'ye `tee`.
    Başlık yorumu `sync-spotify.yml` desenini izler (secret scope'u elle kısıtlama notu).
  - Acceptance:
    - [ ] `NEON_NEWSLETTER_CONNECTION_STRING` yalnız bu workflow'da geçiyor
    - [ ] `ci.yml`, `deploy.yml`, `sync-spotify.yml`, `suggest-curator-note.yml` bu secret'a referans vermiyor (test)
    - [ ] Bu workflow Spotify/Gemini/analytics secret'larına referans vermiyor (test)
  - Verify: `FOCUSED`; `actionlint` varsa onunla
  - Dependencies: T8
  - Files: `.github/workflows/newsletter-ingest.yml`,
    `tests/.../Workflows/NewsletterIngestWorkflowSecretIsolationTests.cs`,
    `tests/.../Workflows/CiWorkflowSecretIsolationTests.cs`, `DeployWorkflowSecretIsolationTests.cs`

- [ ] **T10: Kurulum dokümanı** — XS
  - Description: README'ye (mevcut secret/rol bölümünün yanına) Mehmet'in tek seferlik adımları:
    migration uygula → rol script'i → parola rotate → connection string'i secret olarak ekle ve bu
    workflow'a scope et → `workflow_dispatch` ile ilk çalışma.
  - Acceptance:
    - [ ] Adımlar sıralı, komutlar kopyalanabilir; hiçbir gerçek parola/connection string yok
  - Verify: Mehmet okur
  - Dependencies: T9
  - Files: `README.md`

### Checkpoint C: Complete
- [ ] Tam `dotnet build TheBluesland.slnx` + `dotnet test` bir kez yeşil
- [ ] Spec Success Criteria tek tek işaretlendi
- [ ] `/review` tamamlandı, bulgular kapandı
- [ ] PR açıldı; Mehmet T10 adımlarını uyguladı ve ilk gerçek çalışma yeşil

## Bloklayıcı (modül dışı, PR öncesi zorunlu)

- [ ] **NuGet audit: mevcut paketlerde bilinen açıklar** — 2026-11-10'da yerel restore'da ortaya çıktı;
  `TreatWarningsAsErrors` yüzünden restore kırılıyor (son yeşil CI 2026-10-04, açıklar sonradan
  yayımlandı → bu branch'in PR'ında CI kırılır). Etkilenen: `SixLabors.ImageSharp` 3.1.11 (high ×4,
  moderate ×2; Web), `System.Security.Cryptography.Xml` 10.0.11 (high ×2; Data),
  transitif `System.Security.Cryptography.Pkcs` 10.0.11 (high, moderate; Data).
  - Karar Mehmet'te: sürüm yükseltme ayrı küçük PR mı, bu branch'te ayrı commit mi.
  - O zamana kadar yerel doğrulama yalnız komut satırında `-p:NuGetAudit=false` ile; repo ayarı değişmez.
