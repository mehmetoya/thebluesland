# Implementation Plan: news-ingest (TheBluesland Weekly)

Spec: [docs/specs/weekly-newsletter-news-ingest.md](../docs/specs/weekly-newsletter-news-ingest.md) ·
Map: [docs/specs/weekly-newsletter.md](../docs/specs/weekly-newsletter.md) · Durum: ONAY BEKLİYOR (2026-11-10)

## Overview

`tools/weekly-newsletter` adında yeni bir console aracı ve onun `ingest` alt komutu: `feeds.yml`'deki
RSS/Atom kaynaklarını 2 saatte bir çeker, URL'i normalize eder, `newsletter.news_article`'a
tekrarsız yazar, 8 haftadan eskiyi siler, kaynak başına durumu job summary'ye basar. Web'e dokunulmaz.

## Architecture Decisions

- **Ayrı araç, ayrı rol, ayrı context** — `AnalyticsDbContext` + `create-analytics-role.sql` deseni
  birebir; Spotify aracına ve Web'e referans yok.
- **Riskli olan önce:** gerçek feed'lerin erişilebilirliği/biçimi (Task 1) koddan önce doğrulanır;
  fixture'lar gerçek XML'den alınır, sonraki her test bunlara dayanır.
- **Dikey dilim mantığı:** Phase 2 sonunda "fixture XML → DB satırı" uçtan uca çalışır; Phase 3
  yalnız zamanlama/izolasyon/dokümantasyon ekler.
- **Paket:** yalnız `System.ServiceModel.Syndication` (spec kararı 2). Başka paket yok.

## Dependency Graph

```
T1 feeds + fixtures ──┐
T2 araç iskeleti + config ──┬── T3 RSS client ──┐
                            └── T4 URL normalizer ┤
T5 şema + migration ── T6 rol script ─────────────┤
                                                  └── T7 ingest servisi (DB) ── T8 komut + rapor + retention
                                                                                   └── T9 workflow + secret izolasyonu
                                                                                          └── T10 kurulum dokümanı
```

Paralel yapılabilir (tek oturumda sırayla yapılacak; settings.json eşzamanlı subagent = 1):
T3 ∥ T4, ve T5–T6 ∥ T3–T4.

## Task List

Ayrıntı, kabul kriteri ve doğrulama: [tasks/todo.md](todo.md).

### Phase 1: Foundation
- [ ] T1: Kaynakları doğrula, `feeds.yml` ve fixture'ları oluştur
- [ ] T2: `tools/weekly-newsletter` iskeleti + `feeds.yml` yükleyici/doğrulayıcı

### Checkpoint A
- [ ] Odaklı testler yeşil, `dotnet build TheBluesland.slnx` temiz; Mehmet kaynak listesini gördü

### Phase 2: Core (fixture → DB uçtan uca)
- [ ] T3: `RssFeedClient` (RSS 2.0 + Atom → `FeedItem`)
- [ ] T4: `ArticleUrlNormalizer`
- [ ] T5: `NewsletterDbContext` + `NewsArticle` + migration + şema testi
- [ ] T6: `create-newsletter-role.sql` + `NewsletterRoleTests`
- [ ] T7: `NewsIngestService` — paralel çekim, hata izolasyonu, idempotent insert

### Checkpoint B
- [ ] Fixture'lardan DB'ye uçtan uca entegrasyon testi yeşil; ikinci çalışma 0 satır ekliyor

### Phase 3: Çalıştırma ve teslim
- [ ] T8: `ingest` komutu — retention, job summary raporu, çıkış kodları
- [ ] T9: `newsletter-ingest.yml` + secret izolasyon testleri
- [ ] T10: Kurulum dokümanı (Mehmet'in elle yapacağı adımlar)

### Checkpoint C: Complete
- [ ] Tam `dotnet build` + `dotnet test` bir kez yeşil; spec Success Criteria tek tek işaretli
- [ ] `/review` (code-review-and-quality); bulgular kapandı
- [ ] Mehmet: migration + rol script'i + secret kurulumu → `workflow_dispatch` ile ilk gerçek çalışma

## Execution: skill ve agent kullanımı

| Adım | Kullanılacak | Neden |
|---|---|---|
| Her task | `incremental-implementation` + `test-driven-development` (`/build`) | Önce kırmızı test, sonra kod; task başına commit |
| C# kodu | `dotnet-engineering-standards` | Proje kuralları (CT, sealed, TreatWarningsAsErrors) |
| T3, T5, T7 | `source-driven-development` | Syndication API'si, EF Core 10/Npgsql `ON CONFLICT`, `TimeProvider` resmî dokümana karşı |
| T6 | `security-and-hardening` + `doubt-driven-development` | Yetki script'i production'da tek savunma hattı; analytics'teki `RETURNING` olayı tekrar etmesin |
| T9 | `ci-cd-and-automation` | Cron, concurrency, secret scope |
| Checkpoint C | `code-review-and-quality` (`/review`), sonra `git-workflow-and-versioning` | PR öncesi beş eksenli inceleme |

Subagent: CLAUDE.md bütçesine uyuluyor — implementasyon ana oturumda (`backend-dev` yalnız sen
istersen). Checkpoint C'de tek specialist olarak `code-reviewer`. `architect` bu modülde yok;
ADR-0006 `issue-drafting` spec'inden önce ayrı iş. `test-engineer`, `security-auditor`,
`web-performance-auditor` gerekmiyor (Web'e dokunulmuyor; rol testi T6'da zaten var).

## Risks and Mitigations

| Risk | Etki | Azaltma |
|---|---|---|
| Bir kaynağın RSS'i yok/bot engelliyor/koşulları belirsiz | Orta | T1'de erken doğrulama; ilgili kaynak `enabled: false` + Mehmet'e sorulur. 5+ çalışan kaynak MVP için yeterli |
| Ortamda dış ağ erişimi guard hook'una takılır | Orta | T1'de gerekirse Mehmet'ten tek seferlik izin; olmazsa feed URL'leri Mehmet'in tarayıcısından doğrulanır |
| Syndication'ın tuhaf tarih biçimlerinde exception atması | Orta | Öğe bazında try/catch; T3'te bozuk tarih fixture'ı |
| Rol yetkisi eksik (`RETURNING`/sequence) → production'da ilk insert hatası | Yüksek | T6 testi gerçek `NewsletterDbContext` üzerinden insert eder (analytics dersi) |
| Workflow'un 2 saatte bir sessizce başarısız olması | Düşük | Tüm feed'ler düşerse çıkış 1 → GitHub e-posta bildirimi |

## Open Questions

Yok (spec kararları 1–3 kapandı).
