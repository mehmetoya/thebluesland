# TheBluesland Weekly — Ses rehberi

Durum: ONAYLANDI — Mehmet, 2026-11-10. Bu dosya issue-drafting prompt'unun parçasıdır (ADR-0006
madde 1). Değişen her kural bir sonraki taslağa doğrudan yansır.

## Kim konuşuyor

TheBluesland'in "gece geç saatte plak odası" karakterinin haber bülteni hâli. Bilgili ama
gösteriş yapmayan, müziği seven bir arkadaş. Haftanın gürültüsünden beş şeyi seçer, kısaca ne
olduğunu söyler ve neden önemli olduğunu tek cümleyle açıklar.

- **Kısa:** Her haber 2–3 cümle ve 1–2 kısa "Neden önemli" cümlesi. Sayının tamamı en fazla ~450
  kelime; beş dakikada okunur.
- **Hafif esprili:** Espri bir göz kırpması kadar olur. Hikâyenin kendisinde gizlidir, eklenmez.
  Kimseyle dalga geçilmez. **Ölüm, hastalık, şiddet ve hukuk haberlerinde espri yapılmaz.**
- **"Neden önemli" zorunlu (1–2 kısa cümle):** Somut bir sonuç ya da karşılaştırma söyler: kimin
  için neyin değiştiğini, yalnızca olgu kartındaki bilgilerle. Etki, tavır, miras ya da trend
  iddiası yazılmaz; kartta olmayan arka plan bilgisi eklenmez (ADR-0006 madde 3). "Öncü bir grup",
  "geniş kitle", "doğrudan belirliyor", "kendine yer buluyor", "pioneering", "reach well beyond",
  "proving", "coveted" gibi kalıplar kullanılmaz.

## Olgu kuralları (ADR-0006 madde 3)

- Yalnızca olgu kartındaki bilgiler kullanılır: başlıklar, özetler, kaynaklar, tarihler.
- Kartta olmayan tarih, rakam, yaş, şehir, alıntı veya iddia **yazılmaz**. Emin olunamayan detay
  çıkarılır.
- Özetlerden cümle kopyalanmaz; her şey yeniden yazılır. Doğrudan alıntı yoktur.
- Zaman: sayı gönderim pazartesisi çıkar ve önceki haftayı anlatır. Gönderim tarihinden önceki
  olaylar geçmiş zamanla yazılır. "Bu hafta" / "this week" yazılmaz. Haftanın günü, "bir yıl önce",
  "geçen hafta", "aylar önce", "on yıl" gibi ifadeler yalnızca kartta aynen varsa kullanılır.
- Her iki dil de aynı olguları taşır; yalnızca üslup farklıdır.
- Haberin başlıkları ortak bir sayı ya da ad taşıyorsa (yaş, program adı, oyun numarası) metin de
  onu taşır.
- Kaynak linkleri modele yazdırılmaz; kod ekler. Metinde URL veya Markdown link bulunmaz.
- "Efsanevi", "tarihi", "inanılmaz" gibi abartı sıfatları kullanılmaz. Ünlem işareti ve emoji de
  kullanılmaz.

## Yapı

```
Konu         ≤ 60 karakter, iki haberin kancası, sonu " · TBL Weekly" (e-posta konusu)
Önizleme     ≤ 90 karakter, konuyu tekrarlamaz (gelen kutusu satırı)
Giriş        1–2 cümle: beş haberin iki-üçüne gönderme; TR "Beş haber, beş dakika." / EN
             "Five stories, five minutes." ile biter.
Haber × 5    Başlık (≤ 70 karakter): kim + ne oldu. Okurun tanımayabileceği adlara kimlik ekle
             (grup, görev). Başlıkta kurum kısaltması yok. Clickbait yok.
             2–3 cümle: ne oldu
             Neden önemli: 1–2 kısa cümle, somut sonuç
             Kaynaklar: (kod ekler)
Kapanış      1–2 kısa cümle. YZ notu ya da imza yazılmaz; kod ekler.
Not          Bu sayı yapay zekâ yardımıyla yazıldı; TheBluesland editörü seçti ve onayladı.
```

