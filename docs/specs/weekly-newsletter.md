# Capability Map: TheBluesland Weekly

Durum: ONAYLANDI — Mehmet, 2026-11-10 (takvim önerisi dahil). Önceki `CAPABILITY-MAP.md` / `SPEC-news-ingest.md`
taslakları referans alınmadı; bu map onaylı niyetten ve repo gerçeklerinden sıfırdan çıkarıldı.

## Niyet (onaylı, 2026-11-10)

Her pazartesi global müzik gündeminden seçilmiş 5 haber; Türkçe (öncelikli) ve İngilizce iki ayrı
sayı — çeviri değil, aynı TheBluesland sesi iki dilde (kısa, hafif esprili, "neden önemli").
Ayrıştırıcı iki şey: **ses** ve **seçim** (kapsam taban + önem + çeşitlilik; 5'ten biri
blues/rock/soul/indie *olabilir*, global gündem önceliklidir). Tek insan adımı Mehmet'in haftalık
düzeltme + onayı. MVP dışı: Reddit/YouTube/Trends sinyalleri, tam metin scraping, türe zorunlu
kota, "sürpriz hikâye" kuralı, büyüme pazarlaması.

## Modüller

| Module id | Sorumluluk | Depends on | Faz |
|---|---|---|---|
| news-ingest | Yapılandırmadaki RSS kaynaklarını periyodik çekmek, URL'i normalize edip tekrarsız saklamak | — | MVP |
| story-clustering | Aynı olayı anlatan haberleri tek hikâyede birleştirmek; hikâye başına kaç bağımsız yayın yazdığını çıkarmak | news-ingest | MVP |
| story-ranking | Haftalık hikâyeleri kapsam + önem ile puanlamak, çeşitlilik kurallarını ve isteğe bağlı tür tercihini uygulamak; ilk 5 + 3 yedek | story-clustering | MVP |
| issue-drafting | Seçilen hikâyeler için olgu kartı; ses rehberine göre TR ve EN metni ayrı ayrı yazmak; taslağı `content/weekly/` dosyası olarak üretmek | story-ranking | MVP |
| issue-review | Taslağı draft PR olarak açmak (yedekler PR açıklamasında); yedekle değiştirme; Mehmet'in PR'da düzeltip merge etmesi = onay | issue-drafting | MVP |
| issue-delivery | Merge edilmiş (onaylı) sayıyı e-posta sağlayıcısındaki TR ve EN listelere bir kez göndermek; onay yoksa göndermemek | issue-review | MVP |
| weekly-archive | `/weekly` altında TR/EN sayı sayfaları (`content/weekly/` Markdown'dan, mevcut içerik deseniyle) + sağlayıcının abone formu | issue-review | MVP |
| engagement-signals | Reddit/YouTube/Trends ilgi sinyalleri; story-ranking'e ek puan bileşeni | story-ranking | Sonra |

Build order: news-ingest → story-clustering → story-ranking → issue-drafting → issue-review →
issue-delivery, weekly-archive (paralel) → (sonra) engagement-signals

## Repo ile hizalanan yapısal kararlar

- **Onay yüzeyi = pull request, admin sayfası değil.** Spec §6 MVP'de `/admin` ve `/login` yok
  diyor; §3.3 production web'den AI çağrısını yasaklıyor. Editoryal içeriğin mevcut yolu zaten
  "Markdown + PR onayı" (ADR-0002 madde 5). Sayı `content/weekly/<yıl>-W<hafta>.{tr,en}.md`
  olarak bot PR'ında gelir; Mehmet metni PR'da düzeltir, merge eder. Bildirim GitHub'ın PR
  bildirimidir (ek servis yok).
- **Tek araç, yeni katman yok.** Tüm job'lar tek bir `tools/<newsletter-aracı>` console
  uygulamasında alt komut olarak (`ingest`, `rank`, `draft`, `send`) — `spotify-playlist-fetcher`
  deseni. Şema `TheBluesland.Data` içinde ayrı `newsletter` şemasında; **Web bu şemayı hiç okumaz**
  (arşiv `content/weekly/` dosyalarından render edilir).
- **Abone listesi bizde değil, e-posta sağlayıcısında.** Web salt okunur kalır, kişisel veri DB'ye
  girmez; form ve double opt-in sağlayıcının.
- **AI yalnızca GitHub Actions'ta.** Mevcut credential izolasyon deseni (ADR-0002 madde 4,
  ADR-0005 madde 3): secret yalnız ilgili workflow'a scope edilir, Render'a girmez.
- **Spotify ayrımı.** Newsletter kodu `spotify_playlist_cache`'i okumaz; Spotify verisi hiçbir
  newsletter prompt'una girmez.

## Kararlar (2026-11-10)

1. **ADR-0006 yazılacak — evet.** ADR-0005 bot PR'ı kürator notu için reddetmişti ve kapsamı
   yalnızca kürator notu. Newsletter'da AI metni insan merge'ü sonrası *dışarıya gönderiliyor*;
   bu yeni AI-çıktı sınırı issue-drafting spec'inden önce ADR-0006 ile kayda geçer.
2. **$0 maliyet — evet.** Yazım (ve gerekirse embedding) Gemini ücretsiz katmanında; e-posta TR ve
   EN iki listeyi destekleyen ücretsiz katmanlı bir sağlayıcıda. Ücretli katman gerektiren her
   seçenek reddedilir.
3. **Takvim — "ideal gün/saat", aşağıdaki öneri onay bekliyor.** Saatler yapılandırmada durur,
   kod değişmeden kaydırılabilir.
   - Taslak PR: pazar 10:00 (Europe/Istanbul).
   - TR gönderim: pazartesi 08:00 (Europe/Istanbul) — haftaya başlarken, işe yolda okuma.
   - EN gönderim: pazartesi 15:00 (Europe/Istanbul) = 12:00 Londra / 07:00 New York.
   - Kesim: gönderim saatine kadar merge yoksa o dilin sayısı o hafta gönderilmez (geç merge
     sonradan otomatik gönderilmez).
   - GitHub Actions cron'u dakikalarca gecikebilir; "tam saatinde" değil "o sabah" garantisi.
