# TheBluesland Proje Sözleşmesi

Her oturumda yüklenen kısa ve güncel proje bağlamıdır. Genel C# kuralları
`dotnet-engineering-standards` skill'indedir; ayrıntılı kararlar `docs/adr/` altındadır.

## Mevcut mimari

- .NET 10 / C# 14; nullable, implicit usings ve `TreatWarningsAsErrors` açık.
- `src/TheBluesland.Web`: Blazor Web App; static SSR, yalnız gerektiğinde isolated
  Interactive Server. İçerik okuma, doğrulama ve web sunumu burada.
- `src/TheBluesland.Data`: EF Core/PostgreSQL şeması ve migration'lar. Web salt okunur,
  senkron aracı yazma yetkili bağlantı kullanır.
- `tools/spotify-playlist-fetcher`: GitHub Actions'ta aylık çalışan Spotify senkron aracı.
- `content/playlists`: version-controlled editoryal playlist içeriği.
- `tests/TheBluesland.UnitTests`: birim, şema ve web integration testleri.
- `.github/workflows`: doğrulama, senkron ve diğer otomasyonlar.

MVP'de ayrı API, Domain, Shared, Ui.Shared veya Mobile projesi yoktur. İkinci bir
istemci gerçek ihtiyaç hâline gelmeden bu katmanları oluşturma. Mimari gerekçe için
ADR-0002 ve ADR-0003'e bak.

## Sabit sınırlar

- Editoryal veri Markdown/YAML'de; Spotify kaynaklı playlist özeti PostgreSQL cache'te.
- Track listesi kalıcı saklanmaz. Spotify/AI credential'ları production web ortamına girmez.
- Web cache'e graceful degradation ile erişir; eksik veya bayat cache siteyi çökertmez.
- Yeni proje, katman, repository sarmalayıcı veya paket ancak somut ihtiyaç varsa eklenir.
- MediatR ve AutoMapper eklenmez.
- Mevcut desen ve kurulu kütüphaneler tercih edilir; yeni paket için kullanıcıdan onay alınır.

## Çalışma şekli

- Yalnız görevle ilgili dosyaları ve gerekirse ilgili ADR/spec bölümünü oku.
- Davranış değişikliğine minimum test ekle; bug fix'e regresyon testi ekle.
- Önce ilgili testleri çalıştır. Tam `dotnet build` ve `dotnet test` teslimden önce bir kez
  çalıştırılır; aynı görev zincirindeki agentlar sonucu geçerliyse tekrarlamaz.
- Kullanıcının mevcut çalışma ağacı değişikliklerini koru.

## Agent bütçesi

- Normal feature, bug fix ve küçük refactor ana oturumda veya yalnız `backend-dev` ile tamamlanır.
- Varsayılan olarak subagent çağırma. Bağımsız uzmanlık gerçekten gerekiyorsa en fazla bir
  specialist çağır; agent zinciri kurma ve specialist'ın başka agent çağırmasına izin verme.
- Her specialist kendi işini bitirir ve durur. Sonraki bir role ihtiyaç varsa o agentı çağırmaz;
  kullanıcıya `Önerilen devir: <rol> — <somut görev>` biçiminde bildirir. Devri kullanıcı başlatır.
- Model seviyesi role göre seçilebilir; mimari karar kalitesi için `architect` Opus kullanır.
- `architect` yalnız kullanıcı mimari seçenek/ADR istediğinde; `code-reviewer` yalnız açık review
  veya PR öncesi talebinde; `test-engineer` yalnız test altyapısı, kırık suite ya da kapsamlı
  integration testi istendiğinde kullanılır.
- Ürün netleştirme ve iş planlama ayrı context açmaz: kullanıcı istediğinde ana konuşmada
  `refine-story` ve `plan-work` skill'leri kullanılır.
- Specialist çıktısı en fazla 8 satırlık `Durum / Kanıt / Kalan risk / Önerilen devir /
  Başlatma komutu` sözleşmesiyle kapanır; log veya diff devre taşınmaz.
