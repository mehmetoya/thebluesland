# Kontrol Listesi

- [ ] `dotnet build` uyarısız
- [ ] `dotnet test` yeşil
- [ ] MediatR / AutoMapper / gereksiz repository sarmalayıcı eklenmemiş
- [ ] Onaysız yeni NuGet paketi eklenmemiş
- [ ] `.Result` / `.Wait()` / `async void` yok
- [ ] `throw ex;` yok, boş catch yok
- [ ] `CancellationToken` uçtan uca taşınıyor
- [ ] Okuma sorgularında `AsNoTracking()`; döngü içinde DB çağrısı yok
- [ ] Tenant filtresi gerekçesiz aşılmamış
- [ ] Sihirli sabit yok
- [ ] Yeni endpoint yetkilendirilmiş, liste uçları sayfalı
- [ ] Secret hardcode edilmemiş, PII loglanmıyor
- [ ] Paylaşılan katmana UI/HTTP/platform bağımlılığı sızmamış
- [ ] Web ↔ Mobile arasında doğrudan referans yok
- [ ] Değişen davranış için test eklenmiş
