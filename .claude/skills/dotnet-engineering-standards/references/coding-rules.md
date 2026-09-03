# Kod Kuralları

Yalnız görevle ilgili başlığı uygula; dosyanın tamamını her görevde tekrar okuma.

## C# ve async

- .NET adlandırma kurallarını izle; `var` yalnız tip açıkken kullan.
- I/O yapan public async metotlarda `CancellationToken` taşı ve alt çağrılara geçir.
- `.Result`, `.Wait()`, `async void` (event handler hariç), `throw ex;` ve boş `catch` kullanma.
- `IDisposable` kaynakları `using` veya `await using` ile kapat.

## Tasarım ve hata davranışı

- Mevcut deseni tercih et; ikinci somut ihtiyaçtan önce soyutlama ekleme.
- Beklenen hataları açık sonuçla taşı; exception'ı akış kontrolü için kullanma.
- Sihirli sabitleri anlamlı `const` veya mevcut constants yapısına taşı.
- Yeni repository katmanı, MediatR veya AutoMapper ekleme.

## EF Core

- Mapping için `IEntityTypeConfiguration<T>`, salt-okunur sorguda `AsNoTracking()` kullan.
- Döngü içinde DB çağrısı ve kontrolsüz `Include` zinciri oluşturma.
- Çok adımlı yazmayı transaction içinde yap; migration'ı üret ama DB'ye uygulama.
- Web uygulamasının cache erişimini salt-okunur ve graceful-degradation uyumlu tut.

## Güvenlik

- Secret veya PII hardcode/loglama. Kullanıcı girdisini doğrudan SQL'e basma.
- Spotify/AI credential'larını web runtime'a taşıma; track listesini kalıcı saklama.
- Yeni HTTP yüzeyi eklenirse yetkilendirme ve sınırlı/sayfalı yanıt ihtiyacını değerlendir.

## Test

- Adlandırmada `Metot_Durum_BeklenenSonuc`; görünür Arrange–Act–Assert kullan.
- Bir testte tek davranış doğrula ve projedeki assertion kütüphanesini kullan.
- Bug fix'e regresyon testi ekle. Gerçek saat, sıra veya ağ çağrısına dayanan kırılgan test yazma.
- Birim testte DB kullanma; gerçek persistence davranışı gerektiğinde mevcut integration
  altyapısını kullan. Kapsam yüzdesi yerine riskli yolu test et.
