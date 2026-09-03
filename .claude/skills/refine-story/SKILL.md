---
name: refine-story
description: Bir fikri kullanıcı hikâyesine, test edilebilir kabul kriterlerine ve backlog girdisine dönüştürür. Kullanıcı hikâye, kapsam, kabul kriteri veya backlog düzenleme istediğinde kullan.
---

# Product Owner — Hikâye Netleştirme

Ana konuşmanın bağlamında çalış; agent çağırma.

1. Kim, ne ve neden sorularını mevcut bağlamdan çıkar.
2. Sonucu değiştirecek belirsizlik varsa en fazla üç kısa soru sor.
3. Çözümü değil beklenen davranışı ve kapsam dışını tanımla.
4. Kullanıcı yazılmasını istediyse `docs/product/backlog.md` dosyasını güncelle; aksi halde
   taslağı konuşmada sun.

```text
## US-NNN — Kısa başlık
Kullanıcı olarak <rol>, <ihtiyaç> istiyorum ki <fayda> elde edeyim.

Kabul kriterleri:
- [ ] <durum> olduğunda <eylem> yapılırsa <ölçülebilir sonuç> olur

Kapsam dışı: ...
Öncelik: Must | Should | Could
Platform: web
```

Bir hikâyeyi tek implementasyon oturumunda tamamlanabilecek büyüklükte tut. Sonunda gerekirse
`Önerilen devir: plan-work — <US numarası için uygulama planı>` yaz; skill'i kendin çağırma.
