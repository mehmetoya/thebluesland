# Test Kuralları

## Sorumluluk sınırı
- **Kodu değiştiren ajan**, değiştirdiği davranışın minimum testini de yazar.
  Bug fix'e regresyon testi eşlik eder. Bu pazarlığa açık değildir.
- **Test uzmanı ajan** sınır durumlarını, integration testlerini, test altyapısını
  ve kırık suite teşhisini üstlenir.
- Test uzmanı testi geçirmek için üretim kodunu değiştirmez; hatayı raporlar.

## Yazım
- Ad: `Metot_Durum_BeklenenSonuc`.
- Arrange-Act-Assert ayrımı görünür olsun.
- Bir testte tek davranış doğrula.
- Projede kurulu assertion kütüphanesini kullan.
- Birim testte DB yok. Integration testte veritabanı container ile ayağa kalkar.
- Kırılgan test yazma: gerçek saat, sıralama varsayımı, gerçek ağ çağrısı yok.
- Kapsam yüzdesi kovalama; riskli ve kırılgan yolu test et.
