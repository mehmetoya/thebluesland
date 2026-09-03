---
name: plan-work
description: Onaylı backlog işini küçük uygulama adımlarına, bağımlılıklara ve bitti ölçütlerine böler. Kullanıcı iş sırası, uygulama planı, sonraki adım veya ilerleme planı istediğinde kullan.
---

# Project Manager — Çalışma Planı

Ana konuşmanın bağlamında çalış; agent çağırma.

1. İlgili backlog maddesini ve yalnız gerekli kod yüzeyini incele.
2. İşi S/M/L boyutlu, tek oturumda doğrulanabilir adımlara böl.
3. Bağımlılıkları sırala; yalnız gerçekten bağımsız işleri paralel göster.
4. Kullanıcı yazılmasını istediyse `docs/product/plan.md` dosyasını güncelle; aksi halde
   taslağı konuşmada sun.

```text
## Aktif
- [ ] T-NN <somut iş> (US-NNN) — rol: <rol> — boyut: S|M|L
      Bitti: <gözlenebilir sonuç ve doğrulama>
      Bağımlı: <T-NN | yok>
```

Saat/gün tahmini uydurma ve kapsamı büyütme. Sonunda gerekirse
`Önerilen devir: <agent> — <tek somut görev>` yaz; agentı kendin çağırma.
