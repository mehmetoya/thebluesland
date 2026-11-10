# Implementation Plan: story-clustering (TheBluesland Weekly)

Spec: [docs/specs/weekly-newsletter-story-clustering.md](../docs/specs/weekly-newsletter-story-clustering.md) ·
Map: [docs/specs/weekly-newsletter.md](../docs/specs/weekly-newsletter.md) · Durum: UYGULANIYOR (2026-11-10)

Önceki modülün (news-ingest) planı git geçmişinde: `git show 1187b60:tasks/plan.md`.

## Overview

`tools/weekly-newsletter` içine saf, deterministik bir `StoryClusterer`; bunun yanına DB'den hafta
penceresini okuyan `cluster` alt komutu ve elle tetiklenen `newsletter-preview.yml`. Yeni paket,
yeni tablo ve migration yok.

## Architecture Decisions

- **Riskli olan önce:** yöntemin gerçek veride işe yarayıp yaramadığı. Gold set (T1) ve kalibrasyon
  (T5), DB, rapor ve workflow'dan önce yapılır. Karar kuralı tutmazsa I/O kodu boşa yazılmamış olur.
- **Saf çekirdek, ince kabuk:** Normalizer → Vectorizer → Clusterer ağ ve DB'ye dokunmaz. I/O
  yalnızca `ClusterCommand`'dadır.
- **Eşikler yapılandırmada:** `clustering.yml` (feeds.yml deseni). Gold set testi regresyonu korur.

## Dependency Graph

```
T1 gold set ───────────────────────────────┐
T2 tipler + options + normalizer ── T3 TF-IDF ── T4 clusterer ── T5 gold set testi + kalibrasyon
                                                                    └── T6 rapor ── T7 cluster komutu (DB) ── T8 preview workflow + doküman
```

## Checkpoints

- **A (T1 sonrası):** Mehmet gold set etiketlerine bakar. Etiket değişirse yalnızca T5 yeniden
  kalibre edilir.
- **B (T5 sonrası):** ölçüm karar kuralını geçiyor mu? Geçmiyorsa embedding için ayrı görev
  açılır ve Mehmet'e gösterilir.
- **C (T8 sonrası):** `newsletter-preview` production verisiyle yeşil; tam build ve test; review;
  PR ve merge.
