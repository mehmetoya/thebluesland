# TheBluesland — Product Owner Agent Brief

## Bağlam

Bu, mevcut bir personel projenin devamı — kod yazma aşamasına geçmeden önceki
araştırma/planlama sürecinin çıktısı. Amaç: `thebluesland-business-technical-specification.md`
(v0.1, ekte) dosyasını revize edip implementation'a hazır bir v0.2 spec + backlog
üretmen.

## Proje

Mehmet'in (Spotify kullanıcı adı: `thebluesland`) herkese açık Spotify
playlist'lerini sergileyeceği kişisel bir web sitesi. Ziyaretçiler playlist'leri
mood/genre/occasion/era'ya göre filtreleyebiliyor, her playlist'in bir küratör
notu ve Spotify embed'i var.

## v0.1 spec'ten değişen kararlar (KRİTİK — spec'i bunlara göre revize et)

v0.1 spec'i "embed-first, veritabanı yok, Spotify API yok" ilkesi üzerine
kuruluydu (ADR-001, ADR-003, ADR-005, SEC-001, madde 22 Definition of Done,
madde 11.2 non-goals). Bu karar **sahibi tarafından bilinçli olarak tersine
çevrildi**. Yeni temel mimari:

- Spotify Web API doğrudan entegre ediliyor.
- Spotify'dan çekilen veri (playlist adı, açıklama, kapak görseli, track
  sayısı, sanatçı listesi) kendi PostgreSQL veritabanında saklanıyor.
- Senkron ayda 1 kez çalışıyor (aylık cache/refresh döngüsü).
- Editoryal alanlar (moods, genres, occasions, era, curator note, slug) hâlâ
  git-versioned Markdown/YAML dosyalarında kalıyor — bunlar API'den
  **çekilmiyor**, tamamen elle yönetiliyor.
- Yani nihai mimari **hibrit**: Spotify-kaynaklı alanlar DB'de, editoryal
  alanlar content-as-code'da, ikisi `spotifyPlaylistId` üzerinden join
  ediliyor.

v0.1'deki şu maddeler artık geçersiz ve spec'te güncellenmeli:
- ADR-003 (content-as-code, DB yok) → hibrit modele göre revize et
- ADR-005 (no database) → DB var, gerekçesi "aylık senkron ile authoring
  yükünü azaltmak" olarak yeniden yazılmalı
- SEC-001 ("must not require Spotify client credentials") → artık senkron
  job'ın credential'a ihtiyacı var, ama bu credential **yalnızca CI'da**
  yaşıyor (aşağıya bak), production web app'de değil
- 11.2 non-goals'taki "independent permanent database" maddesi → bu hâlâ
  geçerli olmalı ama şu şekilde daraltılmalı: yasak olan şey **track
  listesinin** kalıcı saklanması; playlist adı/açıklama/kapak/track sayısı
  gibi metadata'nın aylık yenilenen bir cache olarak saklanması kabul edilebilir

## Değişmeyen, sabit kısıtlar (spec'ten çıkarılamaz)

1. **Spotify Developer Policy madde 14**: Spotify Content (metadata, track
   listesi, sanatçı verisi dahil) hiçbir şekilde bir AI/ML modeline "ingest"
   edilemez — bu eğitim değil, inference için bile geçerli, istisnası yok.
   Bu yüzden "AI analiz" özelliği hâlâ v0.1'deki gibi dar tutulmalı: AI
   yalnızca Mehmet'in kendi yazdığı curator note metnini düzenler/çevirir,
   playlist/track verisine dokunmaz.
