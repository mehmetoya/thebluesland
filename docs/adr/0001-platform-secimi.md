# ADR-0001 — Platform ve istemci mimarisi

Durum: Kabul edildi

## Bağlam
Projeler hem web hem mobil uygulama olarak yayınlanacak. Geliştirme tek kişi
tarafından, güçlü .NET birikimiyle yapılıyor.

## Karar
- **.NET 10 (LTS)** hedeflenir; desteği 14 Kasım 2028'e kadar sürüyor.
  .NET 8 ve .NET 9 Kasım 2026'da destek dışına çıkıyor.
- Mimari **API-first**: tek ASP.NET Core Minimal API, iki istemci host'u.
- Ortak Razor bileşenleri **`src/Ui.Shared` Razor Class Library** içinde durur.
  `src/Web` (Blazor WASM) ve `src/Mobile` (MAUI Blazor Hybrid) bu kütüphaneye
  bağımlıdır; **birbirlerine referans vermezler.**
- `src/Shared` DTO/API sözleşmelerini ve istemcide çalıştırılabilen deterministik
  doğrulama kurallarını taşır; UI/HTTP/platform bağımsızdır.
  **Domain entity'leri istemci projelerine açılmaz.**

## Alternatifler
- **Blazor Server**: elendi. Kalıcı sunucu bağlantısına dayanır, mobile çıkış yolu yok.
- **React (web) + React Native/Expo (mobil)**: ekosistem daha geniş ve olgun,
  mobil render performansı daha iyi. Tek kişilik .NET odaklı bir kurulumda iki ayrı
  dil ve araç zinciri anlamına geldiği için elendi.
- **Mobile → Web referansı** (ortak bileşenleri Web'de tutmak): elendi.
  Host projeler arası bağımlılık üretir; RCL bunu ortadan kaldırır.

## Sonuçlar
- DTO/API sözleşmeleri ve deterministik doğrulama kuralları sunucu ile istemciler
  arasında tek kaynaktan gelir. Domain entity'leri paylaşılmaz; veritabanı, tenant
  veya yetki gerektiren doğrulama sunucuda authoritative kalır.
- Blazor WASM'ın ilk indirme boyutu React'e kıyasla büyüktür; halka açık,
  açılış hızının kritik olduğu sayfalarda ölçülmelidir.
- **MAUI sürüm ömrü .NET'ten kısadır.** Bir MAUI majör sürümü, ardılı çıktıktan
  sonra en az 6 ay destekleniyor: MAUI 10'un desteği 11 Mayıs 2027'de bitiyor,
  .NET 10 ise Kasım 2028'e kadar destekli. Mobil host projesi backend'den bağımsız
  ve daha sık yükseltilmek zorunda; bu yüzden ince tutulur, iş mantığı içermez.
  Ayrıca .NET 11'de MAUI mobil için runtime değişiyor: Preview 4 ile CoreCLR
  varsayılan oldu, Preview 6 ile Mono yolu tamamen kaldırıldı ve `UseMonoRuntime`
  kaçış kapısı kapandı. Yani .NET 11'e geçildiğinde CoreCLR'da bir performans
  regresyonu yaşanırsa Mono'ya dönmek mümkün değil. Mobil host'un ince tutulmasının
  gerekçelerinden biri budur; geçiş öncesi performans testi planlanmalı.
- **Expo'ya geçiş senaryosunda ne korunur:** backend ve HTTP API sözleşmesi
  aynen kalır. **Ne korunmaz:** Expo C# `Shared` assembly'sini ve FluentValidation
  kurallarını çalıştıramaz; TypeScript sözleşme üretimi ve istemci tarafı
  doğrulamanın yeniden yazılması gerekir. `Ui.Shared` tamamen yeniden yazılır.
  Yani maliyet "yalnızca UI" değildir; mobil UI + istemci DTO'ları + istemci
  doğrulaması yeni teknolojiye uyarlanır.
