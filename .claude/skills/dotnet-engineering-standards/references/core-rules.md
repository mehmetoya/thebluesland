# Çekirdek Kurallar

## Dil ve adlandırma

- Tip/metot `PascalCase`, yerel/parametre `camelCase`, private alan `_camelCase`.
- Identifier'larda Türkçe karakter yok; yorumlar Türkçe olabilir.
- `var` yalnızca tip sağ taraftan açıkça belliyse.
- Sihirli sayı/string yok → `const` veya modülün `Constants` sınıfı.
- Enum karşılaştırmasında enum tipi veya `nameof()`; string literal değil.
- 3+ parametreli imzalarda primary constructor veya satır kırma.
- Değişmez veriyi `record` ile modelle.

## Async

- I/O yapan public metotlar `async Task`. `async void` yalnızca event handler'da.
- `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` yasak.
- `CancellationToken` her async public metotta parametredir ve I/O çağrılarına geçirilir.
- Kütüphane niteliğindeki kodda `ConfigureAwait(false)`.

## Hata yönetimi

- `catch (Exception ex) { ...; throw; }` — `throw ex;` asla.
- Boş `catch` yok; loglamadan yutma yok.
- Akış kontrolü için exception atma; beklenen hatalar sonuç tipiyle taşınır.
- `IDisposable` → `using` / `await using`.

## Katman disiplini

- Domain dış paket bilmez; invariant'lar constructor/factory'de korunur,
  `public set` açılmaz. Anemic model kaçınılır.
- Uygulama katmanı HTTP bilmez: `HttpContext`, `IActionResult` sızmaz.
- Endpoint iş kuralı içermez: doğrula → çağır → sonucu dön.
- Doğrulama tek yerde yapılır; handler içinde elle tekrar edilmez.
- **MediatR yok** — handler'lar doğrudan DI ile çağrılır.
- **AutoMapper yok** — `ToDto()` extension veya `Select(x => new XDto(...))`.
- EF Core üzerine ikinci repository katmanı sarma; `DbContext` yeterli.

## Veri erişimi (EF Core)

- Konfigürasyon `IEntityTypeConfiguration<T>` sınıflarında.
- Okuma sorgularında `AsNoTracking()`.
- Döngü içinde DB çağrısı yok; toplu iş tek sorgu veya `ExecuteUpdateAsync`.
- Lazy loading kapalı; `Include` zincirleri kontrollü (N+1 üretme).
- Tenant filtresi gerekçesiz `IgnoreQueryFilters()` ile aşılmaz.
- Çok adımlı yazmalar transaction içinde; hata halinde rollback.
- Migration adları anlamlı: `Add_Tenant_To_Orders`.

## API ve güvenlik

- Endpoint'ler varsayılan olarak yetkilendirilir; anonim olan açıkça işaretlenir.
- Token doğrulama parametreleri (issuer, audience, lifetime) açıkça set edilir.
- Kullanıcı girdisi doğrudan SQL'e veya loga basılmaz; PII loglanmaz.
- Secret'lar user-secrets veya environment'ta.
- Liste dönen uçlar sayfalanır.

## İstemci paylaşımı

- `Shared` (sözleşme katmanı): UI, HTTP ve platform bağımlılığı içermez.
  Domain entity'si burada yer almaz; yalnızca DTO ve deterministik doğrulama.
- `Ui.Shared` (sunum katmanı): Razor/UI içerebilir, ancak MAUI veya tarayıcıya
  özgü API içermez. Platform implementasyonu host projelerdedir.
- İş kuralları `Domain` içindedir; paylaşılan katmanlara taşınmaz.
- Ortak Razor bileşenleri host projelerde değil, Razor Class Library'de durur.
- Host projeler (Web, Mobile) birbirine referans vermez.
- Aynı doğrulama kuralı kopyalanmaz; paylaşılan katmandan çalıştırılır.
- Sunucuda istemciye özgü oturum durumu tutulmaz.
