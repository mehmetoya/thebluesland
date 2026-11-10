# Refactor ve redesign değerlendirmesi + sıralı plan

Tarih: 2026-11-10 · Kapsam: `src/TheBluesland.Web`, `tools/*`, `Styles/app.css` · Durum: öneri,
uygulanmadı. Bulguların hepsi kaynak kodu okunarak çıkarıldı; tarayıcıda çalıştırılarak doğrulanmayan
noktalar "doğrulanmadı" diye işaretlendi. Kısıtlar: `.claude/CLAUDE.md`, ADR-0002/0003/0005 (yeni
katman/proje/paket yok, static SSR, cache'te graceful degradation).

**Kısa hüküm**

- **Refactor:** Büyük bir refactor gerekmiyor. Mimari ADR-0003 ile uyumlu. Cache okuma yolu hata
  fırlatmıyor (`PlaylistCacheLookup`), testler geniş (unit, integration ve E2E). Gereken şeyler
  hedefli: bir gerçek form hatası, `/weekly` öncesi yer açacak bir dosya bölme ve düşük öncelikli
  temizlik.
- **Redesign:** Tam bir redesign gerekmiyor. Görsel yön (spec §10, "late-night record room") ve
  token ölçeği onaylanmış, yayılımı da büyük ölçüde yapılmış. Gereken şeyler hedefli: erişilebilirlik
  düzeltmeleri, token yayılımının bitirilmesi ve `/weekly` için eksik tasarım sistemi parçaları
  (prose, form, dil).

---

## 1. Refactor (kod)

| # | Bulgu ve kanıt | Etki | Efor | Risk | Sınıf |
|---|---|---|---|---|---|
| R1 | **Filtre checkbox'ları iki kez gönderiliyor.** `HomePage.razor:108-148` her boyutu aynı `<form>` içinde iki kez render ediyor: masaüstü `<details>` ve mobil `.filter-mobile-dimension`. Gizli kopya yalnız `display:none` (`app.css:537`), `disabled` değil. HTML spec'e göre `display:none` olan işaretli checkbox da form verisine girer. Test her iki kopyanın `checked` geldiğini doğruluyor (`HomePageFilterIntegrationTests.cs:258`). Sonuç: masaüstünde bir filtrenin işareti kaldırılsa bile gizli kopya onu tekrar gönderir. Ayrıca `?mood=x&mood=x` oluşur ve sayaç ikiye katlanır, çünkü `PlaylistFilterCriteria.ActiveCount` değerleri tekilleştirmeden sayıyor. | Kullanıcı aktif filtreyi kaldıramıyor; "Mood (2)" gibi yanlış sayaç | S | Düşük | **Gerekli (hata)**. Mantık kesin, tarayıcıda doğrulanmadı |
| R2 | **`WebHostFactory.cs` 555 satır.** Middleware, CSP ve 11 endpoint'in tamamı tek bir `Create` metodunda inline duruyor (`:44-521`). Ziyaretçi hash'i iki yerde aynı biçimde hesaplanıyor (`:219-223` ve `:508-512`). `/weekly` buraya sitemap/llms girdileri ve CSP değişikliği ekleyecek. | Okunabilirlik ve değişiklik riski | M | Düşük (integration testler kapsıyor) | İyi olur, `/weekly` öncesi önerilir |
| R3 | **Statik rota listeleri tekrarlanıyor.** `PageViewRoute.cs:12` (`ExactMatchRoutes`) ve `SitemapGenerator.cs:16` (`StaticPaths`) aynı listeyi ayrı ayrı tutuyor. `/weekly` iki yere birden eklenmeli; biri unutulursa listeler ayrışır. | Analytics/sitemap tutarsızlığı | S | Düşük | İyi olur, `/weekly` ile birlikte |
| R4 | **`PlaylistDetailView.razor` 437 satır.** Yaklaşık 200 satırı (`:223-400`) Razor bileşeni içinde duran `public static` URL durum makinesi. Testler bu mantığı bileşen tipi üzerinden çağırıyor (`PlaylistRenderSurfaceTests.cs:332`). Mix mod token eşlemesi iki kez yazılmış: `PlaylistDetailView.razor:223-248` ve `PlaylistMixAnalytics.cs` içindeki `TryNormalizeMode`. Kullanılmayan bir metot da var: `BuildAbsoluteUrl` (`:221`). | Test edilebilirlik ve sapma riski | S-M | Düşük | İyi olur, bekleyebilir |
| R5 | **Bayat yorumlar ve sahipsiz izin.** `WebHostFactory.cs:160-162` artık var olmayan `PlaylistDetailView.EmbedUrl`'e atıf yapıyor; repoda hiç `<iframe>` render edilmiyor, ama CSP hâlâ `frame-src https://open.spotify.com` veriyor (`:188`, test `WebHostIntegrationTests.cs:189`). `package.json` açıklaması da stylesheet'in henüz bağlanmadığını söylüyor, oysa `App.razor` bağlıyor. | Yanıltıcı doküman ve gereksiz CSP izni | S | Düşük | İyi olur (CSP kısmı Mehmet kararı) |
| R6 | **Fetcher CLI'da yanlış yazılmış alt komut sessizce geçiyor.** `tools/spotify-playlist-fetcher/Program.cs:79-81`'de bilinmeyen bir `args[0]` içerik dizini kabul ediliyor. Böyle bir dizin bulunamayınca `PlaylistFrontMatterReader.cs:46-49` boş liste döndürüyor ve araç "Nothing to sync" deyip `0` ile çıkıyor. | Manuel kullanımda hata görünmez kalıyor (workflow'lar doğru komutu kullanıyor) | S | Düşük | İyi olur |
| R7 | Sayfalarda küçük tekrar: `BuildAbsoluteUrl` üç sayfada kopya (`HomePage.razor:304`, `CollectionPage.razor:70`, `PlaylistDetailPage.razor:118`). Uzun tarihçe yorumları da okumayı ağırlaştırıyor; örneğin `HomePage.razor:56-96` 40 satırlık bir karar günlüğü. | Okunabilirlik | S | Yok | İyi olur, fırsat buldukça |

**Bilinçli olarak önerilmeyenler**

- Front matter okuma hem Web'de (`FrontMatterSplitter`) hem fetcher'da yapılıyor. Bu, araç Web'e
  referans vermesin diye kabul edilmiş bir sınır. Paylaşılan bir proje açmak CLAUDE.md'ye aykırı.
- `app.css` (1114 satır) dosyasını bölmek veya Tailwind utility class'larına geçmek gerekmiyor.
  Desen tutarlı (BEM benzeri class'lar ve `@theme` token'ları).
- Repository sarmalayıcısı, MediatR veya yeni katman eklemeye gerek yok.

## 2. Redesign (UI/UX)

**Güçlü taraflar (korunmalı)**

- Token paleti iki temada da AA kontrastını geçiyor. Hesap `app.css:11-31` ve `:86-99` değerlerinden
  yapıldı. En düşük çift açık temada muted metin / elevated zemin: 5.02:1. Koyu temada muted / bg:
  7.02:1. Accent / bg: 7.61:1 (koyu), 5.65:1 (açık).
- Hareketler `prefers-reduced-motion` ile korunuyor (`app.css:251`, `:433`, `:724`, `:1097`).
- Başlıklar `clamp` ile akışkan ölçekleniyor (`app.css:63-64`).
- JS olmadan çalışan filtre ve sayfalama var.
- Breadcrumb'da `aria-current` kullanılıyor, kapak görsellerinde alt metin doğru (kart görseli
  `alt=""` ile dekoratif, detay görseli "X cover art").

**Hedefli sorunlar**

| # | Bulgu ve kanıt | WCAG / etki | Sınıf |
|---|---|---|---|
| D1 | **Skip link yok.** `App.razor` `<body>` doğrudan `<SiteHeader />` ile başlıyor. | 2.4.1 | Gerekli |
| D2 | **Mobil "Filters" tetikleyicisinde odak görünmüyor.** Klavye odağı `opacity:0` olan checkbox'ta kalıyor (`app.css:521-525`). Odak halkası bu görünmez kutuya çiziliyor; `.filter-mobile-toggle:focus-visible ~ .filter-mobile-trigger` kuralı yok. | 2.4.7 | Gerekli |
| D3 | **Odak stili tutarsız.** Ortak odak kuralı yalnız `a`, `button` ve `input`'u kapsıyor (`app.css:153-157`). Filtre ve paylaşım `<summary>`'leri tarayıcı varsayılanına kalıyor. Görsel etkisi doğrulanmadı. | 2.4.7 tutarlılık | Gerekli |
| D4 | **Tema düğmesinin ARIA durumu yükte senkronlanmıyor.** `theme.js:10-12` kayıtlı açık temayı yüklüyor, ama butondaki `aria-pressed="false"` ve "Switch to light theme" etiketi (`SiteHeader.razor`) ilk tıklamaya kadar yanlış kalıyor. `localStorage` erişimi `try/catch` içinde değil; depolama engelliyse script tamamen duruyor. | 4.1.2 | Gerekli |
| D5 | **Başlık hiyerarşisi bozuk.** Mobil panelde `<h3>`'ler "Playlists" `<h2>`'sinden önce geliyor (`HomePage.razor:134`). Kart başlıkları sabit `<h2>` (`PlaylistCard.razor`), bu yüzden `<h2>Playlists</h2>` (`HomePage.razor:156`) ve "Related playlists" altında kardeş düzeyde kalıyorlar. | 1.3.1 | Gerekli |
| D6 | **Aktif sayfa işaretlenmiyor.** Primary nav'da `aria-current` yok, aktif sayfanın görsel durumu da yok (`SiteHeader.razor`, `app.css:222-230`). `/weekly` dördüncü öğe olunca bu daha belirgin hale gelecek. | Gezinme ve yönelim | Gerekli (`/weekly` ile) |
| D7 | **Token yayılımı yarım kaldı.** `app.css`'in 101. satırından sonra token yerine kullanılmış yaklaşık 146 ham rem/px değeri var. Kapsayıcı genişliği `72rem` üç kez tekrarlanıyor (`:175`, `:286`, `:299`). Metin ölçüsü de dağınık: `38rem` (`.about`, `:1054`), `40rem` (`:326`), `42rem` (`:331`, `.curator-note` `:837`). | Tutarlılık | İyi olur. Kapsayıcı ve ölçü kısmı gerekli (`/weekly`) |
| D8 | **Bileşen semantiği karışık.** Arama butonu filtre class'ını kullanıyor: `class="filter-apply"` (`HomePage.razor:52`). Genel bir buton veya alan primitive'i yok. Breadcrumb iki farklı biçimde yazılmış: `PlaylistDetailView.razor:14-18` ayıraç için `aria-hidden` kullanıyor, `CollectionPage.razor:12` ham " / " metni basıyor. Detay breadcrumb'ı Collections'ı da atlıyor. | Tutarlılık | Gerekli (`/weekly` formu bunlara dayanacak) |
| D9 | **"Refresh mix" akışı karmaşık.** Beş query parametreli bir durum makinesi (`PlaylistDetailView.razor:83-150`). Gerçekte kullanılıp kullanılmadığı doğrulanmadı; `/dashboard` mix olaylarını zaten sayıyor. | Bilişsel yük | Karar: veriye bakıp sadeleştir ya da bırak |

Responsive yapı tek kırılma noktasına (`40rem`, `app.css:565`) ve `auto-fill`/`auto-fit` gridlere
dayanıyor. Geçmişteki iPhone taşma hataları düzeltilmiş. Gerçek cihazda yeniden kontrol edilmedi
(**doğrulanmadı**).

**`/weekly` arşivi için tasarım sisteminden gerekenler**

Spec: `docs/specs/weekly-newsletter.md`, modül `weekly-archive`.

1. **`.prose` uzun metin stili.** h2/h3, ul/ol, blockquote, satır içi link ve kaynak atfını kapsar.
   `.about/.privacy/.terms` ile `.curator-note` bunun üzerine birleştirilir. Ölçü token'ı
   (`--measure`) ve kapsayıcı token'ı (`--container`) eklenir.
2. **Buton ve alan primitive'leri.** Primary/secondary buton, label'lı input, hata ve başarı
   durumları (`role="status"` / `aria-live`), görünür odak. Abone formu bunları kullanır.
3. **Dil desteği.** `App.razor:3` `lang="en"` sabit. TR sayfaları için `<html lang>` rotaya göre
   ayarlanmalı, en azından `<article lang="tr">` kullanılmalı. `PageMetadata` şu an `hreflang` ve
   `og:locale` desteklemiyor (`PageMetadata.razor:21-31`). TR/EN geçiş bileşeni gerekiyor. Tarihler
   kültüre göre biçimlenmeli.
4. **Arşiv liste öğesi.** Sayı no, tarih, dil rozeti ve özet içerir; kart stilinden türetilir. Sayıya
   özel og görseli mevcut `SocialCardGenerator` ile üretilir.
5. **CSP ile abone formu çelişiyor.** `form-action 'self'`, `script-src 'self'` ve `frame-src`
   (`WebHostFactory.cs:184-192`) sağlayıcının JS widget'ını da iframe'ini de, harici adrese POST'u
   da engelliyor. Yaklaşım seçilmeli (aşağıda karar K1).
6. Nav öğesi ve aktif durum (D6). `PageViewRoute` ve `SitemapGenerator` rotaları (R3).

## 3. Sıralı plan

Her adım ayrı bir PR olarak çıkabilir. Her adımda önce ilgili testler, teslimden önce bir kez tam
`dotnet build` + `dotnet test`, CSS değiştiyse `npm run build:css` çalıştırılır.

### Faz A — `/weekly` öncesi (gerekli)

1. **A1 · Filtre çift gönderim düzeltmesi (R1).** Mobil paneli kendi `<form>`'una ayır; her form tek
   bir kopya göndersin (JS gerekmez). Ek savunma olarak `PlaylistFilterCriteria` değerlerini
   `Distinct` ile tekilleştir.
   *Kabul:* Aktif bir filtrenin işareti masaüstünde kaldırılınca filtre gerçekten kalkar. Query'de
   tekrar eden değer olmaz. Sayaç doğru kalır.
   *Doğrulama:* Her formda bir `name/value` kopyası olduğunu ve `?mood=x&mood=x` için "Mood (1)"
   gösterildiğini kontrol eden regresyon testleri; masaüstü ve mobil genişlikte elle deneme.
2. **A2 · Erişilebilirlik paketi (D1-D5).** Skip link; `summary` ve mobil tetikleyici için
   `:focus-visible`; `theme.js`'te yüklemede ARIA senkronu ve `try/catch`; `PlaylistCard`'a başlık
   düzeyi parametresi; mobil panel başlık düzeylerinin düzeltilmesi.
   *Kabul:* Klavyeyle her etkileşimli öğede görünür odak var. Başlık sırası atlamıyor.
   *Doğrulama:* Markup integration testleri, klavye turu ve axe/Lighthouse kontrolü (araç repoda yok,
   elle yapılır).
3. **A3 · Tasarım sistemi primitive'leri (D7 kapsayıcı/ölçü, D8, `/weekly` 1-2).** `--container` ve
   `--measure` token'ları, `.prose`, `.button` ve `.field`, ortak `Breadcrumb` bileşeni.
   About/Privacy/Terms ve curator note `.prose`'a taşınır.
   *Kabul:* Mevcut sayfalarda görsel regresyon olmaz. Dokunulan selector'larda yeni ham boyut kalmaz.
   *Doğrulama:* Öncesi/sonrası ekran görüntüleri (dar ve geniş, iki tema); render testleri yeşil.
4. **A4 · `WebHostFactory` bölme (R2, iyi olur ama önerilir).** Endpoint'ler aynı projede
   `Map…Endpoints` extension'larına ayrılır (diagnostics, discovery, social cards, outbound).
   Ziyaretçi hash'i tek yardımcıya taşınır. Davranış değişmez.
   *Kabul:* Mevcut integration testleri değiştirilmeden geçer; `WebHostFactory` yaklaşık 200
   satırın altına iner.

### Faz B — `/weekly` ile birlikte

5. **B1 · Dil altyapısı.** `lang`, `hreflang` ve `og:locale` desteği `PageMetadata` ve `App.razor`'a
   eklenir, TR/EN geçişi yapılır. *Kabul:* TR sayfasında `lang="tr"` var; iki dil birbirine
   `alternate` ile bağlı. Bunu doğrulayan testler eklenir.
6. **B2 · Arşiv ve sayı sayfaları, nav aktif durumu (D6), rota listeleri (R3), K1'e göre CSP
   değişikliği.** *Kabul:* Sitemap, llms ve page-view `/weekly`'i kapsıyor; CSP yalnız seçilen
   sağlayıcı origin'ine açılıyor ve bunu bir test sabitliyor.

### Faz C — bekleyebilir

7. **C1:** `PlaylistDetailView` içindeki mix URL mantığını `Content/` altına taşı; mod token
   eşlemesini tek yerde topla; ölü `BuildAbsoluteUrl`'i sil (R4). Testler bileşen yerine yeni tipi
   çağırır.
8. **C2:** Bayat yorumları ve `package.json` açıklamasını düzelt (R5); karar K3'e göre `frame-src`'i
   kaldır.
9. **C3:** Fetcher'da bilinmeyen alt komut için `Usage` mesajı göster ve `1` döndür (R6, regresyon
   testiyle).
