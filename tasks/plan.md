# Implementation Plan: story-ranking (TheBluesland Weekly)

Spec: [docs/specs/weekly-newsletter-story-ranking.md](../docs/specs/weekly-newsletter-story-ranking.md) ·
ADR: [0006](../docs/adr/0006-weekly-newsletter-ai-metin-siniri.md) · Durum: UYGULANIYOR (2026-11-10)

Önceki modüllerin planları git geçmişinde (`git log -- tasks/plan.md`).

## Overview

`StoryRanker`: hikâyeleri kapsam + tür ağırlığıyla puanlar, çeşitlilik kurallarını ve tür slotunu
uygular, 5 seçim + 3 yedek döndürür. Yanına `rank` alt komutu ve preview workflow'unda
`stage: rank` eklenir. Yeni paket, tablo ve migration yok.

## Dependency Graph

```
T1 ranking.yml + options + classifier ── T2 StoryRanker (puan, çeşitlilik, slot) ── T3 W46 gold set testi
                                                                                   └── T4 ortak pencere okuyucu + rank komutu + rapor ── T5 preview stage + doküman
```

## Checkpoint

T5 sonrası: tam build + test, review, PR, merge, production'da `stage: rank`.
