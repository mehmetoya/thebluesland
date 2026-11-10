# TheBluesland Yol Haritası

Amaç: sitenin ve TheBluesland Weekly hattının bugün nerede olduğunu, sıradaki işin hangi kapıdan
geçmesi gerektiğini tek sayfada göstermek. Tarih veya metrik yalnızca repoda ya da onaylı kararlarda
varsa yazılır; bilinmeyen yerde "bilinmiyor" denir.

Son güncelleme: 2026-11-10. Ayrıntılı kararlar `docs/adr/`, modül spec'leri `docs/specs/` altında.
`docs/product/backlog.md` ve `docs/product/plan.md` 2026-09-09 öncesinin dondurulmuş kaydıdır.

Refactor / redesign planı (ayrı doküman): [`docs/plans/refactor-redesign.md`](plans/refactor-redesign.md)

## Şu an (canlı)

| Alan | Ne çalışıyor | Kanıt |
| --- | --- | --- |
| Web sitesi | Blazor static SSR; 115 yayında playlist (+2 taslak), mood/genre/occasion/era filtreleri, koleksiyon sayfaları, ana sayfa araması, playlist mix refresh, `/playlists.json`, SEO + AI keşfedilebilirlik | `content/playlists/`, `docs/specs/playlist-search.md`, `playlist-mix-refresh.md`, `playlists-json-feed.md`, `era-mood-collection-pages.md` |
| Barındırma ve teslim | Render (Docker) + Neon Postgres, $0; her PR'da 6 zorunlu kontrol, `main`'e push'ta GHCR imajı + Render deploy | `ci.yml`, `deploy.yml`, `.github/render.yaml`, ADR-0001/0003 |
| Spotify senkronu | Aylık cache senkronu, rate-limit sonrası otomatik yeniden deneme, gizli playlist'i otomatik yayından kaldırma, `mixed-era` için otomatik dönem | `sync-spotify.yml`, `retry-rate-limited-sync.yml`, `report-eras.yml`, ADR-0002, `docs/automatic-eras.md` |
| AI kürator notu | Elle tetiklenen Gemini taslağı; içeriğe yazmaz, Mehmet PR ile uygular | `suggest-curator-note.yml`, ADR-0005 |
| Çok dillilik (TR/EN) | Her sayfa `/tr/...` ve `/en/...` altında; arayüz, Gizlilik, Koşullar ve Weekly tam Türkçe/İngilizce; dil değiştirici, hreflang, sitemap alternates. Editoryal içerik (playlist notları, koleksiyon metinleri) şimdilik İngilizce | `docs/specs/site-i18n.md` |
| Analytics | Kendi barındırdığımız ziyaret/tıklama kaydı (ham IP yok) + korumalı `/dashboard` | `docs/specs/visitor-and-playlist-click-analytics.md`, `analytics-dashboard.md` |
| Weekly: news-ingest | 2026-11-10'dan beri production'da; 2 saatte bir 7 RSS kaynağı `newsletter` şemasına, 8 hafta saklama. İlk çalışma: 7/7 kaynak başarılı, 124 haber | `newsletter-ingest.yml`, `tools/weekly-newsletter/`, `docs/specs/weekly-newsletter-news-ingest.md` |
| Weekly: story-clustering | Deterministik TF-IDF kümeleme (ağ çağrısı yok, DB'ye yazmaz); elle etiketlenmiş gerçek haftada precision 1.00, recall 0.96. Kümeler elle tetiklenen `newsletter-preview.yml` ile job summary'de görülür | `tools/weekly-newsletter/Clustering/`, `clustering.yml`, `docs/specs/weekly-newsletter-story-clustering.md` |
| Weekly: story-ranking | Kurallı, AI'sız seçim: kapsam + tür ağırlığı, tür başına en fazla 2, ilişkili hikâyeden tek biri, isteğe bağlı blues/rock/soul/indie slotu; 5 seçim + 3 yedek, her biri gerekçesiyle. W46'da Mehmet'in seçimiyle 5/5 aynı | `tools/weekly-newsletter/Ranking/`, `ranking.yml`, `docs/specs/weekly-newsletter-story-ranking.md`, `newsletter-preview.yml` (`stage: rank`) |
| Weekly: issue-drafting | Gemini (ücretsiz katman) seçilen 5 hikâye + 3 yedek için TR ve EN metni ayrı ayrı yazar; linkler, yapı ve AI notu kodda; rakam/özel ad/kopya denetimi uyarı olarak görünür. Varsayılan model yoğunsa lite modele düşer | `tools/weekly-newsletter/Drafting/`, `docs/weekly/ses-rehberi.md`, `docs/specs/weekly-newsletter-issue-drafting.md`, ADR-0006 |
| Weekly: issue-review | Her pazar 10:00 (İstanbul) `newsletter-draft.yml` taslağı `newsletter/<hafta>` dalında draft PR olarak açar (13:00'te emniyet denemesi). PR açıklamasında uyarılar, seçim nedenleri, paragraf kaynakları ve yapıştırılmaya hazır yedekler var. AI anahtarı ile yazma yetkisi ayrı job'larda; Mehmet "Ready for review" deyince CI (yeni `validate-weekly` dahil) çalışır; merge = onay | `tools/weekly-newsletter/Review/`, `docs/specs/weekly-newsletter-issue-review.md` |

## Devam eden

- **story-clustering doğrulaması** — gold set şu an tek haftalık (2026-W46). 2026-W47 tam haftası
  etiketlenip ikinci gold set olarak eklenecek. Skor düşerse eşik yeniden ayarlanacak veya
  embedding görevi açılacak (spec §3 karar kuralı).

## Sıradaki (MVP, sırayla)

Onaylı capability map: [`docs/specs/weekly-newsletter.md`](specs/weekly-newsletter.md). Her modül
koddan önce kendi onaylı spec'ini (`docs/specs/weekly-newsletter-<modül>.md`) ister.

| # | İş | Bağımlı olduğu | Geçmesi gereken kapı |
| --- | --- | --- | --- |
| 1a | **issue-delivery** — kod hazır (`deliver`, `newsletter-deliver.yml`, Kit ücretsiz planı); TR pazartesi 08:00, EN 15:00, Kit'in zamanlayıcısıyla | issue-review | Mehmet'in Kit kurulumu: hesap, DNS (SPF/DKIM), `weekly-tr`/`weekly-en` etiketleri, double opt-in'li iki form, `KIT_API_KEY`, TR formunda KVKK rıza metni; sonra dry-run ve test aboneyle ilk gönderim |
| 1b | **weekly-archive** — yayında: `/{dil}/weekly`, `/{dil}/weekly/{sayı}`, Kit kayıt formu (form id'leri `Weekly:KitFormIdTr/En` ayarıyla açılır) | issue-review | Kit'te iki form + Render'da form id ayarları |

Hattın değişmez kısıtları: $0 maliyet; Spotify verisi hiçbir AI prompt'una girmez; AI yalnız GitHub
Actions'ta (Render'a hiç girmez); tek insan adımı Mehmet'in PR'ı düzeltip merge etmesi; abone
listesi bizde değil, sağlayıcıda; Web `newsletter` şemasını okumaz.

## Sonra (MVP sonrası)

- **engagement-signals** — Reddit/YouTube/Trends ilgi sinyalleri, story-ranking'e ek puan.
- Map'te açıkça MVP dışı bırakılanlar: tam metin scraping, türe zorunlu kota, "sürpriz hikâye"
  kuralı, büyüme pazarlaması. Yeniden açılmaları yeni karar ister.
- Site tarafındaki refactor / redesign: ayrı dokümanda değerlendiriliyor (yukarıdaki bağlantı).

## Açık kararlar / riskler

| Konu | Durum |
| --- | --- |
| E-posta sağlayıcısı | Seçilmedi. TR + EN iki liste, double opt-in ve ücretsiz katman şart; seçim 5a ve 5b'yi kilitliyor |
| ADR-0006 | Kabul edildi (2026-11-10): AI metni yalnız Mehmet'in merge'ünden sonra dışarı çıkar; olgu kartı dışı iddia yok; her sayıda AI notu |
| Ses rehberi | Onaylandı (2026-11-10): `docs/weekly/ses-rehberi.md`, örnek W46 sayısıyla; denetçinin kalibrasyonu |
| Gemini ücretsiz katmanı | Haftada 2 çağrı; ilk production çalışmasında varsayılan model 503 verdi, lite model yazdı. Varsayılan yoğunsa otomatik lite'a düşülüyor |
| Bot PR'ı ve CI | Çözüldü (issue-review §3): CI `ready_for_review` ile tetiklenir; draft PR bu adım olmadan merge edilemez, bot da PR'dan önce `validate-weekly` çalıştırır |
| Zamanlama güvenilirliği | GitHub cron dakikalarca gecikebilir ("o sabah" garantisi, "tam saatinde" değil); zamanlanmış workflow'lar 60 gün repo hareketsizliğinde duraklar |
| Kaynak kullanım koşulları | RSS özetleri yalnız iç kullanım; yayınlanmaz. Yeni kaynak eklenirse koşullar yeniden kontrol edilir |
| Başarı ölçütleri | Abone sayısı, açılma oranı gibi hedefler tanımlı değil — bilinmiyor |
| Özel alan adı | Yönlendirme kodu `main`'de; Render'da `Site__PublicOrigin`'in ayarlı olup olmadığı repodan doğrulanamıyor |
| Render | Ücretsiz saatler workspace başına; servis workspace'teki tek ücretsiz servis olmalı. Native rollback gerçek bir olayla hiç denenmedi |
| ADR numaralandırması | 0004 boşluğu açıklanmadı (dondurulmuş `plan.md`, 2026-09-05) |
