# Spec: issue-review

Modül: `issue-review` · Map: [weekly-newsletter.md](weekly-newsletter.md) · Önceki modül:
[issue-drafting](weekly-newsletter-issue-drafting.md) · Sınır: [ADR-0006](../adr/0006-weekly-newsletter-ai-metin-siniri.md)
madde 2 ve 5 · Durum: UYGULANDI — 2026-11-10 (PR #97; uçtan uca deneme PR #98 kapatıldı; ilk zamanlanmış çalışma 2026-11-15)

## Assumptions

1. Taslak her pazar otomatik olarak bir **draft PR** ile gelir. Mehmet'in tek işi PR'da metni
   düzeltmek, "Ready for review" demek ve merge etmektir. Merge, onaydır (ADR-0006 madde 2).
2. Yazım, issue-drafting'in `draft` komutuyla yapılır. Bu modül yeni metin üretmez; dosyaları
   bir dala koyar, PR'ı açar ve PR açıklamasını hazırlar.
3. Yeni servis, credential, paket veya tablo eklenmez. PR, workflow'un kendi `GITHUB_TOKEN`'ı ile
   açılır. GitHub App ve kişisel token kullanılmaz.
4. Yedekle değiştirme seyrek olmalıdır: story-ranking'in hedefi, Mehmet'in çoğu hafta hiçbir şeyi
   değiştirmemesidir. Bu yüzden MVP'de değiştirme kopyala-yapıştırla yapılır; ayrı bir otomasyon
   eklenmez (bkz. Karar önerisi 3).

## Objective

Her pazar 11:15'te (Europe/Istanbul; pencere 10:00'da biter), o haftanın TR ve EN sayısını
`content/weekly/<yıl>-W<hafta>.{tr,en}.md` olarak tek bir draft PR'da Mehmet'in önüne koymak.
Mehmet ne olduğunu, neden seçildiğini, neye bakması gerektiğini ve yedekleri aynı sayfada görür.

- Mehmet olarak: pazar günü GitHub bildirimiyle bir PR almak ve uyarılı cümlelere doğrudan
  bakmak istiyorum.
- Mehmet olarak: bir haberi yedekle değiştirmek istediğimde yapıştırılmaya hazır metni PR
  açıklamasında bulmak istiyorum.
- issue-delivery olarak: yalnızca `main`'e merge edilmiş ve CI'dan geçmiş bir dosya okumak
  istiyorum.

## Design

### 1. Zamanlama ve tekrar deneme

- `newsletter-draft.yml`'e iki `schedule` eklenir: pazar **08:15 UTC** (11:15 İstanbul) ve
  emniyet için pazar **11:00 UTC** (14:00 İstanbul). Gemini ücretsiz kotası 00:00 Pasifik'te
  (08:00 UTC) sıfırlanır; 07:00 UTC'deki çalışma cumartesinin kotasını harcıyordu. Haftanın
  penceresi yine pazar 10:00 İstanbul'da biter (`issue-week` aynı kalır).
- Çalışma **idempotenttir**. O haftanın `newsletter/<yıl>-W<hafta>` PR'ı (herhangi bir durumda)
  varsa ya da sayı dosyaları `main`'de zaten varsa hiçbir şey yapılmaz ve "zaten açık" diye özet
  yazılır. Yalnız dal var, PR yok ise (push sonrası `gh pr create` düştü) taslak yeniden üretilir,
  mevcut dalın üstüne commit'lenir ve PR açılır. Böylece ikinci cron yalnızca ilki başarısız
  olduğunda iş yapar. Gemini iki modelde de düşerse ilk çalışma kırmızı biter; GitHub bunu
  e-postayla bildirir.
- Elle tetikleme kalır. Yeni `open_pr` girdisi varsayılan olarak `false`'tur: elle çalıştırma
  bugünkü gibi yalnızca artifact üretir. Zamanlanmış çalışmada değer her zaman `true`'dur.
- `concurrency: newsletter-draft` ile iki çalışma üst üste binmez.

### 2. İki job: yazan ve PR açan ayrıdır (ADR-0006 madde 5)

| Job | Yetki | Secret | İş |
|---|---|---|---|
| `draft` | `contents: read` | `GEMINI_API_KEY`, `NEON_NEWSLETTER_CONNECTION_STRING` (yalnızca Draft adımında) | Bugünkü gibi taslağı yazar, rapor ve PR gövdesiyle birlikte artifact'e koyar |
| `open-pr` | `contents: write`, `pull-requests: write` | **yok** | Artifact'i indirir, `newsletter/<hafta>` dalına commit'ler, draft PR açar |

- AI anahtarı ile repoya yazma yetkisi **hiçbir zaman aynı job'da bulunmaz**. Model çıktısı
  `open-pr` job'una yalnızca dosya olarak geçer. Dosya kabuğa enterpolasyonla girmez; PR gövdesi
  `--body-file` ile verilir.
