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
  iddiası yazılmaz; kartta olmayan arka plan bilgisi eklenmez (ADR-0006 madde 3). Başlıkta ve
  gövdede olmayan **yeni bir olgu** ekler; ikisini yeniden anlatmak yanlıştır. Kart böyle bir olgu
  vermiyorsa somut sonraki adım yazılır (tarih, yer, kimin etkilendiği). "Öncü bir grup", "geniş
  kitle", "doğrudan belirliyor", "kendine yer buluyor", "pek çok müzisyen", "birçok müzisyen",
  "yeniden yer alacak", "pioneering", "reach well beyond", "proving", "coveted", "many musicians",
  "a favorite of" gibi kalıplar kullanılmaz. Her yedek haber de yazılır; eksik yedek hatadır.

## Olgu kuralları (ADR-0006 madde 3)

- Yalnızca olgu kartındaki bilgiler kullanılır: başlıklar, özetler, kaynaklar, tarihler.
- Kartta olmayan tarih, rakam, yaş, şehir, alıntı veya iddia **yazılmaz**. Emin olunamayan detay
  çıkarılır.
- Özetlerden cümle kopyalanmaz; her şey yeniden yazılır. Doğrudan alıntı yoktur. Bu rehberin örnek
  sayısı da kopyalanmaz (aşağıya bak).
- Her haberde, kart verdiği ölçüde: **kim** (kimlikle: grup, görev, "hayatta kalan iki Beatles"),
  **ne oldu**, **ne zaman** (tarih), **nerede** (şehir) ve başlıkların bir olay, şarkı (çift
  tırnak içinde) ya da kurum için kullandığı her özel ad (kurum ilk geçtiği yerde tam adıyla).
  İki dil de aynı listeyi taşır.
- Görevleri kart nasıl diyorsa öyle yaz: "kurucu", "mucit", "ilk", "tek" yalnızca bir özet
  söylüyorsa; "co-inventor" "mucitlerinden" olarak kalır.
