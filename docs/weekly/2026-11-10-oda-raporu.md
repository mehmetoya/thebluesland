# TheBluesland Weekly · Tasarımcılar ve Editör Odası raporu (2026-11-10)

İlk gerçek taslak (W47, Gemini, elle tetiklenen deneme koşusu) ve Weekly'nin web/e-posta yüzü beş
bağımsız salt-okunur incelemeyle denetlendi: Design Director + UI sistemleri, UX mimarı + etkileşim,
erişilebilirlik + UX yazarlığı, yayın yönetmeni + TR/EN metin editörü, doğrulama editörü + küratör.
Bulguların tamamı üç PR'a bölündü:

- **PR-1 içerik hattı** (`feature/weekly-content-quality`): ED-1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14; FC-1, 2, 3, 8, 9.
- **PR-2 site ve e-posta tasarımı** (`feature/weekly-design-pass`): DD-1…13, UX-1…11, A11Y-1…8, UXW-1…6, ED-13.
- **PR-3 kaynaklar ve sıralama** (`feature/weekly-sources-ranking`): ED-11, FC küratörlük notları.

## Genel hüküm

W47 taslağı bu haliyle yayınlanamazdı. Sorunlar tasarımdan büyüktü: kaynakla çelişen olgular,
kaynakta olmayan "Neden önemli" yorumları, TR'de düşen bağlam ve bunları yakalamayan bir doğrulayıcı.
Kök nedenler: modelin yalnız 300 karakterlik özet görmesi, prompt'ta gönderim tarihi ve başlık/zorunlu
öğe kuralı olmaması, doğrulayıcının yalnız fazlayı (eksiği değil) ve tüm kartı (haber başına değil)
kontrol etmesi, feed setinin tamamen ABD/İngiltere pop-indie medyası olması.

## Olgu doğrulama özeti (W47)

| Haber | Sonuç |
|---|---|
| Richard Lloyd | Ölüm, 75 yaş, 7 Kasım doğrulandı (Stereogum). "Birlikte kurdu" kaynakla çelişiyor olabilir (Stereogum: Lloyd sonradan ikinci gitarist) — güvenli ifade: "Television'ın gitaristi". |
| YouTube / Billboard | Eşik 2.500 → 1.500, ilk etki 16 Ocak 2027 doğrulandı. EN "a year after" yanlış (~10 ay); "on Monday" kaynaksız. |
| Oasis | Katılmama doğrulandı; tören 14 Kasım (gönderimden önce). "Tavır meselesi" yorumu gerçek gerekçeyle (Noel'in Londra konseri, Liam'ın "Christmas" sözü) çelişiyor. |
| GTA VI festivali | 21 Kasım, Miami doğrulandı. EN'de "VI" eksik; TR'de yer/tarih yok. Markaya zayıf uyum. |
| The Strokes | SNL (7 Kasım, Druski), Valensi yerine Steve Schiltz, Spitting Image klibi doğrulandı. TR'de SNL adı yok. |
| Açılamayanlar | Pitchfork, Music Business Worldwide (403), Rolling Stone, bazı NME linkleri. |

## Çelişkiler ve kararlar

- Kartta olmayan arka plan bilgisi (albüm adı, yıl): **yasak sürüyor** (ADR-0006 madde 3); model
  bunun yerine 800 karakterlik özet görür.
- Lloyd'un kurucu olup olmadığı: çözülene kadar güvenli ifade; ilişki iddiaları editör kontrol listesinde.
- "Tarih ile hafta numarası tutmuyor" bulgusu yerel önizleme kopyasındaki elle değiştirilmiş tarihten
  kaynaklandı; düşüldü.
- İlk ekran görüntüleri bayat derlenmiş CSS ile alınmıştı (DD-1); önizlemeden önce `npm run build:css`.

## Korunan kararlar

Lacivert/turuncu palet ve turuncunun tek aksiyon rengi olması, Fredoka yalnız başlıklarda, 40rem
okuma genişliği, token tabanlı renkler, JS'siz düz HTML form ve dar CSP, e-postada yalnız https
linkleri, "Neden önemli" yapısı, ünlemsiz ton, AI şeffaflık notu, haber başına kaynak gösterimi.

## Doğrulanamayanlar

Gerçek telefon görünümü (headless Chrome 500px altında kırpıyor), gerçek ekran okuyucu, Kit'in
abonelik sonrası sayfası/onay e-postası/şablon sarmalı, ana sayfa render'ı (yerelde veritabanı yok),
erişilemeyen kaynak sitelerdeki iddialar.
