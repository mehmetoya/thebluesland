# Spec: issue-delivery

Modül: `issue-delivery` · Map: [weekly-newsletter.md](weekly-newsletter.md) · Önceki modül:
[issue-review](weekly-newsletter-issue-review.md) · Sınır: [ADR-0006](../adr/0006-weekly-newsletter-ai-metin-siniri.md)
madde 2 · Durum: TASLAK — 2026-11-10 (onay bekliyor)

## Assumptions

1. Abone listesi **bizde değil, sağlayıcıdadır** (map). Kayıt formu, double opt-in, abonelikten
   çıkma ve abone verisi tamamen sağlayıcıdadır. Bizim kodumuz hiçbir e-posta adresi görmez.
2. Gönderim yalnızca `main`'e merge edilmiş `content/weekly/<yıl>-W<hafta>.<dil>.md` dosyasını okur
   (ADR-0006 madde 2). Veritabanı, Gemini ve taslak dalları bu modülün girdisi değildir.
3. Takvim map'te onaylıdır: TR pazartesi 08:00, EN pazartesi 15:00 (Europe/Istanbul). Gönderim
   saatine kadar merge yoksa o dilin sayısı o hafta gitmez; geç merge sonradan otomatik gönderilmez.
4. Maliyet $0'dır. Sağlayıcının ücretsiz katmanı yetmezse hat durur ve bu görünür olur; ücretli
   katmana otomatik geçiş yoktur (ADR-0006 madde 6).
5. Kayıt formunun `/weekly` sayfasına yerleştirilmesi weekly-archive'ın işidir. Bu modül yalnızca
   formun sağlayıcı tarafında hazır olmasını şart koşar.

## Objective

Merge edilmiş sayıyı, kendi saatinde, doğru dil listesine **bir kez** göndermek. Mehmet için ek bir
adım olmamalı: merge etmek, gönderimi zamanlamak demektir.

- Mehmet olarak: pazar günü merge ettiğim sayının pazartesi kendiliğinden gitmesini istiyorum.
- Mehmet olarak: merge'den sonra ama gönderimden önce bir yazım hatasını `main`'de düzeltirsem,
  gidecek e-postanın da düzelmesini istiyorum.
- Abone olarak: yalnızca seçtiğim dilde, haftada bir e-posta almak ve tek tıkla ayrılmak istiyorum.

## Design

### 1. Sağlayıcı: Kit (ücretsiz Newsletter planı)

Resmi sayfalardan doğrulandı (2026-11-10). Şartlar: API ücretsiz planda olmalı, API kampanya
oluşturup zamanlayabilmeli, TR ve EN ayrı kitlelere gidebilmeli, kayıt formu double opt-in'li
olmalı.

| Sağlayıcı | Ücretsiz sınır | API ücretsizde | API ile zamanlama | TR/EN ayrımı | Double opt-in form | Sonuç |
|---|---|---|---|---|---|---|
| **Kit** | 10.000 abone, gönderim sınırı yok | Evet | `POST /v4/broadcasts`, `send_at` | Etiket/segment (`subscriber_filter`) | Evet | **Hepsini karşılıyor** |
| Resend | 1.000 kişi, 3 segment | Evet | `scheduled_at` | Segment | **Form yok** | Formu bizim yazmamız gerekir; bu da web'e anahtar sokar |
| MailerLite | 250 abone, 2.500 e-posta/ay | **Gönderim yok** | — | Grup | ? | Elendi |
| EmailOctopus | 2.500 abone, 10.000 e-posta/ay | ? | **Kampanya endpoint'i yok** | ? | ? | Elendi |
| Brevo | Resmi sayfalar okunamadı | — | — | — | — | Doğrulanamadı; yedek aday |

Bedeli: ücretsiz planda e-postalarda Kit'in markası görünür. Kit'in forms sayfası double opt-in'i
"tüm formlar" için söylüyor, ama planı adıyla anmıyor. Hesap kurulurken (T0) ilk iş bu
doğrulanır. Ücretsiz planda yoksa çalışma durur ve sağlayıcı seçimine geri dönülür.

