---
name: dotnet-engineering-standards
description: Kişisel projelerdeki .NET/C# mühendislik standartlarını uygular — hem yeni kod YAZARKEN hem de diff/PR İNCELERKEN. Async akışı, CancellationToken, exception ve null davranışı, adlandırma, EF Core sorgu prensipleri, API güvenliği, test kalitesi ve MediatR/AutoMapper kullanmama tercihini kapsar. C#/.NET kodu yaz, ekle, düzelt, refactor et denildiğinde de kullan — sadece "review" istendiğinde değil.
---

# .NET Mühendislik Standartları

Bu skill **nasıl kod yazılacağını** tanımlar. Hangi framework, veritabanı veya UI
sorusunun cevabı projenin `CLAUDE.md` dosyasındadır.

## Ne zaman
- **Yazma**: yeni kod, bug fix, refactor.
- **İnceleme**: diff / PR / "bu kod uygun mu".
- **Açıklama**: bir kuralın gerekçesi sorulduğunda.

## Yazma akışı
1. `references/core-rules.md` oku. Test yazıyorsan ayrıca `references/testing-rules.md`.
   Kısa ve tanıdık bir düzeltmede tekrar okuma — token harcama.
2. Komşu dosyalardaki desene uy; yeni desen icat etme.
3. Kuralları ilk seferde uygula.
4. Teslimden önce `references/review-checklist.md` üzerinden geç.

## İnceleme akışı
1. Diff'i al; verilmemişse çıkar veya iste. Kod uydurma.
2. Sadece **eklenen/değişen** satırlar hakkında bulgu üret.
3. `core-rules.md` + `review-checklist.md` uygula.
4. Format: `[Kritik|Önemli|Öneri] dosya:satır — kural` + kanıt + tek cümlelik öneri.
   Türkçe yaz. Sağlanan kuralı raporlama. Aynı kuralı dosya başına bir kez yaz.

## Bağımlılık kuralı (çelişki çıkarsa bu geçerlidir)
Projede **kurulu olan** kütüphaneleri kullan. Kurulu değilse paket ekleme, sor.
Hangi kütüphanenin kullanılacağı `CLAUDE.md` dosyasında yazar.
Tek istisna: **MediatR ve AutoMapper hiçbir koşulda eklenmez.**

## Referanslar
- `references/core-rules.md`
- `references/testing-rules.md`
- `references/review-checklist.md`
