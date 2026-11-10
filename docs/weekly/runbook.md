# TheBluesland Weekly · Gönderim runbook'u

Her hafta Pazar taslağından Pazartesi gönderimine kadar yapılacaklar. Saatler İstanbul saatidir.
Kaynak: Operasyon ve Yayın Odası raporu (2026-11-10). Ayrıntılı kurallar:
`docs/specs/weekly-newsletter-issue-review.md`, `docs/specs/weekly-newsletter-issue-delivery.md`.

## Önceden (bir kez)

- Kendi adresinle hem TR hem EN formuna sitedeki abone kutusundan kaydol ve onay e-postalarını onayla.
  Böylece her gönderimde bir "kanarya" alıcı olursun.
- Kit → Settings → Email: varsayılan gönderen `weekly@thebluesland.com`, posta adresi dolu.
- Cumartesi günü taslak workflow'unu elle çalıştırma: Gemini'nin ücretsiz günlük kotası
  Pazar sabahki taslağa kalsın (kota 11:00'de sıfırlanır).
- Bir kez `gh workflow run newsletter-deliver.yml -f task=smoke` çalıştır: Kit'te taslak bir
  broadcast oluşturup siler; oluşturma yolunun çalıştığını gösterir.

## Pazar

1. **11:15** Taslak workflow'u kendiliğinden çalışır (önce haberleri toplar, sonra yazar).
   **14:00** güvenlik denemesi, dal zaten varsa hiçbir şey yapmaz.
2. **~12:00** Kontrol: `gh pr list --head newsletter/<yıl>-W<hafta>`.
   - PR var, başlıkta `[DÜŞÜK MODEL]` yok → 3. adım.
   - `[DÜŞÜK MODEL]` var → ya dikkatle düzenle ya da PR'ı kapatıp dalı sil
     (`gh pr close <n> --delete-branch`) ve `gh workflow run newsletter-draft.yml -f open_pr=true`.
   - PR yok → `gh run view <id> --log-failed`; 14:00 denemesini bekle. 14:45'te hâlâ yoksa
     `gh workflow run newsletter-draft.yml -f open_pr=true`. Dal var ama PR yoksa:
     `gh pr create --draft --base main --head newsletter/<hafta> --title "TheBluesland Weekly <hafta>" --body "..."`.
3. **Düzenle.** PR açıklamasındaki "Neye bakmalı" tablosunu bitir. Haber değiştirmek için
   "Yedekler — yapıştırmaya hazır" bölümünü kullan. Front matter'daki `sendDate` değişmez.
   İsteğe bağlı: front matter'a `playlist: <slug>` ekle (`content/playlists/<slug>.md`); sayı sayfasında
   ve e-postada "Bu haftanın dinlemesi" bağlantısı çıkar. Taslak aracı bunu kendisi eklemez.
4. **Onayla (hedef: Pazar 22:00'den önce).**
   1. `gh pr ready <n>` (CI başlar, ~4 dk)
   2. `gh pr checks <n> --watch`
   3. Dal gerideyse `gh pr update-branch <n>`, sonra yine `--watch`
   4. `gh pr merge <n> --merge`
5. **Merge sonrası.** `gh run list --workflow newsletter-deliver.yml -L 1` → `gh run view <id>`.
   Özette: etiket senkronu (TR/EN en az 1) ve iki satır "scheduled" (TR 08:00, EN 15:00).
   Not: geç kalınan dil "Late" yazar ama koşu yeşil görünür; rengine değil tabloya bak.
6. **Kit'te kontrol.** Her dilde **tek** zamanlanmış broadcast olmalı; saat, etiket filtresi ve
   tahmini alıcı sayısı doğru. Her birinden "Send test" ile kendine test gönder; footer'da
   abonelikten çıkma linki ve posta adresi olsun; "Show original"da `spf=pass`, `dkim=pass`,
   `dmarc=pass`.

## Pazartesi

- **07:30–07:45** 07:30 onarım koşusu yeşil ve "unchanged".
- **08:05** Kit'te TR "sent"; kendi kutunda gelen kutusu mu spam mı; `/tr/weekly/<hafta>` 200.
- **14:30–15:05** EN için aynısı. Son merge saatleri pratikte TR ~07:40, EN ~14:40.
- TR'de ciddi sorun (yanlış içerik, spam şikayeti, yüksek bounce) görürsen 14:58'den önce EN'yi
  iptal et (aşağıda, tek dil).

## Geri alma

- **Gönderimden önce (tercih edilen):** GitHub'da merge'ü "Revert" et → ready → CI → merge.
  Deliver `Cancel` üretir, broadcast silinir, sayı web arşivinden düşer. ~10–15 dk.
- **Dakikalar kaldıysa:** Kit'te broadcast'i sil ve hemen ardından ya revert PR'ını merge et ya
  da `gh workflow disable newsletter-deliver.yml`. İkinci adımı atlarsan Pazartesi 07:30/14:30
  onarım koşusu broadcast'i yeniden kurar. Sonra `gh workflow enable newsletter-deliver.yml`.
- **Tek dil:** yalnız o dilin dosyasını kaldıran PR.
- **Gönderimden sonra:** geri alma yok. Web metnini PR ile düzelt; gerekirse Kit'ten elle
  düzeltme e-postası ya da sonraki sayıda düzeltme notu.
- **Kit reddederse:** koşu kırmızı olur; hata satırını oku, düzelt, `gh run rerun <id>`
  (önce `gh workflow run newsletter-deliver.yml -f task=deliver -f apply=false` ile kuru deneme).