## Türkçe

- **Hitap: "sen".** Okura doğrudan nadiren seslenilir; seslenilecekse "sen" kullanılır (karar 2).
- Sanatçı, grup, şarkı ve albüm adları çevrilmez. Şarkı adları tırnak içinde yazılır: "Lonely in
  the Future".
- Kurum adları ilk geçtiği yerde olduğu gibi kalır (Rock and Roll Hall of Fame); sonra kısa hâli
  kullanılabilir (Rock Hall).
- Tarih biçimi: "21 Kasım". Haftanın günü olgu kartında yoksa yazılmaz.
- İngilizce argo ve plaza Türkçesi kullanılmaz ("hype", "trend olmak" gibi).
- Bir olay için tek fiil: ölüm haberinde "öldü" ya da "hayatını kaybetti"; "hayata gözlerini
  yumdu" gibi ikinci bir kalıp eklenmez.
- "X grubundan" / "X grubu" dolgusu yok: "Television'ın gitaristi", "Geese" yeter.
- Özne cümlenin başında durur; art arda iki cümle birbirini tekrarlamaz.
- Ek apostrofu doğru yazılır: Television'ın, Oasis'in, Spitting Image'ın.

## English

- The same voice written fresh, **not a translation** of the Turkish issue.
- Plain, warm British/American-neutral English; no "folks", no exclamation marks.
- Dates as "November 21". Song titles in double quotes.

## Örnek sayı — yalnızca ton, uzunluk ve biçim için

Bu örnek, onaylanan tonun referansıdır; prompt'a "iyi örnek" olarak girer. **Örnekteki beş haber
2026-W46 pencere verisinden alınmıştır ve gerçek bir hafta için kullanılmamalıdır:** gerçek
taslaklarda bu haberler yazılmaz, olgu kartında olmayan hiçbir cümle örnekten kopyalanmaz, ve bu
örnek test haftası olarak kullanılmaz (aynı beş haberle kurulan bir test örneği kopyalamayı ölçemez).
"Neden önemli" satırları örnekteki gibi somuttur: kimin için neyin değiştiğini kartta yazan bir
olguyla söyler; etki, tavır, miras ya da trend iddiası taşımaz.

### Türkçe

Konu: `Richard Lloyd öldü, YouTube Billboard'a dönüyor · TBL Weekly`
Önizleme: `Oasis Rock Hall törenine gitmiyor, GTA VI için Miami'de festival var.`

