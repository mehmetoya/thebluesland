---
name: dotnet-engineering-standards
description: TheBluesland'de C#/.NET kodu yazarken, düzeltirken veya açıkça istenen diff/PR incelemesinde mühendislik standartlarını uygular. Normal açıklama, ürün planlama ve kod değişikliği içermeyen görevlerde tetiklenmez.
---

# .NET Mühendislik Standartları

Proje mimarisini `CLAUDE.md` belirler. Bu skill yalnız kod kalitesini belirler.

## Kod yazma

1. Görev async, veri erişimi, API/güvenlik veya test içeriyorsa
   `references/coding-rules.md` dosyasının yalnız ilgili başlıklarını oku.
2. Komşu koddaki mevcut desene uy ve değişikliği küçük tut.
3. Davranış değişikliğine minimum test ekle.
4. Teslimden önce `references/review-checklist.md` ile yalnız değişen kapsamı kontrol et.

## Kod inceleme

1. Verilen veya en dar git diff'ini kullan; tüm repoyu tarama.
2. `references/review-checklist.md` dosyasını ve gerekirse `coding-rules.md` içindeki ilgili
   başlığı uygula.
3. Yalnız değişen satırlardaki gerçek sorunları Türkçe raporla:
   `[Kritik|Önemli|Öneri] dosya:satır — kural` + kanıt + tek cümlelik öneri.

Kurulu olmayan paket ekleme; kullanıcıdan onay al. MediatR ve AutoMapper ekleme.