### 2. Zamanlama: merge anında Kit'e, saatinde Kit gönderir

GitHub cron'u dakikalarca gecikebilir ve yoğunlukta atlanabilir. Bu yüzden gönderimi GitHub değil,
**Kit'in kendi zamanlayıcısı** yapar:

- `newsletter-deliver.yml`, `main`'e `content/weekly/**` değiştiren her push'ta çalışır.
- `deliver` komutu `main`'deki her sayı dosyası için gönderim anını hesaplar: `sendDate` ve dilin
  saati, Europe/Istanbul. Gönderim anı gelecekteyse Kit'te o sayının broadcast'ini **upsert** eder:
  yoksa oluşturur, varsa ve henüz gitmediyse içeriğini günceller. Her ikisinde de `send_at`
  gönderim anıdır.
- **Emniyet:** pazartesi 04:30 ve 11:30 UTC'de (her gönderimden 30 dakika önce) aynı komut cron
  ile yeniden çalışır. Push tetiklemesi bir kez başarısız olduysa (ör. Kit geçici olarak yanıt
  vermediyse) bu çalışma toparlar. Durum zaten doğruysa hiçbir şey değişmez.
- Merge gönderim anından **2 dakikadan az** önce veya gönderim anından sonra olduysa, o dil
  gönderilmez ve özette "geç merge, bu hafta gitmedi" yazar (map kesim kuralı).

### 3. `main` gerçeğin kaynağıdır (upsert kuralları)

Her broadcast'in iç açıklama alanında (`description`, abone görmez) bir işaret bulunur:
`thebluesland-weekly:2026-W47:tr`. Komut, Kit'teki broadcast'leri bu işarete göre eşleştirir.

| `main`'de dosya | Kit'te broadcast | Gönderim anı | Ne yapılır |
|---|---|---|---|
| var | yok | gelecekte (≥ 2 dk) | oluştur, `send_at` ile zamanla |
| var | var, gitmemiş | gelecekte | içerik farklıysa güncelle, aynıysa dokunma |
| var | var, gitmiş | — | dokunma; sonraki düzeltme yalnızca web arşivine yansır |
| var | yok | geçmişte veya < 2 dk | **gönderme**; özette uyarı |
| yok (revert/silme) | var, gitmemiş | gelecekte | broadcast'i sil (gönderim iptal) |

Aynı işaretle iki broadcast bulunursa komut hiçbir şey yapmaz ve başarısız olur (çift gönderim
riski). Mehmet'in Kit panelinde bakması gerekir.

### 4. E-postanın içeriği

- **Konu:** front matter'daki `title` (ör. "TheBluesland Weekly · 47. hafta").
- **Önizleme metni:** girişin ilk cümlesi.
- **Gövde:** front matter çıkarılmış Markdown, Markdig ile HTML'e çevrilir. Markdig repoda zaten
  merkezi paket olarak var; aracın `.csproj`'una yalnızca referansı eklenir, yeni paket yoktur.
  Ham HTML kapalıdır (`DisableHtml`). Linkler yalnızca `https://` olabilir. Satır içi stil sade
  tutulur; Kit kendi şablonunu ve abonelikten çıkma bağlantılı alt bilgisini ekler.
- AI notu sayının içindedir ve aynen gider (ADR-0006 madde 4). Komut, notun gövdede bulunduğunu
  ayrıca doğrular; yoksa göndermez. `validate-weekly` bunu zaten CI'da zorluyor.
- `public: false`: Kit'in web arşivi kullanılmaz; arşiv `/weekly`'dir (weekly-archive).

### 5. Yapılandırma ve secret

```yaml
# tools/weekly-newsletter/delivery.yml  (gizli değil; repoda)
timeZone: Europe/Istanbul
languages:
  tr: { sendTime: "08:00", kitTagId: 0000000 }
  en: { sendTime: "15:00", kitTagId: 0000000 }
minimumLeadMinutes: 2
```

