# Spec: story-ranking

Modül: `story-ranking` · Map: [weekly-newsletter.md](weekly-newsletter.md) · Önceki modül:
[story-clustering](weekly-newsletter-story-clustering.md) · Durum: ONAYLANDI — Mehmet, 2026-11-10 (3 karar + W46 gold set)

## Assumptions

1. story-clustering gibi bir kütüphane bileşenidir ve bellekte çalışır. Girdisi bir pencerenin
   `Story` listesidir, çıktısı sıralı bir seçimdir. DB'ye yazmaz, migration yoktur.
2. Seçim deterministiktir ve AI kullanmaz. "Neden önemli" metnini issue-drafting yazar; bu modül
   yalnızca hangi 5 hikâyenin gireceğine ve **neden seçildiğine** (puan dökümü) karar verir.
3. Seçim mükemmel olmak zorunda değil: Mehmet PR'da her hikâyeyi 3 yedekten biriyle
   değiştirebilir (map: issue-review). Hedef, Mehmet'in çoğu hafta hiçbir şeyi değiştirmemesidir.
4. Kaynak sayısı 7 olduğu için kapsam tek başına zayıf bir sinyaldir. Bu hafta 12 hikâyeyi 2
   kaynak yazmış; aralarındaki farkı ancak "önem" sinyali açar.

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
puan = kapsam + önem
kapsam = farklı kaynak sayısı × 1.0
önem   = hikâyenin türüne göre sabit ek puan (ranking.yml)
```

**Tür**, hikâyenin tüm üye başlıklarında aranan anahtar kelimelerle belirlenir. Yukarıdan aşağı ilk
eşleşen tür kazanır. Kelime listeleri ve ağırlıklar `ranking.yml`'dedir; kod değişmeden ayarlanır.

| Tür | Örnek kelimeler | Ek puan | Gerekçe |
|---|---|---:|---|
| roundup | "best songs", "new albums you should", "selects playlist", "weekly round-up", "the number ones" | hariç | Editoryal liste, haber değil |
| obituary | dies, died, dead, death (ama "near-death" değil) | +1.5 | Kayıp haberi gündemin en net sinyali |
| legal | lawsuit, sues, court, trial, judge, charged, contract | +1.0 | Sektörü ve sanatçıları etkiler |
| industry | charts, streaming, royalties, acquires, deal, earnings, quarter, label | +1.0 | "Neden önemli" açısı en güçlü tür |
| milestone | diagnosis, cancels, hiatus, reunite, farewell, final show, hall of fame | +0.75 | Kariyer dönüm noktası |
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
   (başlık, özet veya kategori kelimeleri; liste `ranking.yml`'de) yoksa, eşleşen en iyi aday
   5. sıradakinin yerine geçer. Bunun koşulu, adayın puanının 5. sıradakinin en az %70'i olmasıdır
   (`genreSwapMinRatio`). Eşleşen aday yoksa veya puanı düşükse hiçbir şey değişmez.
4. Yedekler aynı kurallarla 6.–8. sıradan seçilir.

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

- [ ] W46 gold set: ilk 5'in en az 4'ü Mehmet'in 5'iyle aynı; Mehmet'in 5'i ilk 8'in içinde.
- [ ] Çeşitlilik kurallarının her biri testle kanıtlı.
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
