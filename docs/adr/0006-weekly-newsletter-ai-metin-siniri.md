# ADR-0006 — TheBluesland Weekly: AI metni dışarı gider, ama yalnızca insan onayından sonra

Durum: **Önerildi** — Mehmet'in onayını bekliyor (2026-11-10). Onaylanmadan `issue-drafting`
modülünün spec'i yazılmaz (capability map, karar 1).

## Bağlam

ADR-0005 AI'ı yalnızca kürator notu için ve dar bir şekilde açtı. AI'nın çıktısı her zaman taslaktı:
hiçbir dosyaya otomatik yazılmıyordu, bot PR'ı açılmıyordu, Mehmet metni elle taşıyordu. Kapsamı
yalnızca kürator notuydu.

TheBluesland Weekly'de durum farklı:

- AI'nın yazdığı TR ve EN metinler, Mehmet düzeltip merge ettikten sonra **abonelere e-postayla
  gider** ve `/weekly` arşivinde **yayınlanır**. Yani AI metni dışarı çıkıyor.
- Taslak bir **bot PR'ı** ile gelir (capability map: onay yüzeyi = PR). ADR-0005 bu deseni kürator
  notu için reddetmişti.
- Girdi Spotify verisi değil: herkese açık müzik yayınlarının RSS başlıkları, feed özetleri ve
  linkleri (`newsletter.news_article`).

Bu yeni sınır, kod yazılmadan önce kayda geçmeli.

## Karar

1. **Girdi kapsamı: yalnızca o haftanın seçilmiş hikâyelerinin olgu kartı.** Prompt'a girebilenler:
   - story-ranking'in seçtiği 5 hikâye (ve yedekler), bunların üye haberlerinin başlığı, feed özeti
     (ilk 300 karakter), kaynak adı, yayın zamanı ve linki;
   - repodaki ses rehberi (`docs/weekly/ses-rehberi.md`, issue-drafting spec'iyle yazılır).

   Prompt'a giremeyenler:
   - **Spotify kaynaklı hiçbir veri** (ADR-0005'in kapsamı bu hattı kapsamaz; newsletter kodu
     `spotify_playlist_cache`'i okumaz);
   - makalelerin tam metni (çekilmez; map: MVP dışı);
   - abone verisi (sağlayıcıdadır, bizde hiç yoktur).

   Model dış kaynak çekmez; prompt kapalı bir metindir.

2. **Çıktı önce taslaktır; dışarı çıkış yalnızca insan merge'üyle olur.**
   - Bot, taslağı `content/weekly/<yıl>-W<hafta>.{tr,en}.md` olarak bir **draft PR**'da açar.
     Doğrudan `main`'e yazamaz.
   - Gönderim (`issue-delivery`) yalnızca `main`'e merge edilmiş dosyayı okur. Merge yoksa o dilin
     sayısı o hafta gönderilmez; sonradan otomatik gönderilmez (map: takvim kararı).
   - Merge, Mehmet'in onayıdır. Mehmet metni PR'da düzeltebilir veya bir hikâyeyi yedekle
     değiştirebilir.

3. **Olgu sınırı.** AI metni yalnızca olgu kartındaki olguları kullanır. Her hikâye en az bir kaynak
   linkiyle yayınlanır. Feed özetinden cümle kopyalanmaz; ses rehberine göre yeniden yazılır. Olgu
   kartında olmayan bir iddia (tarih, rakam, alıntı) taslakta görünürse, bu Mehmet'in PR'da
   sileceği bir hata olarak kabul edilir. issue-drafting, bunu azaltmak için her paragrafın
   dayandığı kaynakları PR açıklamasında listeler.

4. **Şeffaflık.** Her sayının altında kısa bir not yer alır: "Bu sayı yapay zekâ yardımıyla
   hazırlandı ve TheBluesland editörü tarafından düzenlendi." Kesin metin ses rehberinde.

5. **Credential ve yetki izolasyonu (ADR-0005 madde 3'ün deseni).**
   - `GEMINI_API_KEY` yalnızca taslak workflow'unun yazım adımına verilir; Render'a asla girmez.
   - Taslak workflow'u `contents: write` ve `pull-requests: write` alır, çünkü PR açmak için
     gerekir. Bu yetki yalnızca o workflow'da bulunur ve yalnızca `newsletter/` önekli dallara
     yazar. Mevcut secret izolasyon testleri bu kuralları da kapsayacak şekilde genişletilir.
   - Web uygulaması hiçbir AI sağlayıcısıyla konuşmaz; arşiv sayfaları merge edilmiş Markdown'dan
     render edilir.

6. **Maliyet: $0.** Gemini ücretsiz katmanı kullanılır. Ücretsiz katman yetmezse hat durur ve
   özette görünür; ücretli katmana otomatik geçiş yoktur.

## Gerekçe

- Tek insan adımı hedefi (map) bot PR'ı gerektiriyor. ADR-0005'te reddedilmesinin nedeni, kürator
  notunun nadir ve tek tek yazılması, bot PR'ının ise gürültü olmasıydı. Weekly'de iş haftalık ve
  sabit: PR doğru araç.
- Girdi herkese açık haber başlıkları ve özetleri; Spotify Developer Policy'nin makine öğrenmesi
  kısıtı bu hatta uygulanmaz, çünkü Spotify verisi hiç girmez.
- "Merge = onay" mevcut editoryal akışla aynı (ADR-0002 madde 5). Yeni bir onay yüzeyi açılmıyor.

## Alternatifler

- **AI'sız, elle yazım.** Reddedildi: başarı ölçütü "her hafta aksamadan, Mehmet yalnızca birkaç
  cümle düzeltip onaylar". Elle yazım bu ölçütü karşılamaz.
- **AI metni onaysız gönderilir.** Reddedildi: olgu hatası ve ses sapması doğrudan abonelere gider.
- **Taslak PR yerine job summary + elle kopyalama (ADR-0005 deseni).** Reddedildi: iki dosyayı
  her hafta elle taşımak tek insan adımını ikiye çıkarır.

## Sonuçlar

- issue-drafting spec'i bu ADR'ın maddelerine bağlanır (girdi listesi, olgu sınırı, şeffaflık notu).
- Repoya yazabilen ikinci bir workflow oluşur. İzolasyon testleri genişletilir ve dal öneki
  kuralı test edilir.
- `GITHUB_TOKEN` ile açılan PR'lar `pull_request` workflow'larını tetiklemez. `content/weekly/`
  doğrulamasının nasıl çalışacağı issue-review spec'inde çözülür (roadmap riski).
- Abonelere giden her metin bir insan merge'ünden geçmiş olur. Bu ADR değişmeden bu kural
  gevşetilemez.
