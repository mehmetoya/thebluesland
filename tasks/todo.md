# Todo — issue-delivery (TheBluesland Weekly)

Plan: [tasks/plan.md](plan.md) · Spec: [docs/specs/weekly-newsletter-issue-delivery.md](../docs/specs/weekly-newsletter-issue-delivery.md)

- [ ] **T0: Kit kurulumu (Mehmet)** — hesap, posta adresi, DNS (SPF/DKIM), `weekly-tr`/`weekly-en` etiketleri, double opt-in'li iki form, `KIT_API_KEY` secret'ı, TR formunda rıza metni
- [x] **T1: `delivery.yml` + `DeliveryOptionsLoader`** — S
- [x] **T2: `IssueEmailRenderer`** — S
- [x] **T3: `DeliveryPlanner`** — M
- [x] **T4: `KitClient`** — S
- [x] **T5: `DeliverCommand`, rapor, `deliver` alt komutu** — M
- [x] **T6: `newsletter-deliver.yml`, izolasyon testleri, doküman** — S
- [ ] Checkpoint: build, test, format; PR ve merge; T0 sonrası dry-run ve test aboneyle gerçek gönderim

## Takip

- [ ] 2026-11-15 pazar: ilk zamanlanmış taslak çalışmasının draft PR açtığını ve 10:00 UTC denemesinin hiçbir şey yapmadığını doğrula.
- [ ] 2026-W47 tam haftası etiketlenip ikinci clustering gold set'i olarak eklenecek.
- [ ] İlk 4 haftadan sonra: yedekle değiştirme sıklığına göre `swap` workflow'u; düzeltme miktarını otomatik ölçmek.