- Tek secret `KIT_API_KEY`'dir (Kit v4 API anahtarı, `X-Kit-Api-Key` header'ı). Yalnızca
  `newsletter-deliver.yml`'in Deliver adımına verilir. Render'a ve web uygulamasına hiç girmez.
- `newsletter-deliver.yml`: `contents: read`. Gemini ve Neon secret'ı yoktur, repoya yazmaz.
  `concurrency: newsletter-deliver` ile çalışmalar sırayla koşar. Komut çıktısı stop-commands
  içindedir.
- İzolasyon testleri genişler: `KIT_API_KEY` yalnızca bu workflow'da bulunur; bu workflow başka
  secret tutmaz; `src/` altında Kit'e dair hiçbir referans yoktur.

### 6. `deliver` alt komutu

```
dotnet run --project tools/weekly-newsletter -- deliver [--content content/weekly] [--dry-run]
```

- Akış: `delivery.yml` okunur → `validate-weekly` kuralları uygulanır (bozuk dosya varsa hiçbir şey
  yapılmaz) → Kit'teki işaretli broadcast'ler listelenir → §3 tablosu uygulanır → özet basılır.
- `--dry-run` Kit'e yazmaz; ne yapacağını söyler. Elle tetiklemede varsayılan budur.
- Özet tablosu: her sayı ve dil için durum (zamanlandı, güncellendi, değişmedi, gitti, geç merge,
  iptal edildi), gönderim anı ve Kit broadcast id'si.
- Çıkış kodu 0'dır; "geç merge" de 0'dır, çünkü bu bir karardır, hata değildir. Yapılandırma eksikse,
  Kit hata verdiyse (yeniden denemeden sonra) veya çift broadcast bulunduysa 1'dir.

## Kod dışı kurulum (Mehmet, bir kez — T0)

1. Kit hesabı (ücretsiz). Ayarlarda gönderici adı "TheBluesland Weekly" girilir. Kit, alt bilgide
   zorunlu olarak bir posta adresi ister.
2. `thebluesland.com` için Kit'in verdiği gönderici doğrulama DNS kayıtları (SPF/DKIM) eklenir.
3. `weekly-tr` ve `weekly-en` etiketleri oluşturulur, id'leri `delivery.yml`'e yazılır.
4. İki form (TR, EN) açılır. Double opt-in açık olur ve her form kendi dil etiketini verir. Formların
   gömme kodu weekly-archive'a gider.
5. API anahtarı → GitHub secret `KIT_API_KEY`.

Ben (ajan) yalnızca 3'teki id'leri ve 5'teki secret'ın varlığını doğrularım. Hesap açma, DNS ve
yasal metin Mehmet'indir.

## Tech Stack

.NET 10, mevcut paketler (YamlDotNet, System.Text.Json, Markdig referansı). Yeni paket, tablo ve
migration yok.

## Project Structure

```
tools/weekly-newsletter/delivery.yml
tools/weekly-newsletter/Delivery/{DeliveryOptions, DeliveryOptionsLoader, IssueEmailRenderer,
  KitClient, DeliveryPlanner, DeliverCommand, DeliveryReportFormatter}.cs
tests/TheBluesland.UnitTests/WeeklyNewsletter/Delivery/*Tests.cs
tests/TheBluesland.UnitTests/Workflows/NewsletterDeliverWorkflowTests.cs
.github/workflows/newsletter-deliver.yml
```

`DeliveryPlanner.Plan(issues, broadcasts, now, options)` saf ve deterministiktir; §3 tablosunun
tamamı burada test edilir. I/O yalnızca `KitClient` ve `DeliverCommand`'dadır.

## Testing Strategy

Gerçek Kit çağrısı testte **yapılmaz**; `HttpMessageHandler` sahte yanıt verir.

