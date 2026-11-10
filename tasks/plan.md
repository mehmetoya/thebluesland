# Implementation Plan: issue-review (TheBluesland Weekly)

Spec: [docs/specs/weekly-newsletter-issue-review.md](../docs/specs/weekly-newsletter-issue-review.md) ·
ADR: [0006](../docs/adr/0006-weekly-newsletter-ai-metin-siniri.md) madde 2 ve 5 ·
Durum: UYGULANIYOR (2026-11-10)

## Dependency Graph

```
T1 yazıcı: bölüm/front matter parçaları public; yedekler + PR gövdesi review/ klasörüne
 ├─ T2 PullRequestBodyFormatter (uyarılar, seçim, kaynaklar, yapıştırılmaya hazır yedekler)
 └─ T3 WeeklyIssueValidator + validate-weekly + issue-week alt komutları; ci.yml (ready_for_review + adım)
T2 + T3 ── T4 newsletter-draft.yml: schedule ×2, open_pr, iki job; workflow testleri
T4 ── T5 doküman (drafting spec §4, roadmap, README), uçtan uca elle deneme
```

## Checkpoint

T5 sonrası: tam build, test ve format; PR ve merge; dispatch'te `open_pr: true` ile gerçek bir
draft PR; "Ready for review" ile CI'ın tetiklendiğinin görülmesi; deneme PR'ının kapatılması.
