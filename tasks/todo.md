# Todo — story-clustering (TheBluesland Weekly)

Plan: [tasks/plan.md](plan.md) · Spec: [docs/specs/weekly-newsletter-story-clustering.md](../docs/specs/weekly-newsletter-story-clustering.md)

`FOCUSED`: `dotnet test tests/TheBluesland.UnitTests/TheBluesland.UnitTests.csproj --filter "FullyQualifiedName~WeeklyNewsletter|FullyQualifiedName~Workflows"`

## Phase 1: Yöntem

- [x] **T1: Gold set** — S
  - `Fixtures/newsletter-clustering/2026-W46.json`: 124 gerçek haber, 17 çok-haberli grup, 2
    belirsiz zincir (`ambiguousGroups`). Tuzaklar etiketsiz bırakıldı: Pink / Pink Floyd, iki
    Taylor Swift haberi, wah-wah listesi / wah-wah mucidi, McCartney milyarder / McCartney & Ringo,
    Geese saygı cover'ı / Richard Lloyd ölümü.
  - Verify: Checkpoint A (Mehmet).
- [x] **T2: `ClusterInput`, `Story`, `ClusteringOptions` (+ `clustering.yml` yükleyici), `TextNormalizer`** — S
  - Acceptance: aksan, noktalama, `P!NK` → `pink`, kısaltma tablosu; geçersiz yml → anlamlı hata.
  - Files: `Clustering/*.cs`, `clustering.yml`, `ClusteringOptionsLoaderTests`, `TextNormalizerTests`.
- [x] **T3: `TfIdfVectorizer`** — S
  - Acceptance: başlık ×2 + özetin ilk 300 karakteri; tek kelime + iki kelimelik öbek; hafta-içi
    IDF; L2 normalize seyrek vektör; boş özet.
- [x] **T4: `StoryClusterer`** — M
  - Acceptance: bağlantı = kosinüs ≥ T ∧ |Δt| ≤ 96 sa ∧ farklı kaynak; average-linkage; key =
    en küçük id; deterministik (karıştırılmış girdi → aynı çıktı); 1.000 haber < 5 sn.
- [x] **T5: Gold set testi + kalibrasyon** — S
  - Acceptance: çift bazında P ≥ 0.95, R ≥ 0.80; ölçülen değerler spec'e yazılır.
  - Sonuç: T = 0.22, P 1.00, R 0.96; iki kelimelik öbekler recall'u düşürdüğü için çıkarıldı.
  - Takip: 2026-W47 tam hafta etiketlenip ikinci gold set olarak eklenecek (tek hafta = genelleme kanıtı yok).
  - Verify: Checkpoint B.

## Phase 2: Kabuk

- [x] **T6: `MarkdownCell` çıkarımı + `ClusterReportFormatter`** — S
- [x] **T7: `ClusterCommand` + `cluster` alt komutu** — M
  - Acceptance: `[from, to)` ve `published_at ?? fetched_at` (Testcontainers); varsayılan pencere
    son 7 gün; hata → stderr + çıkış 1.
- [x] **T8: `newsletter-preview.yml` + README satırı** — XS
  - Acceptance: yalnızca `workflow_dispatch`; secret yalnızca çalıştırma adımında; izolasyon testi
    yeni dosyayı da tarıyor.

## Checkpoint C

- [x] Tam `dotnet build TheBluesland.slnx` ve `dotnet test` yeşil (633)
- [x] Spec Success Criteria işaretlendi
- [ ] Review; PR; merge; production'da `newsletter-preview` yeşil