10. **C4:** Kalan ham rem/px değerlerini token'a çevir (D7); D9 için veriye dayalı karar ver.

## 4. Mehmet'in kararı gereken noktalar

- **K1 · Abone formu yaklaşımı (`/weekly` öncesi).**
  - (a) Sağlayıcının endpoint'ine giden düz HTML `<form>` ve `form-action`'a o origin'in eklenmesi.
    JS yok, SSR ile uyumlu. Önerilen bu.
  - (b) Sağlayıcının kendi barındırdığı kayıt sayfasına link. CSP değişmez, UX zayıflar.
  - (c) Kendi POST proxy'miz. Önerilmiyor: sağlayıcı anahtarı production web'e girer ve kişisel veri
    bizim yüzeyimizden geçer. Bu, spec'teki "web salt okunur" ilkesine aykırı.

  Sağlayıcı seçimi de bu karara bağlı.
- **K2 · URL ve dil şeması.** Örnek: `/weekly/tr/2026-W46` ve `/weekly/en/2026-W46` mi, yoksa
  `/weekly/2026-W46` altında iki dil mi? `<html lang>` rotaya göre mi ayarlanacak, yoksa
  `<article lang>` yeterli mi?
- **K3 · Kullanılmayan `frame-src https://open.spotify.com` izni.** Kaldırılsın mı, yoksa ileride
  embed gelme ihtimaline karşı kalsın mı?
- **K4 · "Refresh mix" (D9).** Dashboard verisine göre sadeleştirilsin mi, olduğu gibi kalsın mı?
