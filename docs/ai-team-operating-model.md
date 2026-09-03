# AI Yazılım Takımı Çalışma Modeli

## Roller

- `architect`: Opus/high, en fazla 15 turn. Yalnız mimari karar ve ADR.
- `backend-dev`: Sonnet/high, en fazla 40 turn. Implementasyon ve minimum test.
- `test-engineer`: Sonnet/medium, en fazla 20 turn. Test altyapısı, kapsamlı integration
  testi ve kırık suite teşhisi.
- `code-reviewer`: Sonnet/high, en fazla 15 turn. Açıkça istenen bağımsız diff incelemesi.
- `refine-story`: Ana konuşmada çalışan product-owner skill'i.
- `plan-work`: Ana konuşmada çalışan project-manager skill'i.

## Devir

Agentlar başka agent çağırmaz. Her specialist şu kısa sözleşmeyle durur:

```text
Durum: tamamlandı | kısmi | engelli
Kanıt: test veya üretilen belge
Kalan risk: yok | kısa açıklama
Önerilen devir: <rol> — <tek somut görev> | yok
Başlatma komutu: “...” | yok
```

Devri yalnız kullanıcı başlatır. Log, tam diff veya yeniden okunabilecek dosya içeriği handoff'a
kopyalanmaz.

## Kullanım bütçesi

- Normal feature veya bug fix başına en fazla bir subagent.
- Nested ve eşzamanlı subagent sayısı: sıfır/bir.
- Hook retry hedefi: görev başına en fazla iki.
- Tam `dotnet build` + `dotnet test`: görev zincirinde en fazla bir kez.
- Review, kapsamlı test ve ADR yalnız kullanıcı devriyle başlar.

İki haftalık deneme boyunca haftada en az bir kez şunu çalıştır:

```text
bash .claude/scripts/usage-report.sh
```

Eşikler aşılırsa önce agent çağrı sayısını ve hook engel nedenlerini incele; modeli düşürmek son
seçenek olsun. Takım yapılandırmasını değişikliklerden sonra doğrula:

```text
bash .claude/scripts/validate-team.sh
```
