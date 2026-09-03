# Kontrol Listesi

- [ ] İlgili testler yeşil; teslim doğrulaması görev zincirinde yalnız bir kez yapılmış
- [ ] MediatR / AutoMapper / gereksiz repository sarmalayıcı eklenmemiş
- [ ] Onaysız yeni NuGet paketi eklenmemiş
- [ ] `.Result` / `.Wait()` / `async void` yok
- [ ] `throw ex;` yok, boş catch yok
- [ ] `CancellationToken` uçtan uca taşınıyor
- [ ] Okuma sorgularında `AsNoTracking()`; döngü içinde DB çağrısı yok
- [ ] Tenant filtresi gerekçesiz aşılmamış
- [ ] Sihirli sabit yok
- [ ] Yeni HTTP yüzeyinde yetkilendirme ve yanıt sınırı değerlendirilmiş
- [ ] Secret hardcode edilmemiş, PII loglanmıyor
- [ ] Mevcut `TheBluesland.Web` / `TheBluesland.Data` sınırı korunmuş; gereksiz katman eklenmemiş
- [ ] Değişen davranış için test eklenmiş
