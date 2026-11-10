# Spec: story-ranking

Modül: `story-ranking` · Map: [weekly-newsletter.md](weekly-newsletter.md) · Önceki modül:
[story-clustering](weekly-newsletter-story-clustering.md) · Durum: UYGULANDI — 2026-11-10 (W46: 5/5 Mehmet'in seçimiyle aynı; PR bekliyor)

## Assumptions

1. story-clustering gibi bir kütüphane bileşenidir ve bellekte çalışır. Girdisi bir pencerenin
   `Story` listesidir, çıktısı sıralı bir seçimdir. DB'ye yazmaz, migration yoktur.
2. Seçim deterministiktir ve AI kullanmaz. "Neden önemli" metnini issue-drafting yazar; bu modül
   yalnızca hangi 5 hikâyenin gireceğine ve **neden seçildiğine** (puan dökümü) karar verir.
3. Seçim mükemmel olmak zorunda değil: Mehmet PR'da her hikâyeyi 3 yedekten biriyle
   değiştirebilir (map: issue-review). Hedef, Mehmet'in çoğu hafta hiçbir şeyi değiştirmemesidir.
4. Kaynak sayısı 7 olduğu için kapsam tek başına zayıf bir sinyaldir. Bu hafta 12 hikâyeyi 2
   kaynak yazmış; aralarındaki farkı ancak "önem" sinyali açar.
5. **Kaynak dengesi (PR-3, W47 editoryal inceleme):** ilk 7 kaynak ABD/UK pop-indie-sektör medyasıydı;
   marka (blues/rock kökenli katalog) küresel müzik haberi vaat ediyor. 9 kaynak eklendi (blues,
   klasik rock, rock/metal, caz, şarkı yazarlığı, Guardian, NPR, Quietus). Niş kaynaklar kapsam
   sayısında hep kaybeder; bu yüzden §1'de marka uyumu bonusu var.

## Objective

Bir haftanın hikâyelerinden TheBluesland Weekly'nin 5 haberini ve 3 yedeğini seçmek. Taban
kapsamdır (kaç bağımsız yayın yazdı), üstüne önem eklenir, çeşitlilik kuralları uygulanır. 5'ten
biri blues/rock/soul/indie *olabilir*, ama global gündem önceliklidir (map: onaylı niyet).

- issue-drafting olarak: sıralı 5 hikâyeyi ve her birinin neden seçildiğini almak istiyorum.
- Mehmet olarak: PR'da yedekleri ve her seçimin puan dökümünü görmek istiyorum. Bir hikâyeyi
  değiştirmem gerekirse nedenini anlamalıyım.

## Design

### 1. Puan

```
puan = kapsam + önem + marka
kapsam = farklı kaynak sayısı × 1.0
önem   = hikâyenin türüne göre sabit ek puan (ranking.yml)
marka  = brandBonus (0.75), en az brandMinSources (2) kaynak VE (marka kelimesi VEYA çekirdek kaynak); tek sefer
```

**Marka uyumu.** `brandKeywords` (blues, rock, guitar, jazz, soul, americana, classic rock…) hikâyenin
herhangi bir başlığında/özetinde tam kelime olarak geçerse ya da `coreSources` (american-blues-scene,
blues-rock-review, ultimate-classic-rock, louder, jazztimes) içinden bir kaynak hikâyeyi yazdıysa
hikâye `brandBonus` alır; birikmez, roundup'lar almaz. Bonus yalnız en az `brandMinSources` (2)
kaynaklı hikâyeye verilir. Etkisi: 2 kaynaklı blues/rock haberi (2 + 0.75 = 2.75) 2 kaynaklı
marka-dışı haberi geçer ama 3 kaynaklı büyük haberi (3.0) geçmez. 2026-11-10 tarihli düzeltme: ilk
sürümde bonus 1.25 ve tek kaynaklılara da açıktı; önizlemede tek bir sitenin (Louder) tek kaynaklı
metal haberleri haftanın çok kaynaklı haberlerini ilk 5'ten itti. Çekirdek liste feeds.yml'de değil
ranking.yml'dedir (`StoryRanker.Rank` imzası değişmesin diye); bir test her `coreSources` id'sinin
`feeds.yml`'de etkin olduğunu doğrular. Rapor "Why" sütunu `· brand +0.75` gösterir. Aynı düzeltmede
"death" tek başına ölüm sayılmaz ("since X's death" yeniden birleşme haberidir), "of the week" ve
"Blues Rock Weekly" roundup sayılır, "reform/reforming" milestone'dur.

**Tür**, hikâyenin tüm üye başlıklarında aranan anahtar kelimelerle belirlenir. Yukarıdan aşağı ilk
eşleşen tür kazanır. Kelime listeleri ve ağırlıklar `ranking.yml`'dedir; kod değişmeden ayarlanır.

| Tür | Örnek kelimeler | Ek puan | Gerekçe |
|---|---|---:|---|
| roundup | "best songs", "new albums you should", "selects playlist", "weekly round-up", "the number ones" | hariç | Editoryal liste, haber değil |
| obituary | dies, died, dead, death (ama "near-death" değil) | +1.5 | Kayıp haberi gündemin en net sinyali |
| legal | lawsuit, sues, court, trial, judge, charged, breach, settlement, injunction, infringement, contract dispute | +1.0 | Sektörü ve sanatçıları etkiler. "contract" tek başına **yok**: "record contract" gibi iş haberleri yasal sayılmaz |
| industry | charts, streaming, royalties, acquires, deal, earnings, quarter, label | +1.0 | "Neden önemli" açısı en güçlü tür |
| milestone | diagnosis, cancels, hiatus, reunite, farewell, final show, hall of fame | +0.75 | Kariyer dönüm noktası |
| videogame | gta, grand theft auto, video game | 0 | Müzik dışı lansman: "festival/concert" geçse de `live` bonusu almaz (live'dan önce denenir) |
| live | tour, residency, festival, headline | +0.25 | Haftada çok sayıda; ayırt edici değil |
| release | (eşleşme yoksa) | 0 | Tekil şarkı/video haberleri |

Eşitlikte sırası: daha çok üye haber, sonra daha yeni `LastSeen`, sonra story key.

### 2. Çeşitlilik kuralları (puan sırasıyla açgözlü seçim)

1. Aynı türden en fazla 2 hikâye girer. Haftada 6 tur duyurusu olsa bile en fazla 2'si girer.
2. **İlişkili hikâyeden tek biri girer.** İki hikâyenin TF-IDF merkezleri arasındaki kosinüs
   ≥ `relatedThreshold` (başlangıç 0.15) ise yalnızca puanı yüksek olan alınır. Örnek: "Robbie
   Williams – Olivia Rodrigo" ve "Christmas Wrapping – Olivia Rodrigo" aynı tartışmanın iki
   hikâyesidir.
3. **Tür tercihi slotu (isteğe bağlı, kota değil).** İlk 5'te blues/rock/soul/indie eşleşmesi
   (başlık ve özet kelimeleri; liste `ranking.yml`'de. Feed kategorileri kümeleme girdisinde
   olmadığı için kullanılmıyor) yoksa, eşleşen en iyi aday
   5. sıradakinin yerine geçer. Bunun koşulu, adayın puanının 5. sıradakinin en az %70'i olmasıdır
   (`genreSwapMinRatio`). Eşleşen aday yoksa veya puanı düşükse hiçbir şey değişmez.
4. Yedekler 6.–8. sıradan seçilir: seçilen her şeyle ilişkili olmamalı, tür sınırı yedeklerin kendi içinde uygulanır. Tür slotuyla yerinden çıkan hikâye doğal olarak ilk yedek olur.

### 2b. Bilinen boşluk: geçmiş etkinlik cezası

Gönderim gününden önce bitmiş etkinlikler (ör. cumartesi biten ödül töreni) ceza almıyor. Ucuz ve
sağlam bir çözüm yok: `StoryRanker.Rank` gönderim tarihini bilmiyor (imza Drafting/Review'da da
kullanılıyor) ve özetlerdeki tarihler serbest metin ("last night", "Saturday"). Anahtar kelimeyle
("ceremony", "awards") ceza vermek yaklaşan törenleri de cezalandırırdı. Karar: ranking'de
yapılmıyor; issue-drafting prompt'una eklenen geçmiş zaman kuralı ve Mehmet'in PR incelemesi
karşılıyor. Gerekirse sonraki adım: `Rank`'e gönderim tarihi + yalnızca "tonight/last night/on
<weekday>" kalıpları.

### 3. Gerçek haftada deneme (2026-W46, prototip)

| # | Hikâye | Puan dökümü |
|---:|---|---|
| 1 | Richard Lloyd (Television) öldü | 3 kaynak + obituary 1.5 = 4.50 |
| 2 | YouTube yayınları yeniden Billboard listelerinde | 3 + industry 1.0 = 4.00 |
| 3 | Oasis Hall of Fame törenine katılmayacak | 3 + milestone 0.75 = 3.75 |
| 4 | GTA VI müzik festivali | 3 + live 0.25 = 3.25 |
| 5 | Strokes SNL'de, Spitting Image klibi | 3 + release 0 = 3.00 |
| Y1 | NewJeans sözleşmeleri / HYBE yanıtı | 2 + legal 1.0 = 3.00 |
| Y2 | McCartney & Ringo, VetsAid'de birlikte | 2 + milestone 0.75 = 2.75 |
| Y3 | Juan Atkins'in kataloğu ölümünden sonra %1.285 arttı | 1 + obituary 1.5 = 2.50 |

**PR-3 sonrası W46 (marka bonusu + legal düzeltmesi + videogame, kaynaklar hâlâ 7):**

| # | Hikâye | Puan dökümü |
|---:|---|---|
| 1 | Richard Lloyd (Television) öldü | 3 + obituary 1.5 + brand 1.25 = 5.75 |
| 2 | Oasis Hall of Fame törenine katılmayacak | 3 + milestone 0.75 + brand 1.25 = 5.00 |
| 3 | YouTube yayınları yeniden Billboard listelerinde | 3 + industry 1.0 = 4.00 |
| 4 | Del Casher (gitar wah-wah pedalı mucidi) öldü | 1 + obituary 1.5 + brand 1.25 = 3.75 |
| 5 | GTA VI müzik festivali | 3 + videogame 0 = 3.00 |
| Y1 | Strokes SNL'de | 3.00 |
| Y2 | McCartney & Ringo, VetsAid | 2.75 (W46 verisinde Louder yok; Louder yazsaydı çekirdek bonusu +1.25 alırdı) |
| Y3 | Juan Atkins kataloğu | 2.50 |

Gold set bilinçli sonuç: Mehmet'in 5'inin 4'ü ilk 5'te, 5'incisi (Strokes SNL) ilk yedek; 5/5 ilk 8'de.
Kabul ölçütü (≥4/5, hepsi ilk 8'de) sağlanıyor. Gold set dosyası değiştirilmedi. Fark nedeni: gitar
alet mucidi ölüm haberi (tek kaynak, ama obituary + marka) Strokes'u (3 kaynak, bonus yok) geçiyor;
marka bonusunun tasarım gereği yaptığı şey bu. NewJeans sözleşme haberi artık `legal` değil (Y1
listesinden düştü); GTA VI 3.25'ten 3.0'a indi ama ilk 5'te kalıyor.

- Tür slotu bu hafta devreye girmiyor: ilk 5'te zaten Richard Lloyd (gitarist) ve Oasis var.
- **Bilinen zayıflık:** anahtar kelime yaklaşımı "Near-Death" geçen Darius Rucker belgesel haberini
  ölüm haberi sandı. Kelime listesi bunu dışlayacak; benzer hatalar için emniyet Mehmet'in
  yedekle değiştirmesi.

### 4. Ölçüm — Mehmet'in seçimi gold set olur

Seçim zevk işidir; doğru cevabı Mehmet verir. T1'de W46'nın en yüksek puanlı 15 hikâyesi Mehmet'e
gösterilir. Mehmet "ben olsam bu 5'i seçerdim" der; bu seçim `Fixtures/newsletter-ranking/2026-W46.json`
olarak kaydedilir.

- **Kabul:** algoritmanın ilk 5'i Mehmet'in 5'iyle **en az 4 hikâyede** örtüşür. Mehmet'in 5'i,
  algoritmanın ilk 8'inin (5 + 3 yedek) içindedir.
- Her yeni haftada Mehmet'in PR'daki değişiklikleri yeni gold set olur. Bu, issue-review
  modülünde otomatikleşir.

### 5. Okunur rapor — `rank` alt komutu

```
dotnet run --project tools/weekly-newsletter -- rank [--from …] [--to …]
```

Çıktı Markdown'dır; `newsletter-preview.yml`'e `stage: cluster | rank` girdisi eklenir:

```
## Weekly pick · 2026-11-03 → 2026-11-10 UTC

102 stories · 5 picked + 3 backups · genre slot: not needed (Richard Lloyd, Oasis)

| # | Story | Sources | Why |
|---:|---|---|---|
| 1 | Richard Lloyd, Television Guitarist, Dies at 75 | Pitchfork · Stereogum · NME | 3 sources · obituary +1.5 = 4.50 |
…
| B1 | NewJeans’ Contracts Removed Key Protections… | Pitchfork · MBW | 2 sources · legal +1.0 = 3.00 |

<details><summary>Skipped by diversity rules (n)</summary> … neden: "live: already 2", "related to #…" </details>
```

Aynı rapor ileride bot PR'ının açıklamasına girer (issue-review).

## Tech Stack · Commands · Project Structure

story-clustering ile aynı. Yeni paket ve migration yok.

```
tools/weekly-newsletter/ranking.yml
tests/TheBluesland.UnitTests/WeeklyNewsletter/Ranking/RankingConfigTests.cs   # legal/videogame/brand/coreSources
tools/weekly-newsletter/Ranking/{RankingOptions, RankingOptionsLoader, StoryTypeClassifier, StoryRanker, RankCommand, RankReportFormatter}.cs
tests/TheBluesland.UnitTests/WeeklyNewsletter/Ranking/*Tests.cs
tests/TheBluesland.UnitTests/Fixtures/newsletter-ranking/2026-W46.json
```

`StoryRanker.Rank(stories, options)` saf ve deterministiktir. I/O yalnızca `RankCommand`'dadır;
`RankCommand` pencereyi `ClusterCommand` ile aynı şekilde okur (ortak okuyucuya çıkarılır).

## Testing Strategy

- **Sınıflandırıcı:** her tür için örnek; ilk eşleşen kazanır; "near-death" obituary değil;
  roundup hariç.
- **Ranker:**
  - puan ve eşitlik bozma;
  - tür başına en fazla 2;
  - ilişkili hikâyelerden tek biri;
  - tür slotu: devreye girer, girmez, eşik altında kalan aday;
  - 8'den az hikâye olan hafta (5 + kalan kadar yedek);
  - boş hafta;
  - determinizm.
- **Gold set:** `2026-W46.json` üzerinde §4 kabul ölçütü.
- **Rapor ve komut:** story-clustering testleriyle aynı desen (escape, boş pencere, Testcontainers).

## Boundaries

- **Always:** saf ve deterministik çekirdek; ağırlıklar ve kelime listeleri `ranking.yml`'de;
  her seçimin puan dökümü raporda görünür.
- **Ask first:** AI ile önem puanlamak; yeni sinyal kaynağı (engagement-signals = map'te "Sonra");
  tür tercihini kotaya çevirmek (map: zorunlu kota MVP dışı).
- **Never:** Spotify verisi; seçimi DB'ye yazmak; Mehmet'in gold set seçimini algoritmaya uysun
  diye değiştirmek.

## Success Criteria

- [x] W46 gold set: ilk 5'in en az 4'ü Mehmet'in 5'iyle aynı; Mehmet'in 5'i ilk 8'in içinde. (Sonuç: 5/5; yedekler de §3 tablosuyla aynı.)
- [x] Çeşitlilik kurallarının her biri testle kanıtlı.
- [ ] `newsletter-preview` workflow'u `stage: rank` ile production verisinde yeşil; rapor her
      seçimin nedenini gösteriyor.
- [ ] Tam build ve test yeşil; yeni paket ve migration yok.

## Kararlar (onaylandı, 2026-11-10)

1. **AI'sız, kurallı puanlama:** kapsam + tür ağırlığı, yukarıdaki tablo başlangıç değerleri.
2. **Çeşitlilik:** tür başına en fazla 2 ve ilişkili hikâyeden tek biri.
3. **Tür tercihi:** kota değil. İlk 5'te blues/rock/soul/indie hiç yoksa ve aday yeterince güçlüyse
   (%70) yalnızca 5. sıradakinin yerine geçer.

## Open Questions

- Yok. Mehmet W46 için §3 tablosundaki 5 hikâyeyi aynen seçti (2026-11-10); gold set bu.