2. **Sıfır hosting maliyeti**. Seçilen stack: Render (web hosting, free tier,
   cold start kabul edilebilir) + Neon (PostgreSQL, free tier) + GitHub
   Actions scheduled workflow (aylık Spotify senkronu, ücretsiz). Railway,
   Netlify, Vercel, Azure değerlendirildi ve reddedildi (sırasıyla: gerçek
   ücretsiz plan yok / .NET persistent server çalıştıramıyor / Blazor
   Interactive Server'ın gerektirdiği kalıcı SignalR bağlantısını
   desteklemiyor / Mehmet'in ödeme yapacak bütçesi yok).
3. Aylık senkron **bir GitHub Actions cron job'ı** olarak çalışıyor, uygulama
   içinde bir `BackgroundService` olarak değil — Render'ın free instance'ı
   sürekli ayakta kalmadığı için in-process scheduler güvenilmez. Spotify
   Client ID + refresh token + Neon connection string GitHub Actions
   repository secrets'ında tutuluyor; production web app'in hiçbir Spotify
   veya AI credential'ı yok.

## Kesinleşmiş teknoloji kararı

| Alan | Seçim |
|---|---|
| Uygulama | .NET 10, Blazor Web App |
| Rendering | Static SSR; filtreleme için Interactive Server |
| Veritabanı | PostgreSQL (Neon) + EF Core — yalnızca Spotify-kaynaklı alanlar |
| Editoryal içerik | Markdown/YAML, git-versioned (`content/playlists/*.md`) |
| Spotify entegrasyonu | Authorization Code + PKCE (Mehmet'in kendi hesabı), aylık GitHub Actions cron ile senkron |
| Stil | Tailwind CSS 4 |
| Test | xUnit + Shouldly, Playwright (e2e) |
| CI/CD | GitHub Actions |
| Hosting | Render (web) + Neon (Postgres), $0/ay |

## Elimizdeki girdiler

- v0.1 business/technical spec dosyası (ekli) — yukarıdaki maddeler dışında
  büyük ölçüde geçerli: FR/NFR listesi, taksonomi taslağı, sayfa haritası,
  test stratejisi, repo yapısı, güvenlik bölümü (CSP, embed URL doğrulama,
  Spotify kapak görselini kopyalamama, structured data'ya track listesi
  koymama) sağlam, korunmalı.
- Yerelde çalışan bir .NET console fetcher aracı: Spotify'a Authorization
  Code + PKCE ile bağlanıp playlist adı/açıklama/track listesini çekiyor
  (şu an authoring için elle çalıştırılıyor; senkron job'ı bunun otomatik/
  production versiyonu olacak).
- 2 örnek playlist incelendi: "Masterpieces of Erkin the Father" (Erkin
  Koray, Anadolu rock — v0.1'deki genre sözlüğünde karşılığı yok) ve "Dear
  Mr. Fantasy" (Clapton/Winwood/Traffic, blues rock).
- Site adı kesinleşti: **TheBluesland**.

## Product Owner'dan istenen çıktı

1. v0.1 spec'i yukarıdaki hibrit mimariye göre revize edilmiş bir v0.2 olarak
   yeniden yaz (FR/NFR listesi, ADR'ler, repo yapısı, Definition of Done).
2. Aşağıdaki açık kararları ya çöz ya da implementation'ı bloklayan net
   maddeler olarak spec'e işle:
   - **Site dili**: Türkçe mi, İngilizce mi, yoksa ikisi mi? (curator note
     tonu ve site adının okunuşu buna bağlı)
   - **Taksonomi genişliği**: mevcut taslak 10 mood × 11 genre × 10 occasion
     × 9 era, ama launch hedefi 8 playlist — çoğu filtre kombinasyonu boş
     dönecek. Launch için her boyutta 4-5 değere indirilmesi öneriliyor.
   - **Genre sözlüğü boşluğu**: "Anadolu rock" gibi bir tag yok, ama elde
     bunu gerektiren en az 1 playlist var.
   - **Domain adı**: henüz seçilmedi, Render'ın verdiği ücretsiz subdomain ile
     başlanabilir.
3. Yukarıdaki kararlara dayanan bir backlog/user story kırılımı (DB şeması,
   senkron job, content validation, Blazor scaffold, CI pipeline, deploy
   sırasıyla).
4. Repo yapısına fetcher aracının (`tools/spotify-playlist-fetcher/`) ve
   content publication workflow'una senkron job adımının eklenmesi.
