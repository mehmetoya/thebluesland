# Otomatik dönem ataması (US-023)

Mehmet'in 2026-09-07 kararıyla dönem ataması tek tek onay gerektirmez. Normal aylık sync,
sanatçıları topladığı `/items` sayfalarında `album.release_date` alanını da ister. Ek bir
Spotify çağrısı veya yeni credential gerekmez. Track tarihleri bellekte hesaplanıp atılır;
veritabanına yalnız playlist seviyesinde `computed_eras` etiket dizisi yazılır.

- Tarihi okunabilen en az 10 parça gerekir; daha azında hesaplanan dizi boşaltılır.
- Tarihli parçaların en az %20'sini kapsayan her dönem atanır.
- Hiçbir dönem %60'a ulaşmıyorsa `mixed-era` de atanır.
- Web yalnızca editoryal `eras: [mixed-era]` listesini hesaplanan etiketlerle değiştirir.
  Belirli dönemler veya çoklu dönemler elle atanmışsa bunlar önceliklidir.
- Eksik, yetersiz veya kullanılamayan cache durumunda Markdown etiketleri kullanılır.
- Site dönemleri beş dakika boyunca bellekte tutar; yenileme tek toplu sorgudur.
  Sync sonrası yeniden deploy gerekmez. Liste, filtre, detay ve ilgili playlist'ler aynı
  dönemleri kullanır. `/health/ready` veritabanından bağımsız kalır.

Spotify albüm tarihi yeniden basım tarihini gösterebilir; bu, özgün kayıt dönemi garantisi
değildir. İstisnaları düzeltmek için Markdown'a belirli dönemleri yazmak yeterlidir.
`report-eras` karşılaştırmalı salt-okunur rapor üretmeye devam eder.

## İlk devreye alma

1. `AddComputedPlaylistEras` migration'ını **tablo sahibi/migration yetkili bağlantıyla** uygulayın:
   `dotnet ef database update --project src/TheBluesland.Data/TheBluesland.Data.csproj`
   için bağlantı override'ını güvenli yerel ortamınızdan sağlayın. Design-time factory'nin
   varsayılan bağlantısı yalnız localhost içindir. Production şifresini komut geçmişine yazmayın.
   Alternatif olarak EF'nin ürettiği idempotent SQL dosyasını Neon SQL Editor'de çalıştırın:

   ```sh
   dotnet ef migrations script --idempotent --project src/TheBluesland.Data/TheBluesland.Data.csproj --output /tmp/thebluesland-migrations.sql
   ```

   Migration nullable `computed_eras text[]` sütunu ekler; mevcut veriyi değiştirmez.
   Sync rolü yalnız SELECT/INSERT/UPDATE yetkilidir ve migration çalıştırmamalıdır.
2. Yeni uygulama sürümünü deploy edin. Migration'ı önce uygulamak eski sürümle uyumludur.
3. GitHub Actions → **Sync Spotify playlist cache** → güncel dal → `mode: sync` çalıştırın.
   Mevcut Spotify ve Neon sync secret'ları kullanılır. Başarılı playlist'ler tek tek kaydedilir;
   kesinti olursa normal sync yeniden çalıştırılabilir.
4. Tamamlandıktan sonra beş dakika içinde dönem filtrelerini kontrol edin. Her playlist'in
   belirli bir döneme dönüşmesi beklenmez: dağılım karışıksa `mixed-era` kalır, veri yetersizse
   editoryal etiket korunur.

Kod testleri sahte Spotify yanıtları ve geçici PostgreSQL kullanır. Gerçek 85 playlist'in
sonuçları ilk production sync tamamlanmadan doğrulanmış sayılmaz.

## Uzayan Spotify işleri

`report-eras.yml` ve `sync-spotify.yml` aynı `spotify-api` concurrency grubunu kullanır;
aynı anda Spotify çağrısı yapmazlar. Her job en fazla 30 dakika çalışır.
İstemci en fazla 5 deneme yapar. Tek bir `Retry-After` iki dakikayı aşıyorsa beklemek veya
Spotify'ın süresinden önce tekrar denemek yerine 429 hatasıyla durur. Logdaki cooldown
geçmeden yeni çalışma başlatmayın. Daha kısa beklemeler ve playlist ilerlemesi stderr'e yazılır.
Bu sınırlar rate limit'i kaldırmaz; saatlerce sessiz beklemeyi önler.
