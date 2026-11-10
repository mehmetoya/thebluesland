# Implementation Plan: issue-delivery (TheBluesland Weekly)

Spec: [docs/specs/weekly-newsletter-issue-delivery.md](../docs/specs/weekly-newsletter-issue-delivery.md) ·
Durum: UYGULANIYOR (2026-11-10) · Kit kurulumu (T0) Mehmet'te

## Dependency Graph

```
T0 Kit hesabı, DNS, etiketler, formlar, KIT_API_KEY (Mehmet; kod beklemez)
T1 delivery.yml + DeliveryOptionsLoader ─┬─ T3 DeliveryPlanner (saf; §3 tablosu)
T2 IssueEmailRenderer (Markdig)          │
T4 KitClient (sahte handler) ────────────┴─ T5 DeliverCommand + rapor + deliver alt komutu
T5 ── T6 newsletter-deliver.yml + izolasyon testleri + doküman
```

## Checkpoint

T6 sonrası: tam build, test ve format; PR ve merge. T0 bitince: etiket id'leri `delivery.yml`'e,
`--dry-run` ile production denemesi, test aboneyle ilk gerçek gönderim.
