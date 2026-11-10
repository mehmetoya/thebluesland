# Implementation Plan: issue-drafting (TheBluesland Weekly)

Spec: [docs/specs/weekly-newsletter-issue-drafting.md](../docs/specs/weekly-newsletter-issue-drafting.md) ·
ADR: [0006](../docs/adr/0006-weekly-newsletter-ai-metin-siniri.md) · Ses: [docs/weekly/ses-rehberi.md](../docs/weekly/ses-rehberi.md) ·
Durum: UYGULANIYOR (2026-11-10)

## Dependency Graph

```
T1 IssueWeek + FactCard ── T2 prompt + yanıt ayrıştırma ── T3 denetçi (örnek sayı = kalibrasyon)
                         └ T4 GeminiClient (sahte handler) ┘
T3 ── T5 Markdown yazıcı ── T6 draft komutu (Testcontainers + sahte Gemini) ── T7 newsletter-draft.yml + izolasyon testleri + doküman
```

## Checkpoint

T7 sonrası: tam build + test + format, review, PR, merge, production'da elle `newsletter-draft`
çalıştırması ve Mehmet'in ilk gerçek taslağı okuması.
