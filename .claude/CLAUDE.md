# Proje Teknoloji Sözleşmesi

Her oturumda yüklenir; **bu projenin** teknoloji kararlarını taşır.
Genel mühendislik kuralları `dotnet-engineering-standards` skill'indedir.

## Hedef platform
- .NET 10 (LTS) / C# 14. `TreatWarningsAsErrors`, nullable ve implicit usings açık.
- **API-first.** Backend hiçbir UI teknolojisini varsaymaz; tek sözleşme HTTP API'dir.

## Projeler
```
src/Api/         ASP.NET Core Minimal API
src/Domain/      entity + value object       → dış paket bağımlılığı yok
src/Infra/       EF Core, PostgreSQL, migration
src/Shared/      DTO/API sözleşmeleri + istemcide çalışabilen doğrulama
                 → UI/HTTP/platform bağımsız. Domain entity'si BURAYA GİRMEZ.
src/Ui.Shared/   Razor Class Library → ortak Razor bileşenleri (sunum katmanı).
                 Razor/UI içerir; MAUI veya tarayıcıya özgü API içermez.
src/Web/         Blazor WASM host    → platform implementasyonları burada
src/Mobile/      MAUI Blazor Hybrid host → platform implementasyonları burada
tests/
```
Katman sözleşmesi:
- **İş kuralları `Domain` içindedir**, `Shared` veya `Ui.Shared` içinde değil.
- `Domain` entity'leri istemci projelerine **açılmaz**; istemci yalnızca `Shared`
  içindeki DTO'ları görür.
- Veritabanı, tenant veya yetki gerektiren doğrulama sunucuda authoritative kalır;
  `Shared` yalnızca deterministik (girdiye bakarak karar verilebilen) kuralları taşır.
- **Web ve Mobile birbirine referans vermez.** Ortak bileşen `Ui.Shared` içine konur.

## Kararlar
- Kimlik: ASP.NET Core Identity + JWT (mobil istemci cookie kullanamaz).
- Veri: EF Core + PostgreSQL (Npgsql). Migration'lar `src/Infra/Migrations`.
- Multi-tenancy: `ITenantContext` + EF Core Global Query Filter.
- Stil: Tailwind CSS.
- Doğrulama: FluentValidation, kurallar `src/Shared` içinde.
- Test: xUnit + Shouldly + Testcontainers.

## Mobil kısıtları
- Sunucuda oturum durumu tutulmaz; her istek kendi kendine yeter.
- Yanıtlar sayfalanır ve küçüktür; bağlantı yavaş ve kesintili varsayılır.
- `src/Shared` ve `src/Ui.Shared` içine platform bağımlılığı girmez.
- **MAUI sürüm ömrü .NET'ten kısadır** (bkz. ADR-0001). Mobil host projesi backend'den
  bağımsız ve daha sık yükseltilir; bu yüzden ince tutulur. İçinde iş mantığı olmaz —
  iş kuralları `Domain`, sözleşme `Shared`, sunum `Ui.Shared` içindedir.

## Çalışma şekli
- Kod yazmadan önce ilgili klasörü oku; varsayım üretme.
- Teslimden önce `dotnet build` ve `dotnet test` yeşil olmalı.
- Mimari kararlar `docs/adr/`, gereksinimler `docs/product/backlog.md`,
  plan `docs/product/plan.md`.

## Ajanlar
architect · backend-dev · code-reviewer · test-engineer · product-owner · project-manager