- `open-pr` yalnızca `newsletter/` önekli bir dala push eder. Dal adı koddan gelir
  (`newsletter/2026-W47`) ve workflow'da `^newsletter/[0-9]{4}-W[0-9]{2}$` ile doğrulanır.
- PR'ın atanan kişisi `github.repository_owner`'dır. Bildirim böylece garanti olur.
- Repo ayarı olan "Allow GitHub Actions to create pull requests" bir kez açılır
  (`gh api` ile; kod dışı adım).

### 3. Bot PR'ı CI'ı tetiklemiyor — çözüm: "Ready for review"

`GITHUB_TOKEN` ile açılan PR'lar `pull_request` workflow'larını tetiklemez (ADR-0006 Sonuçlar,
roadmap riski). Önerilen çözüm yeni credential gerektirmez:

1. `ci.yml`'in `pull_request` tetikleyicisine `ready_for_review` türü eklenir
   (`types: [opened, synchronize, reopened, ready_for_review]`).
2. Draft PR GitHub'da merge edilemez. Mehmet merge etmeden önce "Ready for review"a basmak
   **zorundadır**. Bu olay Mehmet'in kimliğiyle gelir ve tüm CI'ı tetikler.
3. Mehmet'in PR'daki her düzeltme commit'i de (`synchronize`) CI'ı tetikler.
4. Ek emniyet olarak `open-pr` job'u, dalı push etmeden önce yeni `validate-weekly` denetimini
   çalıştırır. Bozuk bir dosya PR'a hiç gelmez.

