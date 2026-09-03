# ADR-0003 — Mimari kapsam: TheBluesland neden CLAUDE.md'deki çok-istemcili şablonu kullanmıyor

Durum: Kabul edildi ve onaylandı — Mehmet, 2026-09-03 tarihinde US-005 ile (ilk `TheBluesland.Web`
implementasyonu) devam kararı vererek bu sapmayı açıkça onayladı.

## Bağlam

Bu repodaki `.claude/CLAUDE.md` dosyası ve `docs/adr/0001-platform-secimi.md`, genel bir ".NET
teknoloji sözleşmesi" tanımlıyor: API-first mimari, ayrı `src/Api` (ASP.NET Core Minimal API),
`src/Domain` (entity + value object), `src/Infra` (EF Core/Postgres), `src/Shared`
(DTO/istemci-doğrulama), `src/Ui.Shared` (ortak Razor Class Library), `src/Web` (Blazor WASM host)
ve `src/Mobile` (MAUI Blazor Hybrid host). Bu şablon, hem web hem mobil istemcisi olan, tek
geliştiricinin yürüttüğü genel bir proje için scaffold edilmiş (`setup-ai-team-v5.sh`'tan kalma
görünüyor) — TheBluesland'e özel olarak yazılmamış.

TheBluesland'in kendi teknoloji kararı ise farklı: `docs/business-technical-specification.md`
(v0.2), tek bir .NET 10 Blazor Web App tanımlıyor — static SSR + isolated Interactive Server,
mobil hedef yok, ayrı bir API host yok. v0.2 ile eklenen tek paylaşılan proje
`src/TheBluesland.Data` (EF Core şema), CLAUDE.md'nin `Domain`/`Shared`/`Ui.Shared` ayrımıyla aynı
amaca hizmet etmiyor (bkz. ADR-0002, "Sonuçlar").

Bu ADR, iki mimari arasındaki farkı sessizce görmezden gelmek yerine açıkça belgelemek için
yazıldı.

## Karar

TheBluesland, repodaki genel çok-istemcili `.NET` şablonunu (CLAUDE.md, ADR-0001) **kullanmaz.**
Bunun yerine:

- Tek üretim projesi: `src/TheBluesland.Web` (Blazor Web App, static SSR + isolated
  interactivity).
- Bir paylaşılan persistence projesi: `src/TheBluesland.Data` (EF Core entity + migration),
  yalnızca `TheBluesland.Web` ve `tools/spotify-playlist-fetcher` arasında DB şemasını paylaşmak
  için var — CLAUDE.md'nin `Domain`/`Shared` ayrımının karşılığı değil.
- Ayrı bir `src/Api` yok. Blazor Web App kendi sunucu tarafı render'ını yapıyor; dışarıya açık,
  istemcilerin tükettiği bir HTTP API sözleşmesi yok.
- Ayrı bir `src/Mobile` yok, `src/Ui.Shared` yok. MVP'de mobil hedef yok.

## Gerekçe

1. **MVP'de mobil hedef yok.** CLAUDE.md'nin API-first ayrımının temel gerekçesi, web ve mobil
   istemcilerin aynı sözleşmeyi paylaşmasıdır. TheBluesland'in tek istemcisi kendi sunucu tarafı
   render eden web uygulamasıdır; ikinci bir istemci yok, dolayısıyla paylaşılacak bir sözleşme de
   yok.
2. **API-first ayrımının getirdiği karmaşıklık haklı değil.** Tek kişilik, tek istemcili, SEO-kritik
   bir static-SSR sitede ayrı bir `src/Api` + `src/Domain` + `src/Shared` katmanlaması, gerçek bir
   bağımlılık sınırı çözmüyor — sadece dolaylama ekliyor. `business-technical-specification.md`
   bölüm 12.5'teki ilke ("New projects or layers must only be introduced when they create a real
   dependency boundary") burada da geçerli.
3. **Blazor Server / Interactive Server zaten ADR-0001'de web-mobil ayrımı için elenmişti**
   ("kalıcı sunucu bağlantısına dayanır, mobile çıkış yolu yok" gerekçesiyle). TheBluesland'in
   mobil hedefi olmadığı için bu itiraz burada geçerli değil; TheBluesland zaten sınırlı ölçüde
   Interactive Server kullanıyor (yalnızca filtre bileşeni, spec bölüm 12.3), tüm site değil.
4. **`TheBluesland.Data` bir istisna değil, dar kapsamlı bir gerçek sınır.** Web uygulaması ve
   `tools/spotify-playlist-fetcher` iki bağımsız process (biri Render'da sürekli, diğeri GitHub
   Actions'ta ayda bir) ve aynı `spotify_playlist_cache` şemasını paylaşmaları gerekiyor. Bu,
   CLAUDE.md'nin çok-istemcili UI paylaşımı gerekçesinden tamamen farklı, dar ve somut bir neden.

## Alternatifler

- **CLAUDE.md şablonunu aynen uygulamak (src/Api + src/Domain + src/Infra + src/Shared):**
  Reddedildi. Tek istemcili bir static-SSR site için gereksiz dolaylama üretir; `src/Api`
  katmanının tükettiği hiçbir ikinci istemci yok.
- **CLAUDE.md'yi TheBluesland'e özel olarak güncellemek:** Değerlendirilmedi/ertelendi — CLAUDE.md
  repo genelinde başka projeler için de kullanılıyor olabilir (scaffold script'i genel amaçlı);
  onu TheBluesland'e özel hâle getirmek başka projeleri etkileyebilir. Bunun yerine bu ADR ve
  spec'in başındaki not ile sapma belgelendi.

## Sonuçlar

- `docs/business-technical-specification.md`, `docs/adr/0002-spotify-veri-mimarisi.md` ve bu ADR,
  CLAUDE.md/ADR-0001'in TheBluesland için **geçerli olmadığını** açıkça belirtir.
- Bu sapma, implementasyon başlamadan önce Mehmet'in açık onayını gerektiriyordu (spec bölüm 23,
  madde 9); bu onay 2026-09-03'te, US-005/`TheBluesland.Web` işine devam talimatıyla verildi.
- Eğer TheBluesland ileride gerçek bir mobil istemci kazanırsa (bugün yol haritasında yok, spec
  bölüm 20), bu ADR yeniden değerlendirilmeli ve muhtemelen editoryal/DB mantığının bir kısmı
  CLAUDE.md'nin `Domain`/`Shared` ayrımına benzer bir şekilde yeniden katmanlanmalıdır. Bu, bugünkü
  tek-proje kararının kabul ettiği bir gelecekteki yeniden yapılandırma maliyetidir.