- **Planlayıcı:** §3 tablosunun her satırı; tam 2 dakika sınırı; Istanbul saati; yıl dönümündeki
  hafta; çift broadcast; TR geç kalsa da EN'in zamanlanması.
- **Renderer:** başlık, linkler, AI notu; ham HTML'in kaçışlanması; `javascript:` gibi linklerin
  düşmesi; front matter'ın gövdeye sızmaması; W46 örnek sayısının HTML'i (snapshot değil, öğe
  kontrolü).
- **KitClient:** header'daki anahtar, URL'de anahtar yok; `send_at` ofsetli ISO 8601; etiket filtresi;
  429/5xx'te yeniden deneme; hata mesajında anahtar yok.
- **Komut:** sahte Kit ile uçtan uca; `--dry-run` hiçbir POST/PUT/DELETE yapmaz; geçersiz sayı
  dosyasında hiçbir çağrı yapılmaz.
- **Workflow:** tetikleyiciler (`push` paths, iki cron, dispatch), `contents: read`, tek secret ve
  tek adım, input'ların script'e enterpolasyonla girmemesi.

## Boundaries

- **Always:** yalnızca `main`'deki doğrulanmış dosya gönderilir; her gönderim bir işaretle
  eşleştirilir; geç merge gönderilmez ve görünür olur.
- **Ask first:** ücretli Kit planı; sağlayıcı değişikliği; gönderim saatlerini değiştirmek; abonelere
  sayı dışında bir e-posta göndermek; Kit'in web arşivini açmak.
- **Never:** abone verisini okumak, saklamak veya loglamak; `KIT_API_KEY`'i web uygulamasına veya
  Render'a koymak; geç kalmış bir sayıyı sonradan otomatik göndermek; gitmiş bir broadcast'i
  değiştirmeye çalışmak.

## Success Criteria

- [ ] Kurulum doğrulandı: double opt-in ücretsiz planda açık; alan adı doğrulandı; iki etiket var.
- [ ] İlk gerçek sayı TR'de pazartesi 08:00'de, EN'de 15:00'te, yalnızca kendi etiketine ve bir kez
      gitti.
- [ ] Merge'den sonra `main`'de yapılan bir düzeltme gönderimden önce broadcast'e yansıdı (deneme
      haftasında `--dry-run` ve test aboneyle).
- [ ] İzolasyon testleri `KIT_API_KEY`'in yalnızca deliver workflow'unda olduğunu kanıtlıyor.
- [ ] Tam build ve test yeşil; yeni paket ve migration yok.

## Karar önerileri (onay bekliyor)

1. **Sağlayıcı: Kit ücretsiz planı** (§1). Bedeli Kit markasıdır. Yedek aday Brevo'dur; bugün
   doğrulanamadı.
2. **Zamanlamayı Kit yapar; merge anında upsert, gönderimden 30 dk önce emniyet çalışması** (§2).
   GitHub cron'unun gecikmesi gönderim saatini etkilemez.
3. **`main` gerçeğin kaynağıdır** (§3): gönderimden önceki düzeltme e-postaya yansır, revert
   gönderimi iptal eder, gittikten sonra hiçbir şeye dokunulmaz.
4. **Saatler ve etiket id'leri `delivery.yml`'de, tek secret `KIT_API_KEY`** (§5).
5. **Yasal metin Mehmet'in.** Kit ABD'de bir şirket; abonenin e-posta adresi yurt dışında işleniyor.
   TR formundaki açık rıza metnini ve KVKK'nın yurt dışına aktarım yükümlülüklerini Mehmet belirler
   (gerekirse bir uzmana danışır). Bu spec hukuki görüş değildir. Gönderim kodu bu karardan
   bağımsızdır, ama ilk gerçek gönderim bu metin formda yer almadan yapılmaz.

## Open Questions

- Kit hesabının kurulumu (T0) Mehmet'tedir. Kurulum bitmeden T1–T5 sahte Kit ile yazılabilir ve
  test edilebilir, ama success criteria'nın ilk iki maddesi kurulumu bekler.
