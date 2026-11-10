# Todo — story-ranking (TheBluesland Weekly)

Plan: [tasks/plan.md](plan.md) · Spec: [docs/specs/weekly-newsletter-story-ranking.md](../docs/specs/weekly-newsletter-story-ranking.md)

`FOCUSED`: `dotnet test tests/TheBluesland.UnitTests/TheBluesland.UnitTests.csproj --filter "FullyQualifiedName~WeeklyNewsletter|FullyQualifiedName~Workflows"`

- [x] **T1: `ranking.yml`, `RankingOptions` + loader, `StoryTypeClassifier`** — S
- [x] **T2: `StoryRanker`** — M (puan + eşitlik; tür başına ≤ 2; ilişkili hikâyeden tek; tür slotu; kısa hafta; determinizm)
- [x] **T3: W46 gold set testi** — S (ilk 5 ∩ Mehmet'in 5'i ≥ 4; Mehmet'in 5'i ilk 8'de)
- [x] **T4: `ArticleWindowReader` çıkarımı, `RankCommand`, `RankReportFormatter`, `rank` alt komutu** — M
- [x] **T5: `newsletter-preview.yml` `stage` girdisi, README, roadmap** — XS
- [ ] Checkpoint: tam build + test, review, PR, merge, production'da `stage: rank`

## Takip (story-clustering)

- [ ] 2026-W47 tam haftası etiketlenip ikinci clustering gold set'i olarak eklenecek.