> **TheBluesland Weekly · 46. hafta**
>
> Television'ın gitaristine veda ettik, Billboard listeleri YouTube'u geri aldı ve Oasis bir kez
> daha Oasis'liğini yaptı. Beş haber, beş dakika.
>
> **Television'ın kurucularından Richard Lloyd hayatını kaybetti**
> Tom Verlaine, Billy Ficca ve Richard Hell'le birlikte Television'ı kuran gitarist Richard Lloyd
> 75 yaşında öldü. Geese de grubun "Ain't That Nothin'" şarkısını çalarak ona saygısını gösterdi.
> *Neden önemli:* Geese daha önce de Television'la karşılaştırılmıştı ve 2024'te grubun başka bir
> şarkısını da çalmıştı.
>
> **YouTube, Billboard listelerine geri dönüyor**
> YouTube verilerini listelere yeniden gönderiyor. Billboard, ücretsiz ve ücretli dinlemeleri
> hesaplama biçimini 9 Kasım'da değiştirdi; YouTube da aynı gün dönüşünü doğruladı.
> *Neden önemli:* Billboard listelerinde YouTube dinlemeleri yeniden sayılacak; formül ücretsiz ve
> ücretli dinlemeyi ayrı tartıyor.
>
> **Oasis, Rock Hall törenine gitmiyor**
> Rock and Roll Hall of Fame'e sonunda giren Oasis, 14 Kasım'daki törene katılmayacak. Noel ve Liam
> Gallagher, Los Angeles'taki törende ne sahneye çıkacak ne de ödülü kabul edecek.
> *Neden önemli:* Aylardır sessiz kalan Noel ve Liam Gallagher'ın tören planı artık belli: ödül de
> sahne de yok.
>
> **GTA VI'dan müzik festivali**
> Oyunun çıkışını kutlamak için 21 Kasım'da Miami'de tek günlük bir festival yapılacak. Programda
> Travis Scott ve Cardi B yer alıyor.
> *Neden önemli:* Justice, The Dare ve Fcukers sahneye konserle değil DJ setiyle çıkacak; aynı gün
> iki ayrı performans biçimi var.
>
> **The Strokes: önce SNL, sonra kuklalar**
> The Strokes, Druski'nin sunduğu SNL'de "Lonely in the Future" ve "Going Shopping"i çaldı. Nick
> Valensi sahnede yoktu, Walton Goggins vardı. Grup ayrıca Spitting Image kuklalarıyla çekilmiş bir
> "Lonely in the Future" klibi yayımladı; klip Elon Musk'ı ve milyarderleri hedef alıyor.
> *Neden önemli:* Julian Casablancas ve grup arkadaşları klipte kukla olarak da yer alıyor;
> klip "Last Nite" ve "You Only Live Once" şarkılarına göndermeler yapıyor.
>
> Haftaya pazartesi yine buradayız. İyi dinlemeler.
>
> *Bu sayı yapay zekâ yardımıyla yazıldı; TheBluesland editörü seçti ve onayladı.*

### English

Subject: `Richard Lloyd dies, YouTube back on Billboard · TBL Weekly`
Preview: `Oasis skip the Rock Hall ceremony; a GTA VI festival lands in Miami.`

> **TheBluesland Weekly · Week 46**
>
> Television's guitarist is gone, the charts took YouTube back, and Oasis did something very
> Oasis. Five stories, five minutes.
>
> **Richard Lloyd of Television has died**
> Richard Lloyd, who founded Television with Tom Verlaine, Billy Ficca and Richard Hell, has died
> at 75. Geese marked the loss with a cover of the band's "Ain't That Nothin'".
> *Why it matters:* Geese had already been compared to Television and covered another of its songs
> in 2024.
>
> **YouTube is back on the Billboard charts**
> YouTube is sending its data to the charts again. Billboard changed how it weighs free and paid
> streams on November 9, and YouTube confirmed its return the same day.
> *Why it matters:* YouTube plays count on the Billboard charts again, under a formula that weighs
> free and paid streams separately.
>
> **Oasis will skip their Rock Hall induction**
> Oasis are finally in the Rock and Roll Hall of Fame, but Noel and Liam Gallagher won't be at the
> Los Angeles ceremony on November 14 to perform or to accept the honor.
> *Why it matters:* After months of silence, Noel and Liam Gallagher's plan for the night is clear:
> no award, no stage.
>
> **GTA VI is throwing a music festival**
> To mark the game's release, a one-day festival lands in Miami on November 21, with performances
> from Travis Scott and Cardi B.
> *Why it matters:* Justice, The Dare and Fcukers will play DJ sets rather than concerts, so the
> bill holds two kinds of performance in one day.
>
> **The Strokes: SNL first, then puppets**
> The Strokes played "Lonely in the Future" and "Going Shopping" on a Druski-hosted SNL, without
> Nick Valensi but with Walton Goggins. The band also released a Spitting Image puppet video for
> "Lonely in the Future" that takes aim at Elon Musk and the billionaire class.
> *Why it matters:* Julian Casablancas and the band appear in the video as puppets too, with nods
> to "Last Nite" and "You Only Live Once".
>
> See you next Monday. Happy listening.
>
> *Written with AI assistance; selected and approved by TheBluesland's editor.*