- Karttaki iki makale bir tarihte ya da sayıda çelişiyorsa o ayrıntı yazılmaz.
- Zaman: sayı gönderim pazartesisi çıkar ve önceki haftayı anlatır. Gönderim tarihinden önceki
  olaylar geçmiş zamanla yazılır. Pencere ile gönderim tarihi arasında tarihlenen bir olay, okur
  sayıyı açtığında bitmiş olur: duyuru geçmiş zamanla verilir ("katılmayacağını açıkladı" / "said
  they would skip"), nasıl sonuçlandığı asla yazılmaz; gönderim tarihinden önceki bir olay için
  gelecek zaman ("-ecek/-acak", "will") kullanılmaz. Henüz başlamamış bir şey geçmiş sayılmaz:
  "16 Ocak'tan itibaren sayılacak" / "will count from January 16", "başladı" / "started" değil.
  "Bu hafta" / "this week" yazılmaz. Haftanın günü, "bir yıl önce",
  "geçen hafta", "aylar önce", "on yıl" gibi ifadeler yalnızca kartta aynen varsa kullanılır.
- Her iki dil de aynı olguları taşır; yalnızca üslup farklıdır.
- Haberin başlıkları ortak bir sayı ya da ad taşıyorsa (yaş, program adı, oyun numarası) metin de
  onu taşır.
- Kaynak linkleri modele yazdırılmaz; kod ekler. Metinde URL veya Markdown link bulunmaz.
- "Efsanevi", "tarihi", "inanılmaz" gibi abartı sıfatları kullanılmaz. Ünlem işareti ve emoji de
  kullanılmaz.

## Yapı

```
Konu         ≤ 60 karakter, iki haberin kancası, sonu " · TBL Weekly" (e-posta konusu).
             Sığmazsa ikinci kanca atılır; kısaltma yapılmaz.
Önizleme     ≤ 90 karakter, konuyu tekrarlamaz (gelen kutusu satırı)
Giriş        1–2 cümle: beş haberin iki-üçüne gönderme; TR "Beş haber, beş dakika." / EN
             "Five stories, five minutes." ile biter.
Haber × 5    Başlık (≤ 70 karakter): kim + ne oldu. Okurun tanımayabileceği adlara kimlik ekle
             (grup, görev). Başlıkta kurum kısaltması yok. Clickbait yok.
             2–3 cümle: ne oldu
             Neden önemli: 1–2 kısa cümle, somut sonuç
             Kaynaklar: (kod ekler)
Kapanış      1–2 kısa cümle. İmza ve YZ notu yazılmaz; modelin kapanışında YZ'den söz eden cümle silinir.
Kredi        Kod ekler (model yazmaz): TR "Bu sayı TheBluesland ve Gemini'nin elinden." / EN "This issue
             comes from TheBluesland and Gemini." Kapanışın altında tek satır (ADR-0006 madde 4).
```

## Türkçe

- **Hitap: "sen".** Okura doğrudan nadiren seslenilir; seslenilecekse "sen" kullanılır (karar 2).
- Sanatçı, grup, şarkı ve albüm adları çevrilmez. Şarkı adları tırnak içinde yazılır: "Lonely in
  the Future".
- Kurum adları ilk geçtiği yerde olduğu gibi kalır (Rock and Roll Hall of Fame); sonra kısa hâli
  kullanılabilir (Rock Hall). Başlıkta "Rock Hall" kısa hâli geçebilir; gövde önce tam adı yazar.
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

## Örnek sayı (kurgusal örnek) — yalnızca ton, uzunluk ve biçim için

Aşağıdaki sayı **kurgusal bir örnektir**: sanatçılar, gruplar, kurumlar, şehirler ve olaylar
uydurmadır ve hiçbir gerçek haftayla ilgisi yoktur. Prompt'a "iyi örnek" olarak girer ve hedef
üslubu gösterir: gönderim tarihinden önceki olaylar geçmiş zamanla, henüz başlamamış olanlar
gelecek zamanla yazılır; "Neden önemli" satırları başlıkta ve gövdede olmayan yeni bir olgu ekler;
başlıkta kimlik (grup, görev) verilir; gövdede kurumun tam adı geçer.

**Örnekteki hiçbir ifade, cümle parçası ya da olgu taslağa taşınmaz.** Gerçek taslakta yalnızca
olgu kartındaki haberler, o kartın kendi bilgileriyle yazılır.

### Türkçe (kurgusal örnek)

Konu: `Tessaly Brand öldü, Maren Sloane solo çıkıyor · TBL Weekly`
Önizleme: `Port Alder'daki Saltwater Ballroom 5 Aralık'ta yeniden açılıyor.`

> **TheBluesland Weekly · 46. hafta**
>
> Port Alder bir basçısına veda etti, bir grup ödül gecesine gelmeyeceğini açıkladı ve bir salon
> kapılarını yeniden açıyor. Beş haber, beş dakika.
>
> **Orchard Kids'in basçısı Tessaly Brand 68 yaşında öldü**
> Grup, basçısı Tessaly Brand'in 68 yaşında öldüğünü bir açıklamayla duyurdu. Ione Marchetti ile
> Dale Kerrigan, Port Alder'daki anma konserinde "Low Tide Lullaby"yi çaldı.
> *Neden önemli:* Brand, grubun altı albümünün hepsinde yer almıştı.
>
> **Velvet Harbour, Calder Vale ödül gecesine katılmayacağını açıkladı**
> Albüm dalında aday gösterilen Velvet Harbour, 12 Kasım'daki Calder Vale Music Prize törenine
> katılmayacağını açıkladı. Aynı dalın kısa listesinde beş isim vardı.
> *Neden önemli:* Plak şirketi Dunmore Hall Records, grubun o akşam Hallvik'te konser vermek için
> programlandığını söyledi.
>
> **Calder Vale listesi, konser plaklarını 16 Ocak'tan sayacak**
> Calder Vale albüm listesi, hayranların konserin kendisinde aldığı plakaları 16 Ocak'tan itibaren
> sayacak. Sonradan internetten verilen siparişler sayıma girmeyecek.
> *Neden önemli:* Yeni kuralın uygulandığı ilk liste 23 Ocak'ta yayımlanacak.
>
> **Brindle Court solisti Maren Sloane'ın ilk solo albümü "Cold Frames"**
> Maren Sloane, ilk solo albümünün 20 Şubat'ta çıkacağını duyurdu. "Mothlight" adlı tekli şimdi
> dinlenebiliyor; albüm Calder Vale'deki Ash Row Studios'ta kaydedildi.
> *Neden önemli:* Sloane albümü Brindle Court'un turneleri arasındaki bir molada yazdı; grup ara
> verdiğini açıklamadı.
>
> **Port Alder'daki Saltwater Ballroom 5 Aralık'ta yeniden açılıyor**
> 900 kişilik Saltwater Ballroom, 5 Aralık'ta Sable Quarter konseriyle kapılarını yeniden açacak.
> Salon, mart ayındaki bir yangın yüzünden kapanmıştı.
> *Neden önemli:* İlk geceye ait biletler 20 Kasım'da satışa çıkacak.
>
> Haftaya pazartesi yine buradayız. İyi dinlemeler.
>

### English (kurgusal örnek)

Subject: `Tessaly Brand dies, Velvet Harbour skip prize · TBL Weekly`
Preview: `A Port Alder ballroom reopens on December 5; Maren Sloane goes solo.`

> **TheBluesland Weekly · Week 46**
>
> A Port Alder bassist is mourned, a prize night has a no-show, and a ballroom gets its doors back.
> Five stories, five minutes.
>
> **Tessaly Brand of The Orchard Kids has died at 68**
> The band confirmed in a statement that its bassist Tessaly Brand died at 68. Ione Marchetti and
> Dale Kerrigan played "Low Tide Lullaby" at a memorial show in Port Alder.
> *Why it matters:* Brand appeared on all six of the group's albums.
>
> **Nominees Velvet Harbour pull out of the Calder Vale Prize night**
> Velvet Harbour, nominated for album of the year, said they would skip the Calder Vale Music Prize
> ceremony on November 12. The shortlist for that category had five acts.
> *Why it matters:* Their label, Dunmore Hall Records, said the band was already booked to play in
> Hallvik that evening.
>
> **Calder Vale album chart will count gig vinyl from January 16**
> From January 16 the Calder Vale album chart will count vinyl that fans buy at the show itself.
> Web orders placed afterwards will not count.
> *Why it matters:* The first chart under the new rule is published on January 23.
>
> **Brindle Court's Maren Sloane announces solo album "Cold Frames"**
> Maren Sloane, singer of Brindle Court, announced her first solo album for February 20. The single
> "Mothlight" is out now, and she recorded the album at Ash Row Studios in Calder Vale.
> *Why it matters:* Sloane wrote the record during a break between Brindle Court tours, and the
> band has not announced a pause.
>
> **Port Alder's Saltwater Ballroom reopens on December 5**
> The Saltwater Ballroom, a 900-capacity club in Port Alder, will reopen on December 5 with a show
> by Sable Quarter. A fire closed the venue in March.
> *Why it matters:* Tickets for that first night go on sale on November 20.
>
> See you next Monday. Happy listening.
>
