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
| Analytics | Kendi barındırdığımız ziyaret/tıklama kaydı (ham IP yok) + korumalı `/dashboard` | `docs/specs/visitor-and-playlist-click-analytics.md`, `analytics-dashboard.md` |
| Weekly: news-ingest | 2026-11-10'dan beri production'da; 2 saatte bir 7 RSS kaynağı `newsletter` şemasına, 8 hafta saklama. İlk çalışma: 7/7 kaynak başarılı, 124 haber | `newsletter-ingest.yml`, `tools/weekly-newsletter/`, `docs/specs/weekly-newsletter-news-ingest.md` |

## Devam eden

**story-clustering** — branch `feature/newsletter-story-clustering`, spec
`docs/specs/weekly-newsletter-story-clustering.md` (onay: 2026-11-10; henüz `main`'de değil).

- Bitti: deterministik TF-IDF kümeleyici; elle etiketlenmiş gold set'te precision 1.00, recall 0.96.
- Kalan: `cluster` alt komutu, Markdown raporu, yalnız elle tetiklenen `newsletter-preview.yml`.

## Sıradaki (MVP, sırayla)

Onaylı capability map: [`docs/specs/weekly-newsletter.md`](specs/weekly-newsletter.md). Her modül
koddan önce kendi onaylı spec'ini (`docs/specs/weekly-newsletter-<modül>.md`) ister.

| # | İş | Bağımlı olduğu | Geçmesi gereken kapı |
| --- | --- | --- | --- |
| 1 | **ADR-0006** — dışarı gönderilen AI metninin sınırı | — (story-ranking ile paralel yazılabilir) | Mehmet'in ADR onayı. ADR-0005 yalnız kürator notunu kapsıyor ve bot PR'ı reddetmişti; newsletter'da AI metni merge sonrası dışarı gidiyor |
| 2 | **story-ranking** — kapsam + önem puanı, çeşitlilik kuralları; ilk 5 + 3 yedek | story-clustering `main`'de | Onaylı spec |
| 3 | **issue-drafting** — olgu kartı, TR ve EN metin ayrı ayrı, `content/weekly/` taslağı | story-ranking, **ADR-0006 kabul** | Onaylı spec; ses rehberi (repoda henüz yok); Gemini ücretsiz katman |
| 4 | **issue-review** — bot draft PR `content/weekly/<yıl>-W<hafta>.{tr,en}.md`, yedekler PR açıklamasında; pazar 10:00 (Europe/Istanbul) | issue-drafting | Onaylı spec; repoya yazabilen ikinci workflow olacağı için secret/izin izolasyon testlerinin genişletilmesi |
| 5a | **issue-delivery** — onaylı sayıyı bir kez gönder; TR pazartesi 08:00, EN pazartesi 15:00; saatine kadar merge yoksa o dil o hafta gitmez | issue-review | E-posta sağlayıcısı seçimi (ücretsiz katman, TR + EN iki liste); onaylı spec |
| 5b | **weekly-archive** — `/weekly` TR/EN sayı sayfaları (`content/weekly/`'den) + sağlayıcının abone formu | issue-review (5a ile paralel) | UI/UX tasarım çalışması; abone formu için sağlayıcı seçimi ve CSP uyumu |

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
| ADR-0006 | Yazılmadı; issue-drafting'in ön koşulu |
| Ses rehberi | Map "ses rehberine göre" diyor ama repoda böyle bir doküman yok; issue-drafting spec'inden önce gerekli |
| Gemini ücretsiz katmanı | Haftalık TR + EN yazımına yetip yetmediği ölçülmedi |
| Bot PR'ı ve CI | `GITHUB_TOKEN` ile açılan PR'lar `pull_request` workflow'larını tetiklemez; `content/weekly/` doğrulamasının nasıl koşacağı issue-review spec'inde çözülmeli |
| Zamanlama güvenilirliği | GitHub cron dakikalarca gecikebilir ("o sabah" garantisi, "tam saatinde" değil); zamanlanmış workflow'lar 60 gün repo hareketsizliğinde duraklar |
| Kaynak kullanım koşulları | RSS özetleri yalnız iç kullanım; yayınlanmaz. Yeni kaynak eklenirse koşullar yeniden kontrol edilir |
| Başarı ölçütleri | Abone sayısı, açılma oranı gibi hedefler tanımlı değil — bilinmiyor |
| Özel alan adı | Yönlendirme kodu `main`'de; Render'da `Site__PublicOrigin`'in ayarlı olup olmadığı repodan doğrulanamıyor |
| Render | Ücretsiz saatler workspace başına; servis workspace'teki tek ücretsiz servis olmalı. Native rollback gerçek bir olayla hiç denenmedi |
| ADR numaralandırması | 0004 boşluğu açıklanmadı (dondurulmuş `plan.md`, 2026-09-05) |