Reddedilen alternatifler: GitHub App (Mehmet'in kurması gereken yeni bir kimlik ve özel anahtar),
fine-grained PAT (süresi dolar, bir kişiye bağlıdır), CI'ı `workflow_dispatch` ile tetiklemek
(check'ler PR'da görünür, ama branch protection ile eşleşmesi kırılgandır).

### 4. `content/weekly/` doğrulaması

Yeni `validate-weekly` alt komutu (`tools/weekly-newsletter`) ve `ci.yml` content-validation
job'unda yeni bir adım. Kurallar:

| Kural | Neden |
|---|---|
| Dosya adı `<yıl>-W<hafta>.<tr|en>.md`; front matter'daki `issue` ve `language` adla aynı | issue-delivery ve weekly-archive dosyayı addan bulur |
| Front matter issue-drafting §4 biçiminde parse edilir; `sendDate` o ISO haftanın pazartesisi | Sözleşme |
| 5 hikâye; front matter'daki her hikâyenin ≥ 1 kaynağı, kaynak URL'si `https://` | ADR-0006 madde 3 |
| Gövdedeki `## ` başlık sayısı front matter'daki hikâye sayısına eşit | Değiştirmede yarım kalan düzenleme |
| Sonda AI notu birebir var (dile göre) | ADR-0006 madde 4 |
| `*.backups.*.md` dosyası yok | Yedekler `main`'e girmez |

Bu denetim deterministik ve hızlıdır; Gemini veya veritabanı gerektirmez.

### 5. Yedekler dalda değil, PR açıklamasında

issue-drafting §4'teki "yan dosya merge'de silinir" maddesi şöyle değişir: **yan dosyalar dala hiç
commit'lenmez.** Yedeklerin metni yalnızca PR açıklamasında ve artifact'te bulunur. Böylece
silinmesi unutulacak bir dosya kalmaz.

### 6. PR açıklaması

`draft` komutu, raporun yanında `pr-body.md` de üretir (ayrı formatter, saf fonksiyon):

```
## TheBluesland Weekly · 2026-W47 · gönderim 2026-11-16 (TR 08:00, EN 15:00)

Merge = onay. Göndermeden önce: "Ready for review" → CI yeşil → merge.
Pazartesi 08:00'e kadar merge edilmezse TR sayısı bu hafta gitmez (EN için 15:00).

### Neye bakmalı
TR ✅ uyarı yok · EN ⚠️ 1 (tablo: hikâye → uyarı)
Model: gemini-3.8-flash

### Seçim
(story-ranking raporu: #, hikâye, kaynaklar, puan dökümü; atlananlar <details> içinde)

### Paragrafların kaynakları (ADR-0006 madde 3)
1. Richard Lloyd… — Pitchfork: "<başlık>" · Stereogum: "<başlık>"

### Yedekler — yapıştırmaya hazır
<details><summary>Y1 · NewJeans… (2 kaynak · legal +1.0 = 3.00)</summary>
  front matter girdisi (key + sources) ve TR/EN bölümü: başlık, gövde, neden önemli, Kaynaklar satırı
</details>
```

GitHub'ın 65.536 karakterlik PR gövdesi sınırını aşarsa yedeklerin metni kısaltılmaz; gövdeye
"yedekler artifact'te" notu düşülür ve link verilir. Bir sayı ~450 kelime olduğundan bu
beklenmez.

### 7. Ölçüm (issue-drafting başarı ölçütü)

Mehmet'in düzeltme miktarı, merge'den sonra elle ölçülür: bot commit'i ile merge edilen dosya
arasındaki diff. Otomatik izleme (bir `pull_request: closed` job'u) ilk 4 haftanın verisi
"birkaç cümle" hedefini gösterene kadar ertelenir. Elle ölçüm `git diff` ile iki dakikalık iştir.

## Tech Stack

.NET 10, mevcut paketler. GitHub CLI (`gh`) runner'da zaten var. Yeni paket, tablo, secret ve
migration yok.

## Project Structure

```
tools/weekly-newsletter/Review/{WeeklyIssueValidator, ValidateWeeklyCommand, PullRequestBodyFormatter}.cs
tools/weekly-newsletter/Drafting/DraftCommand.cs           (pr-body.md da yazar; yedek dosyaları ayrı klasöre)
.github/workflows/newsletter-draft.yml                     (schedule, open_pr, open-pr job)
.github/workflows/ci.yml                                   (ready_for_review, validate-weekly adımı)
tests/TheBluesland.UnitTests/WeeklyNewsletter/Review/*Tests.cs
tests/TheBluesland.UnitTests/Workflows/NewsletterDraftWorkflowTests.cs
```

## Testing Strategy

- **Doğrulayıcı:** her kural için olumlu ve olumsuz örnek. issue-drafting yazıcısının ürettiği
  dosya sıfır hatayla geçer (round-trip). Yarım kalmış bir yedek değişikliği yakalanır.
- **PR gövdesi:** uyarı tablosu, kaynak listesi ve yedek blokları. Model metni Markdown'ı
  bozamaz (escape). Boyut sınırı davranışı da test edilir.
- **Workflow (YAML üzerinde, mevcut izolasyon testleri deseninde):**
  - `GEMINI_API_KEY` ve Neon secret'ı yalnızca `draft` job'unun Draft adımında geçer;
  - `open-pr` job'unda hiçbir `secrets.` referansı yoktur;
  - yazma yetkisi yalnızca `open-pr` job'undadır; workflow düzeyinde `contents: read` kalır;
  - push edilen dal `newsletter/` önekli ve regex'le doğrulanıyor;
  - input'lar ve model çıktısı `run:` içine enterpolasyonla girmez;
  - cron'lar `0 7 * * 0` ve `0 10 * * 0`;
  - `ci.yml`'de `ready_for_review` var.
- **Uçtan uca (elle, bir kez):** dispatch'te `open_pr: true` ile gerçek bir draft PR açılır;
  "Ready for review" CI'ı tetikler; PR merge edilmeden kapatılır ve dal silinir.

## Boundaries

- **Always:** AI anahtarı ile yazma yetkisi ayrı job'larda; bot yalnızca `newsletter/` dallarına
  ve yalnızca draft PR olarak yazar; doğrulanmamış dosya PR'a girmez.
- **Ask first:** yeni credential (GitHub App/PAT); otomatik merge; yedekle değiştirmeyi otomatik
  yapan bir workflow; `main`'e bot yazımı.
- **Never:** bot'un PR'ı onaylaması veya merge etmesi; aynı hafta için ikinci PR; Mehmet'in
  düzenlediği bir dalı yeniden yazmak.

## Success Criteria

- [ ] Pazar 08:15 UTC'de draft PR kendiliğinden açılır; ikinci cron hiçbir şey yapmaz.
- [x] "Ready for review" tüm CI job'larını tetikler; `validate-weekly` yeşil. (PR #98: 6/6 yeşil.)
- [x] PR açıklaması Mehmet'in ihtiyacının tamamını içerir: uyarılar, seçim nedenleri, kaynaklar
      ve yapıştırılmaya hazır yedekler.
- [x] İzolasyon testleri iki job'un ayrımını kanıtlar.
- [x] Tam build ve test yeşil (745); yeni paket ve secret yok.

## Kararlar (onaylandı, 2026-11-10)

1. **CI tetikleme: "Ready for review" + bot tarafında ön doğrulama** (§3). Yeni credential yok.
2. **Yetki ayrımı: iki job** (§2). AI anahtarı ve yazma yetkisi asla aynı yerde bulunmaz.
3. **Yedekle değiştirme MVP'de kopyala-yapıştır** (§6). İlk 4 haftada Mehmet ayda birden fazla
   değiştirirse bir `swap` workflow'u ayrı bir spec ile eklenir.
4. **Yan dosyalar dala girmez** (§5). issue-drafting spec'indeki madde buna göre güncellenir.
5. **Emniyet cron'u 14:00 İstanbul** (§1). Gemini sabah düşerse taslak yine pazar günü gelir.
