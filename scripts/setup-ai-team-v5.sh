#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# setup-ai-team-v3.sh
# Claude Code AI geliştirme ekibi — güvenlik sertleştirmesi yapılmış sürüm.
#
# Kullanım:  cd ~/projeler/benim-proje && bash setup-ai-team-v3.sh
# Var olan dosyanın üzerine yazmaz; <dosya>.new (.new.1, .new.2 ...) bırakır.
#
# v2'ye göre değişenler:
#   - guard'lar fail-CLOSED (bozuk/eksik girdi = engelle)
#   - shell sanitize yerine dar komut allowlist'i; metakarakter tamamen yasak
#   - test-engineer ve reviewer'a Bash hook'u (dotnet format / ef kaçağı kapandı)
#   - yazma yolu kontrolü symlink'e karşı canonical path ile yapılıyor
#   - src/Ui.Shared Razor Class Library (Mobile → Web bağımlılığı kaldırıldı)
#
# v5'te değişen:
#   - KRİTİK DÜZELTME: ajan frontmatter'ındaki "hooks:" alanı Claude Code'da
#     subagent (Task tool) çağrılarında TETİKLENMİYOR — bilinen bug (GH #18392).
#     Gerçek oturumda doğrulandı: architect, docs/ dışına yazı yaptı ve
#     engellenmedi. Çözüm: hook'lar artık .claude/settings.json içinde,
#     GLOBAL olarak tanımlı; guard script'leri hook JSON'undaki agent_type
#     alanına bakarak hangi ajanın çağırdığını kendisi çözüyor.
# ---------------------------------------------------------------------------
set -euo pipefail

NEWCOUNT=0
say()  { printf '  \033[32m✓\033[0m %s\n' "$1"; }
note() { printf '  \033[33m•\033[0m %s\n' "$1"; }

write() { # write <path> ; içerik stdin'den
  local path="$1" target i=1
  mkdir -p "$(dirname "$path")"
  if [ ! -e "$path" ]; then cat > "$path"; say "$path"; return; fi
  target="$path.new"
  while [ -e "$target" ]; do target="$path.new.$i"; i=$((i+1)); done
  cat > "$target"; NEWCOUNT=$((NEWCOUNT+1)); note "$path zaten var → $target"
}

echo ""
echo "Claude Code AI ekibi (v3) kuruluyor: $(pwd)"
echo ""

mkdir -p .claude/agents .claude/scripts .claude/skills docs/adr docs/product

# ===========================================================================
# 1) ORTAK YARDIMCI — fail-closed JSON okuma
# ===========================================================================
write .claude/scripts/_json.sh <<'EOF'
# Hook JSON'undan alan okur. Ayrıştırma başarısızsa __PARSE_ERROR__ döner.
# Çağıran script bunu exit 2 ile karşılamak ZORUNDADIR (fail-closed).
json_get() { # json_get <json> <alan-yolu>   örn: .tool_input.file_path
  local raw="$1" path="$2"
  if command -v jq >/dev/null 2>&1; then
    printf '%s' "$raw" | jq -er "$path // \"\"" 2>/dev/null || echo "__PARSE_ERROR__"
  elif command -v python3 >/dev/null 2>&1; then
    printf '%s' "$raw" | python3 -c '
import json,sys
keys=[k for k in sys.argv[1].strip(".").split(".") if k]
try:
    d=json.load(sys.stdin)
except Exception:
    print("__PARSE_ERROR__"); sys.exit(0)
for k in keys:
    if isinstance(d,dict) and k in d: d=d[k]
    else: d=""; break
print(d if isinstance(d,str) else "")' "$path"
  else
    echo "__PARSE_ERROR__"
  fi
}

# Girdiyi oku ve boşsa engelle.
read_input_or_block() {
  local input; input="$(cat)"
  if [ -z "$input" ]; then
    echo "Engellendi: hook girdisi boş (fail-closed)." >&2; exit 2
  fi
  printf '%s' "$input"
}

fail_on_parse_error() { # fail_on_parse_error <deger> <alan-adi>
  if [ "$1" = "__PARSE_ERROR__" ]; then
    echo "Engellendi: hook girdisi ayrıştırılamadı ($2). jq veya python3 gerekli." >&2
    exit 2
  fi
}
EOF

# ===========================================================================
# 2) YAZMA YOLU KISITI — canonical path, symlink'e dayanıklı
# ===========================================================================
write .claude/scripts/guard-write-path.sh <<'EOF'
#!/usr/bin/env bash
# Write/Edit çağrılarını, ÇAĞIRAN AJANA göre belirli klasörlerle sınırlar.
# settings.json'da TEK bir global hook olarak tanımlanır (argümansız).
# Ajan kimliği hook JSON'undaki agent_type alanından okunur — çünkü ajan
# frontmatter'ındaki "hooks:" alanı Task-tool subagent çağrılarında
# TETİKLENMİYOR (bilinen Claude Code hatası). Bu yüzden kısıt merkezde durur.
# Exit 2 = engelle.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=/dev/null
. "$DIR/_json.sh"

INPUT="$(read_input_or_block)"

TOOL="$(json_get "$INPUT" '.tool_name')"
fail_on_parse_error "$TOOL" "tool_name"
[ -n "$TOOL" ] || { echo "Engellendi: tool_name yok (fail-closed)." >&2; exit 2; }

case "$TOOL" in
  Write|Edit|MultiEdit|NotebookEdit) ;;
  *) exit 0 ;;   # bu hook yalnızca yazma araçlarını denetler
esac

AGENT="$(json_get "$INPUT" '.agent_type')"
fail_on_parse_error "$AGENT" "agent_type"
if [ -z "$AGENT" ]; then
  # agent_type yok = ana oturum (subagent değil). Bu script yalnızca
  # subagent'ları kısıtlamak için var; ana oturuma karışmaz.
  exit 0
fi

case "$AGENT" in
  architect|product-owner|project-manager) set -- docs ;;
  backend-dev)                             set -- src tests ;;
  test-engineer)                           set -- tests ;;
  code-reviewer)
    echo "Engellendi: code-reviewer hiçbir dosyaya yazamaz/düzenleyemez." >&2
    exit 2 ;;
  *)
    echo "Engellendi: tanımsız ajan ($AGENT) için yazma kuralı yok (fail-closed)." >&2
    exit 2 ;;
esac

FILE="$(json_get "$INPUT" '.tool_input.file_path')"
fail_on_parse_error "$FILE" "file_path"
if [ -z "$FILE" ]; then
  FILE="$(json_get "$INPUT" '.tool_input.notebook_path')"
  fail_on_parse_error "$FILE" "notebook_path"
fi
[ -n "$FILE" ] || { echo "Engellendi: yazma hedefi okunamadı (fail-closed)." >&2; exit 2; }

ROOT="${CLAUDE_PROJECT_DIR:-$(pwd)}"

# --- canonical path: symlink'leri çöz, dosya henüz yoksa da çalışsın ---
canon() {
  if command -v realpath >/dev/null 2>&1; then
    realpath -m -- "$1" 2>/dev/null && return 0
  fi
  if command -v python3 >/dev/null 2>&1; then
    python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$1" 2>/dev/null && return 0
  fi
  return 1
}

case "$FILE" in
  /*) ABS="$FILE" ;;
   *) ABS="$ROOT/$FILE" ;;
esac

CANON_ROOT="$(canon "$ROOT")" || { echo "Engellendi: proje kökü çözümlenemedi." >&2; exit 2; }
CANON_ABS="$(canon "$ABS")"   || { echo "Engellendi: yol çözümlenemedi ($FILE)." >&2; exit 2; }

case "$CANON_ABS" in
  "$CANON_ROOT"/*) REL="${CANON_ABS#"$CANON_ROOT"/}" ;;
  *) echo "Engellendi: proje kökü dışına yazma ($FILE → $CANON_ABS)." >&2; exit 2 ;;
esac

for allowed in "$@"; do
  case "$REL" in "$allowed"/*) exit 0 ;; esac
done

echo "Engellendi: '$AGENT' ajanı yalnızca şu klasörlere yazabilir: $*  (çözümlenen: $REL)" >&2
exit 2
EOF

# ===========================================================================
# 3) SHELL KISITI — sanitize değil, dar allowlist
# ===========================================================================
write .claude/scripts/guard-shell.sh <<'EOF'
#!/usr/bin/env bash
# Bash'i, ÇAĞIRAN AJANA göre dar bir komut kümesine sınırlar.
# settings.json'da TEK global hook olarak tanımlanır (argümansız). Ajan kimliği
# hook JSON'undaki agent_type alanından okunur (bkz. guard-write-path.sh başlığı
# — ajan frontmatter hook'ları subagent çağrılarında tetiklenmiyor, bilinen bug).
#
# Tasarım: shell'i ayrıştırıp güvenli hale getirmeye ÇALIŞMAZ.
# Metakarakter içeren her komut reddedilir; kalan komutlar tam listeye göre denetlenir.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=/dev/null
. "$DIR/_json.sh"

INPUT="$(read_input_or_block)"

TOOL="$(json_get "$INPUT" '.tool_name')"
fail_on_parse_error "$TOOL" "tool_name"
[ -n "$TOOL" ] || { echo "Engellendi: tool_name yok (fail-closed)." >&2; exit 2; }
[ "$TOOL" = "Bash" ] || exit 0

AGENT="$(json_get "$INPUT" '.agent_type')"
fail_on_parse_error "$AGENT" "agent_type"
if [ -z "$AGENT" ]; then
  exit 0   # ana oturum; bu script yalnızca subagent'ları kısıtlar
fi

case "$AGENT" in
  code-reviewer)  set -- build test ;;
  test-engineer)  set -- build test restore ;;
  backend-dev)    set -- build test restore format ef ;;
  architect|product-owner|project-manager)
    echo "Engellendi: '$AGENT' ajanı Bash çalıştıramaz." >&2
    exit 2 ;;
  *)
    echo "Engellendi: tanımsız ajan ($AGENT) için shell kuralı yok (fail-closed)." >&2
    exit 2 ;;
esac

CMD="$(json_get "$INPUT" '.tool_input.command')"
fail_on_parse_error "$CMD" "command"
[ -n "$CMD" ] || { echo "Engellendi: komut okunamadı (fail-closed)." >&2; exit 2; }

# --- 1) Metakarakter yasağı: zincirleme, boru, yönlendirme, ikame, alt kabuk, tırnak
#     (grep'e gömülü satır sonu pattern'i bozduğu için bash pattern eşleşmesi kullanılır)
case "$CMD" in
  *[\;\&\|\<\>\`\$\(\)\{\}\\\'\"]*)
    echo "Engellendi: komutta shell metakarakteri var (; & | < > \` \$ ( ) { } \\ ' \"). Tek düz komut yaz." >&2
    exit 2 ;;
esac
# --- 1b) Satır sonu / kontrol karakteri
if [ "$CMD" != "${CMD//$'\n'/}" ] || [ "$CMD" != "${CMD//$'\r'/}" ] || [ "$CMD" != "${CMD//$'\t'/}" ]; then
  echo "Engellendi: komut satır sonu veya sekme içeriyor." >&2
  exit 2
fi

# --- 2) Tehlikeli argüman yasağı (yazabilen/çalıştırabilen bayraklar)
for tok in $CMD; do
  case "$tok" in
    -o|-O|--output|--output=*|--output-*|--exec|-exec|--exec-path=*|--upload-pack=*|--receive-pack=*|-c|--config=*|--config-env=*|--delete|-delete|--write|--fix|--pager=*|ext::*)
      echo "Engellendi: '$tok' yazma veya çalıştırma yapabilir." >&2; exit 2 ;;
  esac
done

# --- 2b) Proje dışı yol yasağı (MSBuild anahtarları muaf)
for tok in $CMD; do
  case "$tok" in
    -*) continue ;;
    /p:*|/t:*|/m:*|/v:*|/bl|/bl:*|/nologo) continue ;;
    ..|../*|*/../*|*/..|/*|~*)
      echo "Engellendi: proje dışı yol kullanılamaz ($tok)." >&2; exit 2 ;;
  esac
done

# --- 3) Komut allowlist'i
GIT_RO="diff status log show blame rev-parse ls-files describe shortlog"
# shellcheck disable=SC2206
PARTS=( $CMD )
BIN="${PARTS[0]:-}"
SUB="${PARTS[1]:-}"

case "$BIN" in
  git)
    case " $GIT_RO " in
      *" $SUB "*) exit 0 ;;
      *) echo "Engellendi: 'git $SUB' salt-okunur değil. İzinli: $GIT_RO" >&2; exit 2 ;;
    esac ;;
  dotnet)
    for allowed in "$@"; do
      if [ "$SUB" = "$allowed" ]; then
        # 'dotnet ef' yalnızca migration üretimi için
        if [ "$SUB" = "ef" ]; then
          [ "${PARTS[2]:-}" = "migrations" ] || {
            echo "Engellendi: 'dotnet ef' yalnızca 'migrations' ile kullanılabilir." >&2; exit 2; }
          case "${PARTS[3]:-}" in
            add|list|script) ;;
            *) echo "Engellendi: izinli migration işlemleri yalnızca add, list, script." >&2; exit 2 ;;
          esac
        fi
        exit 0
      fi
    done
    echo "Engellendi: 'dotnet $SUB' bu ajan için izinli değil. İzinli: $*" >&2; exit 2 ;;
  *)
    echo "Engellendi: '$AGENT' ajanı '$BIN' çalıştıramaz. İzinli: git (salt-okunur) ve dotnet ($*). Dosya arama için Read/Grep/Glob araçlarını kullan." >&2
    exit 2 ;;
esac
EOF

write .claude/scripts/verify-guards.sh <<'GUARDEOF'
#!/usr/bin/env bash
# AKTİF guard dosyalarını, gerçek hook'un gönderdiği JSON şekliyle dener
# (agent_type gömülü, argümansız çağrı). "Kurulum scriptini çalıştırdım" ile
# "guard'lar gerçekten aktif" aynı şey değildir; bu script ikincisini kanıtlar.
# Kullanım: bash .claude/scripts/verify-guards.sh
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.." || exit 1
SH=".claude/scripts/guard-shell.sh"; WP=".claude/scripts/guard-write-path.sh"
P=0; F=0
chk(){ if [ "$2" = "$3" ]; then P=$((P+1)); printf '  ok   %s\n' "$1";
       else F=$((F+1)); printf '  FAIL %s (exit=%s, beklenen %s)\n' "$1" "$2" "$3"; fi; }
run(){ printf '%s' "$2" | bash "$1" >/dev/null 2>&1; echo $?; }

for f in "$SH" "$WP"; do
  [ -f "$f" ] || { echo "HATA: $f yok."; exit 1; }
  [ -x "$f" ] || { echo "HATA: $f çalıştırılabilir değil → chmod +x $f"; exit 1; }
done
if ls .claude/scripts/*.new* .claude/agents/*.new* .claude/*.new* >/dev/null 2>&1; then
  echo "UYARI: .new dosyaları var. Aktif olan ESKİ sürüm olabilir — diff'leyip birleştir."
fi
if ! grep -q '"PreToolUse"' .claude/settings.json 2>/dev/null; then
  echo "HATA: .claude/settings.json içinde global PreToolUse hook'u bulunamadı."
  exit 1
fi

echo "shell guard (agent_type gömülü JSON ile):"
chk "reviewer: zincirleme engellendi"  "$(run "$SH" '{"tool_name":"Bash","agent_type":"code-reviewer","tool_input":{"command":"git status; rm -rf /"}}')" 2
chk "reviewer: arka plan & engellendi" "$(run "$SH" '{"tool_name":"Bash","agent_type":"code-reviewer","tool_input":{"command":"git status & touch /tmp/x"}}')" 2
chk "reviewer: backtick engellendi"    "$(run "$SH" '{"tool_name":"Bash","agent_type":"code-reviewer","tool_input":{"command":"git diff `id`"}}')" 2
chk "reviewer: dotnet format engelli"  "$(run "$SH" '{"tool_name":"Bash","agent_type":"code-reviewer","tool_input":{"command":"dotnet format"}}')" 2
chk "reviewer: git diff izinli"        "$(run "$SH" '{"tool_name":"Bash","agent_type":"code-reviewer","tool_input":{"command":"git diff main...HEAD"}}')" 0
chk "test-engineer: ef engelli"        "$(run "$SH" '{"tool_name":"Bash","agent_type":"test-engineer","tool_input":{"command":"dotnet ef migrations add X"}}')" 2
chk "backend-dev: ef migr izinli"      "$(run "$SH" '{"tool_name":"Bash","agent_type":"backend-dev","tool_input":{"command":"dotnet ef migrations add X"}}')" 0
chk "backend-dev: ef remove engelli"   "$(run "$SH" '{"tool_name":"Bash","agent_type":"backend-dev","tool_input":{"command":"dotnet ef migrations remove"}}')" 2
chk "backend-dev: proje disi engelli"  "$(run "$SH" '{"tool_name":"Bash","agent_type":"backend-dev","tool_input":{"command":"dotnet test ../baska"}}')" 2
chk "architect: Bash tamamen engelli"  "$(run "$SH" '{"tool_name":"Bash","agent_type":"architect","tool_input":{"command":"git status"}}')" 2
chk "tanimsiz ajan: engelli"           "$(run "$SH" '{"tool_name":"Bash","agent_type":"hayali-ajan","tool_input":{"command":"git status"}}')" 2
chk "ana oturum (agent_type yok): serbest" "$(run "$SH" '{"tool_name":"Bash","tool_input":{"command":"git status; ls"}}')" 0
chk "bozuk json engellendi"            "$(run "$SH" 'bozuk{{{')" 2

echo "write guard (agent_type gömülü JSON ile):"
chk "architect: docs izinli"           "$(run "$WP" '{"tool_name":"Write","agent_type":"architect","tool_input":{"file_path":"docs/a.md"}}')" 0
chk "architect: src engelli"           "$(run "$WP" '{"tool_name":"Write","agent_type":"architect","tool_input":{"file_path":"src/P.cs"}}')" 2
chk "backend-dev: src izinli"          "$(run "$WP" '{"tool_name":"Write","agent_type":"backend-dev","tool_input":{"file_path":"src/Api/P.cs"}}')" 0
chk "backend-dev: docs engelli"        "$(run "$WP" '{"tool_name":"Write","agent_type":"backend-dev","tool_input":{"file_path":"docs/x.md"}}')" 2
chk "test-engineer: tests izinli"      "$(run "$WP" '{"tool_name":"Write","agent_type":"test-engineer","tool_input":{"file_path":"tests/A.cs"}}')" 0
chk "code-reviewer: her yazma engelli" "$(run "$WP" '{"tool_name":"Write","agent_type":"code-reviewer","tool_input":{"file_path":"docs/x.md"}}')" 2
chk "traversal engellendi"             "$(run "$WP" '{"tool_name":"Write","agent_type":"architect","tool_input":{"file_path":"docs/../src/a.cs"}}')" 2
chk "bos girdi engellendi"             "$(run "$WP" '')" 2
chk "ana oturum (agent_type yok): serbest" "$(run "$WP" '{"tool_name":"Write","tool_input":{"file_path":"src/x.cs"}}')" 0
echo ""
echo "$P geçti, $F kaldı"
[ "$F" -eq 0 ] || exit 1
GUARDEOF

chmod +x .claude/scripts/guard-write-path.sh .claude/scripts/guard-shell.sh .claude/scripts/verify-guards.sh 2>/dev/null || true

# ===========================================================================
# 4) TEKNOLOJİ SÖZLEŞMESİ
# ===========================================================================
write .claude/CLAUDE.md <<'EOF'
# Proje Teknoloji Sözleşmesi

Her oturumda yüklenir; **bu projenin** teknoloji kararlarını taşır.
Genel mühendislik kuralları `dotnet-engineering-standards` skill'indedir.

## Hedef platform
- .NET 10 (LTS) / C# 14. `TreatWarningsAsErrors`, nullable ve implicit usings açık.
- **API-first.** Backend hiçbir UI teknolojisini varsaymaz; tek sözleşme HTTP API'dir.

## Projeler
```
src/Api/         ASP.NET Core Minimal API
src/Domain/      entity + value object       → dış paket bağımlılığı yok
src/Infra/       EF Core, PostgreSQL, migration
src/Shared/      DTO/API sözleşmeleri + istemcide çalışabilen doğrulama
                 → UI/HTTP/platform bağımsız. Domain entity'si BURAYA GİRMEZ.
src/Ui.Shared/   Razor Class Library → ortak Razor bileşenleri (sunum katmanı).
                 Razor/UI içerir; MAUI veya tarayıcıya özgü API içermez.
src/Web/         Blazor WASM host    → platform implementasyonları burada
src/Mobile/      MAUI Blazor Hybrid host → platform implementasyonları burada
tests/
```
Katman sözleşmesi:
- **İş kuralları `Domain` içindedir**, `Shared` veya `Ui.Shared` içinde değil.
- `Domain` entity'leri istemci projelerine **açılmaz**; istemci yalnızca `Shared`
  içindeki DTO'ları görür.
- Veritabanı, tenant veya yetki gerektiren doğrulama sunucuda authoritative kalır;
  `Shared` yalnızca deterministik (girdiye bakarak karar verilebilen) kuralları taşır.
- **Web ve Mobile birbirine referans vermez.** Ortak bileşen `Ui.Shared` içine konur.

## Kararlar
- Kimlik: ASP.NET Core Identity + JWT (mobil istemci cookie kullanamaz).
- Veri: EF Core + PostgreSQL (Npgsql). Migration'lar `src/Infra/Migrations`.
- Multi-tenancy: `ITenantContext` + EF Core Global Query Filter.
- Stil: Tailwind CSS.
- Doğrulama: FluentValidation, kurallar `src/Shared` içinde.
- Test: xUnit + Shouldly + Testcontainers.

## Mobil kısıtları
- Sunucuda oturum durumu tutulmaz; her istek kendi kendine yeter.
- Yanıtlar sayfalanır ve küçüktür; bağlantı yavaş ve kesintili varsayılır.
- `src/Shared` ve `src/Ui.Shared` içine platform bağımlılığı girmez.
- **MAUI sürüm ömrü .NET'ten kısadır** (bkz. ADR-0001). Mobil host projesi backend'den
  bağımsız ve daha sık yükseltilir; bu yüzden ince tutulur. İçinde iş mantığı olmaz —
  iş kuralları `Domain`, sözleşme `Shared`, sunum `Ui.Shared` içindedir.

## Çalışma şekli
- Kod yazmadan önce ilgili klasörü oku; varsayım üretme.
- Teslimden önce `dotnet build` ve `dotnet test` yeşil olmalı.
- Mimari kararlar `docs/adr/`, gereksinimler `docs/product/backlog.md`,
  plan `docs/product/plan.md`.

## Ajanlar
architect · backend-dev · code-reviewer · test-engineer · product-owner · project-manager
EOF

# ===========================================================================
# 5) SKILL
# ===========================================================================
write .claude/skills/dotnet-engineering-standards/SKILL.md <<'EOF'
---
name: dotnet-engineering-standards
description: Kişisel projelerdeki .NET/C# mühendislik standartlarını uygular — hem yeni kod YAZARKEN hem de diff/PR İNCELERKEN. Async akışı, CancellationToken, exception ve null davranışı, adlandırma, EF Core sorgu prensipleri, API güvenliği, test kalitesi ve MediatR/AutoMapper kullanmama tercihini kapsar. C#/.NET kodu yaz, ekle, düzelt, refactor et denildiğinde de kullan — sadece "review" istendiğinde değil.
---

# .NET Mühendislik Standartları

Bu skill **nasıl kod yazılacağını** tanımlar. Hangi framework, veritabanı veya UI
sorusunun cevabı projenin `CLAUDE.md` dosyasındadır.

## Ne zaman
- **Yazma**: yeni kod, bug fix, refactor.
- **İnceleme**: diff / PR / "bu kod uygun mu".
- **Açıklama**: bir kuralın gerekçesi sorulduğunda.

## Yazma akışı
1. `references/core-rules.md` oku. Test yazıyorsan ayrıca `references/testing-rules.md`.
   Kısa ve tanıdık bir düzeltmede tekrar okuma — token harcama.
2. Komşu dosyalardaki desene uy; yeni desen icat etme.
3. Kuralları ilk seferde uygula.
4. Teslimden önce `references/review-checklist.md` üzerinden geç.

## İnceleme akışı
1. Diff'i al; verilmemişse çıkar veya iste. Kod uydurma.
2. Sadece **eklenen/değişen** satırlar hakkında bulgu üret.
3. `core-rules.md` + `review-checklist.md` uygula.
4. Format: `[Kritik|Önemli|Öneri] dosya:satır — kural` + kanıt + tek cümlelik öneri.
   Türkçe yaz. Sağlanan kuralı raporlama. Aynı kuralı dosya başına bir kez yaz.

## Bağımlılık kuralı (çelişki çıkarsa bu geçerlidir)
Projede **kurulu olan** kütüphaneleri kullan. Kurulu değilse paket ekleme, sor.
Hangi kütüphanenin kullanılacağı `CLAUDE.md` dosyasında yazar.
Tek istisna: **MediatR ve AutoMapper hiçbir koşulda eklenmez.**

## Referanslar
- `references/core-rules.md`
- `references/testing-rules.md`
- `references/review-checklist.md`
EOF

write .claude/skills/dotnet-engineering-standards/references/core-rules.md <<'EOF'
# Çekirdek Kurallar

## Dil ve adlandırma
- Tip/metot `PascalCase`, yerel/parametre `camelCase`, private alan `_camelCase`.
- Identifier'larda Türkçe karakter yok; yorumlar Türkçe olabilir.
- `var` yalnızca tip sağ taraftan açıkça belliyse.
- Sihirli sayı/string yok → `const` veya modülün `Constants` sınıfı.
- Enum karşılaştırmasında enum tipi veya `nameof()`; string literal değil.
- 3+ parametreli imzalarda primary constructor veya satır kırma.
- Değişmez veriyi `record` ile modelle.

## Async
- I/O yapan public metotlar `async Task`. `async void` yalnızca event handler'da.
- `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` yasak.
- `CancellationToken` her async public metotta parametredir ve I/O çağrılarına geçirilir.
- Kütüphane niteliğindeki kodda `ConfigureAwait(false)`.

## Hata yönetimi
- `catch (Exception ex) { ...; throw; }` — `throw ex;` asla.
- Boş `catch` yok; loglamadan yutma yok.
- Akış kontrolü için exception atma; beklenen hatalar sonuç tipiyle taşınır.
- `IDisposable` → `using` / `await using`.

## Katman disiplini
- Domain dış paket bilmez; invariant'lar constructor/factory'de korunur,
  `public set` açılmaz. Anemic model kaçınılır.
- Uygulama katmanı HTTP bilmez: `HttpContext`, `IActionResult` sızmaz.
- Endpoint iş kuralı içermez: doğrula → çağır → sonucu dön.
- Doğrulama tek yerde yapılır; handler içinde elle tekrar edilmez.
- **MediatR yok** — handler'lar doğrudan DI ile çağrılır.
- **AutoMapper yok** — `ToDto()` extension veya `Select(x => new XDto(...))`.
- EF Core üzerine ikinci repository katmanı sarma; `DbContext` yeterli.

## Veri erişimi (EF Core)
- Konfigürasyon `IEntityTypeConfiguration<T>` sınıflarında.
- Okuma sorgularında `AsNoTracking()`.
- Döngü içinde DB çağrısı yok; toplu iş tek sorgu veya `ExecuteUpdateAsync`.
- Lazy loading kapalı; `Include` zincirleri kontrollü (N+1 üretme).
- Tenant filtresi gerekçesiz `IgnoreQueryFilters()` ile aşılmaz.
- Çok adımlı yazmalar transaction içinde; hata halinde rollback.
- Migration adları anlamlı: `Add_Tenant_To_Orders`.

## API ve güvenlik
- Endpoint'ler varsayılan olarak yetkilendirilir; anonim olan açıkça işaretlenir.
- Token doğrulama parametreleri (issuer, audience, lifetime) açıkça set edilir.
- Kullanıcı girdisi doğrudan SQL'e veya loga basılmaz; PII loglanmaz.
- Secret'lar user-secrets veya environment'ta.
- Liste dönen uçlar sayfalanır.

## İstemci paylaşımı
- `Shared` (sözleşme katmanı): UI, HTTP ve platform bağımlılığı içermez.
  Domain entity'si burada yer almaz; yalnızca DTO ve deterministik doğrulama.
- `Ui.Shared` (sunum katmanı): Razor/UI içerebilir, ancak MAUI veya tarayıcıya
  özgü API içermez. Platform implementasyonu host projelerdedir.
- İş kuralları `Domain` içindedir; paylaşılan katmanlara taşınmaz.
- Ortak Razor bileşenleri host projelerde değil, Razor Class Library'de durur.
- Host projeler (Web, Mobile) birbirine referans vermez.
- Aynı doğrulama kuralı kopyalanmaz; paylaşılan katmandan çalıştırılır.
- Sunucuda istemciye özgü oturum durumu tutulmaz.
EOF

write .claude/skills/dotnet-engineering-standards/references/testing-rules.md <<'EOF'
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
EOF

write .claude/skills/dotnet-engineering-standards/references/review-checklist.md <<'EOF'
# Kontrol Listesi

- [ ] `dotnet build` uyarısız
- [ ] `dotnet test` yeşil
- [ ] MediatR / AutoMapper / gereksiz repository sarmalayıcı eklenmemiş
- [ ] Onaysız yeni NuGet paketi eklenmemiş
- [ ] `.Result` / `.Wait()` / `async void` yok
- [ ] `throw ex;` yok, boş catch yok
- [ ] `CancellationToken` uçtan uca taşınıyor
- [ ] Okuma sorgularında `AsNoTracking()`; döngü içinde DB çağrısı yok
- [ ] Tenant filtresi gerekçesiz aşılmamış
- [ ] Sihirli sabit yok
- [ ] Yeni endpoint yetkilendirilmiş, liste uçları sayfalı
- [ ] Secret hardcode edilmemiş, PII loglanmıyor
- [ ] Paylaşılan katmana UI/HTTP/platform bağımlılığı sızmamış
- [ ] Web ↔ Mobile arasında doğrudan referans yok
- [ ] Değişen davranış için test eklenmiş
EOF

# ===========================================================================
# 6) AJANLAR
# ===========================================================================

write .claude/agents/architect.md <<'EOF'
---
name: architect
description: Mimari kararlar, proje/katman yapısı, teknoloji seçimi ve ADR yazımı. Kod yazmaz, yalnızca docs/ altına yazar. Yeni modül tasarımı, bağımlılık yönü veya "bunu nasıl konumlandıralım" sorularında kullanılır.
tools: Read, Grep, Glob, Write
model: opus
permissionMode: dontAsk
skills: [dotnet-engineering-standards]
---

Sen bu projenin yazılım mimarısın. Kod implemente etmezsin; karar verir ve yazıya
dökersin.

## Akış
1. Mevcut yapıyı ve `docs/adr/` altındaki önceki kararları oku.
2. En az iki seçenek üret; artı, eksi ve maliyetini yaz.
3. Net bir öneri ver — "duruma göre değişir" ile bitirme.
4. Kararı `docs/adr/NNNN-kisa-baslik.md` altına yaz:
   Bağlam / Karar / Alternatifler / Sonuçlar / Durum.

## Sınırlar
- Yalnızca `docs/` altına yazabilirsin; merkezi hook (`settings.json`) zorunlu kılar. Bash'in yok.
- `CLAUDE.md` teknoloji sözleşmesini ve MediatR/AutoMapper yasağını verili kabul et.
  Değişmesi gerekiyorsa yeni bir ADR öner, kendin değiştirme.
- Her kararda gözet: sunucuda oturum durumu yok, paylaşılan katman platformdan
  bağımsız, host projeler birbirine referans vermez, MAUI sürüm ömrü .NET'ten kısa.
- Basit olanı seç. Soyutlamayı ikinci somut ihtiyaçta öner.
- Kısa yaz: karar + gerekçe.
EOF

write .claude/agents/backend-dev.md <<'EOF'
---
name: backend-dev
description: .NET backend implementasyonu — endpoint, handler, entity, EF Core konfigürasyonu, migration, paylaşılan sözleşme ve doğrulama kuralları. Yeni feature, bug fix ve refactor işlerinde kullanılır.
tools: Read, Write, Edit, Bash, Grep, Glob
model: sonnet
permissionMode: dontAsk
skills: [dotnet-engineering-standards]
---

Sen bu projenin backend geliştiricisisin. `dotnet-engineering-standards` çalışma
kuralın, `CLAUDE.md` projenin teknoloji sözleşmesidir.

## Akış
1. İlgili klasörü ve komşu bir örneği oku; mevcut desene uy.
2. Değişikliği en küçük kapsamda yap.
3. **Değiştirdiğin davranışın minimum testini sen yazarsın.** Bug fix ise regresyon
   testi zorunludur. Sınır durumları ve integration testleri test-engineer'a kalır.
4. `dotnet build` ve ilgili testleri çalıştır.
5. Teslimde 5 satırı geçmeyen özet: ne değişti, neden, hangi dosyalar.

## Sınırlar
- Yalnızca `src/` ve `tests/` altına yazabilirsin; merkezi hook (`settings.json`) zorunlu kılar.
- Bash'in `dotnet build|test|restore|format|ef migrations` ve salt-okunur git ile
  sınırlıdır; merkezi hook zorunlu kılar. Zincirleme, boru ve yönlendirme kabul edilmez — tek düz komut yaz.
- Projede **kurulu** kütüphaneleri kullan; yeni paket için sor.
  MediatR ve AutoMapper hiçbir koşulda eklenmez.
- Migration üretirsin, **veritabanına uygulamazsın**.
- Gereksinim belirsizse tahmin etme; tek net soru sor.
EOF

write .claude/agents/code-reviewer.md <<'EOF'
---
name: code-reviewer
description: Yazılmış kodu veya diff'i proje standartlarına göre inceler ve bulgu raporu üretir. Dosya değiştirmez. Bir feature bittiğinde veya PR öncesi kullanılır.
tools: Read, Grep, Glob, Bash
model: sonnet
permissionMode: plan
skills: [dotnet-engineering-standards]
---

Sen bu projenin kod inceleyicisisin. **Hiçbir dosyayı değiştirmezsin.**

## Akış
1. Diff'i çıkar: `git diff`, `git diff main...HEAD` veya `git show`.
   Sana bir diff verilmişse onu kullan.
2. Dosya içeriğine ve aramaya **Read / Grep / Glob araçlarıyla** eriş — shell'de
   `cat`, `grep`, `find` çalıştıramazsın.
3. Sadece **eklenen/değişen** satırlar hakkında bulgu üret.
4. `dotnet-engineering-standards` ve `CLAUDE.md` sözleşmesini uygula.

## Rapor
```
[Kritik|Önemli|Öneri] dosya.cs:42 — kuralın adı
  Kanıt: <kod satırı>
  Öneri: <tek cümle, kararlı>
```
Kritikleri başa al. Sorun yoksa "temiz" de.

## Sınırlar
- Bash'in yalnızca salt-okunur git ve `dotnet build|test` ile sınırlıdır; merkezi hook zorunlu kılar.
  `dotnet format` dahil dosyayı değiştirebilecek her komut engellidir.
- Zincirleme, boru, yönlendirme ve çok satırlı komut reddedilir.
- Stil tercihi ile gerçek hatayı ayır.
- Aynı kuralı dosya başına bir kez raporla. En fazla 15 bulgu.
- Okuyamadığın dosya hakkında yorum yapma.
EOF

write .claude/agents/test-engineer.md <<'EOF'
---
name: test-engineer
description: Sınır durumu ve integration testleri tasarlar, test altyapısını kurar, kırık test suite'ini teşhis eder. Kapsamlı test çalışması gerektiğinde veya testler kırmızıyken kullanılır. Basit birim testini backend-dev kendi yazar.
tools: Read, Write, Edit, Bash, Grep, Glob
model: sonnet
permissionMode: dontAsk
skills: [dotnet-engineering-standards]
---

Sen bu projenin test mühendisisin. backend-dev mutlu yol ve regresyon testini
kendi yazar; **sen sınır durumlarını, integration testlerini, test altyapısını ve
teşhis işini üstlenirsin.**

## Akış
1. Test edilecek kodu oku; davranışı anla, implementasyonu ezberleme.
2. Eksik olanı bul: sınır değerler, hata yolları, eşzamanlılık, tenant izolasyonu,
   kalıcılık davranışı.
3. Integration testlerinde veritabanını container ile ayağa kaldır.
4. `dotnet test` çalıştır; sonucu ve kalan riski özetle.

## Sınırlar
- Yalnızca `tests/` altına yazabilirsin; merkezi hook (`settings.json`) zorunlu kılar.
- Bash'in `dotnet build|test|restore` ve salt-okunur git ile sınırlıdır; merkezi hook zorunlu kılar.
  `dotnet format` ve `dotnet ef` engellidir — bunlar `src/` altını değiştirebilir.
- Testi geçirmek için üretim kodunu **değiştiremezsin**. Hata varsa raporla;
  düzeltmeyi backend-dev yapar.
- Projede kurulu test kütüphanelerini kullan; yeni paket için onay iste.
- Kapsam yüzdesi kovalama; riskli yolu test et.
EOF

write .claude/agents/product-owner.md <<'EOF'
---
name: product-owner
description: Fikirleri kullanıcı hikayelerine, kabul kriterlerine ve önceliklendirilmiş backlog'a çevirir. Kod ve mimari kararı vermez. Belirsiz bir isteği tanımlarken kullanılır.
tools: Read, Grep, Glob, Write
model: sonnet
permissionMode: dontAsk
---

Sen bu projenin product owner'ısın. Teknik çözüm değil, **problem ve kabul kriteri**
üretirsin.

## Akış
1. Kim, ne, neden sorularını netleştir.
2. Belirsizlik varsa en fazla 3 soru sor; sonra en makul varsayımla ilerle ve
   varsayımı açıkça yaz.
3. Çıktıyı `docs/product/backlog.md` dosyasına ekle.

## Hikaye formatı
```
## US-012 — Kısa başlık
Kullanıcı olarak <rol>, <ihtiyaç> istiyorum ki <fayda> elde edeyim.

Kabul kriterleri:
- [ ] <durum> olduğunda <eylem> yapılırsa <sonuç> olur

Kapsam dışı: ...
Öncelik: Must | Should | Could
Platform: web | mobil | ikisi
```

## Sınırlar
- Yalnızca `docs/` altına yazabilirsin; merkezi hook (`settings.json`) zorunlu kılar. Bash'in yok.
- Çözümü tarif etme; ne olması gerektiğini yaz.
- Kabul kriteri test edilebilir olsun ("hızlı olsun" değil, "2 saniyede yanıt döner").
- Web ve mobil davranışı aynı mı farklı mı, her hikayede belirt.
- Bir hikaye tek oturumda bitebilecek büyüklükte olsun.
EOF

write .claude/agents/project-manager.md <<'EOF'
---
name: project-manager
description: Backlog'u çalışma planına böler, iş sırasını ve bağımlılıkları belirler, ilerlemeyi takip eder. Kod yazmaz, ürün kararı vermez. "Nereden başlayalım / sırayla ne yapacağız" sorularında kullanılır.
tools: Read, Grep, Glob, Write
model: sonnet
permissionMode: dontAsk
---

Sen bu projenin proje yöneticisisin. Planı sen tutarsın.

## Akış
1. `docs/product/backlog.md` ve mevcut kod durumunu oku.
2. İşi 1-3 saatlik somut adımlara böl; her adımın "bitti" tanımını yaz.
3. Bağımlılıkları ve sırayı belirt; paralel gidebilecekleri işaretle.
4. `docs/product/plan.md` dosyasını güncelle.

## Plan formatı
```
## Aktif
- [ ] T-07 Endpoint: sipariş oluşturma  (US-012) — ajan: backend-dev — boyut: M
      Bitti: endpoint çalışıyor, doğrulama var, test yeşil
      Bağımlı: T-06

## Sıradaki
## Tamamlanan
```

## Sınırlar
- Yalnızca `docs/` altına yazabilirsin; merkezi hook (`settings.json`) zorunlu kılar. Bash'in yok.
- Gün/saat tahmini uydurma; sıralama ve boyut (S/M/L) yeter.
- API sözleşmesini istemci işlerinden önce planla; Web ve Mobile ona bağlıdır.
- Kapsamı sen büyütme; yeni ihtiyaç çıkarsa product-owner'a bırak.
- Durum raporu istendiğinde 10 satırı geçme.
EOF

# ===========================================================================
# 7) İZİNLER
# ===========================================================================
write .claude/settings.json <<'EOF'
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Write|Edit|MultiEdit|NotebookEdit",
        "hooks": [
          { "type": "command", "command": "d=\"${CLAUDE_PROJECT_DIR:-$PWD}\"; while [ \"$d\" != \"/\" ] && [ ! -e \"$d/.claude/scripts/guard-write-path.sh\" ]; do d=\"$(dirname \"$d\")\"; done; G=\"$d/.claude/scripts/guard-write-path.sh\"; [ -x \"$G\" ] || { echo \"Engellendi: guard bulunamadi veya calistirilabilir degil ($G).\" >&2; exit 2; }; exec \"$G\"" }
        ]
      },
      {
        "matcher": "Bash",
        "hooks": [
          { "type": "command", "command": "d=\"${CLAUDE_PROJECT_DIR:-$PWD}\"; while [ \"$d\" != \"/\" ] && [ ! -e \"$d/.claude/scripts/guard-shell.sh\" ]; do d=\"$(dirname \"$d\")\"; done; G=\"$d/.claude/scripts/guard-shell.sh\"; [ -x \"$G\" ] || { echo \"Engellendi: guard bulunamadi veya calistirilabilir degil ($G).\" >&2; exit 2; }; exec \"$G\"" }
        ]
      }
    ]
  },
  "permissions": {
    "defaultMode": "default",
    "allow": [
      "Bash(dotnet build)",
      "Bash(dotnet build:*)",
      "Bash(dotnet test)",
      "Bash(dotnet test:*)",
      "Bash(dotnet restore)",
      "Bash(dotnet restore:*)",
      "Bash(dotnet format)",
      "Bash(dotnet format:*)",
      "Bash(dotnet ef migrations add:*)",
      "Bash(dotnet ef migrations list:*)",
      "Bash(dotnet ef migrations script:*)",
      "Bash(git status)",
      "Bash(git status:*)",
      "Bash(git diff)",
      "Bash(git diff:*)",
      "Bash(git log)",
      "Bash(git log:*)",
      "Bash(git show:*)",
      "Bash(git ls-files:*)",
      "Bash(git rev-parse:*)",
      "Read(./**)",
      "Write(./src/**)",
      "Edit(./src/**)",
      "Write(./tests/**)",
      "Edit(./tests/**)",
      "Write(./docs/**)",
      "Edit(./docs/**)"
    ],
    "ask": [
      "Bash(dotnet run)",
      "Bash(dotnet run:*)",
      "Bash(dotnet watch)",
      "Bash(dotnet watch:*)",
      "Bash(dotnet add package:*)",
      "Bash(dotnet new)",
      "Bash(dotnet new:*)",
      "Bash(dotnet publish)",
      "Bash(dotnet publish:*)",
      "Bash(dotnet workload)",
      "Bash(dotnet workload:*)",
      "Bash(git checkout:*)",
      "Bash(git switch:*)"
    ],
    "deny": [
      "Bash(git push)",
      "Bash(git push:*)",
      "Bash(git commit)",
      "Bash(git commit:*)",
      "Bash(git reset --hard)",
      "Bash(git reset --hard:*)",
      "Bash(git clean)",
      "Bash(git clean:*)",
      "Bash(dotnet ef database update)",
      "Bash(dotnet ef database update:*)",
      "Bash(dotnet ef database drop)",
      "Bash(dotnet ef database drop:*)",
      "Bash(dotnet nuget push:*)",
      "Bash(rm -rf:*)",
      "Bash(sudo:*)",
      "Bash(curl:*)",
      "Bash(wget:*)",
      "Write(./.claude/**)",
      "Edit(./.claude/**)",
      "Read(./**/.env)",
      "Read(./**/*.env)",
      "Read(./**/appsettings.Production.json)",
      "Read(./**/secrets.json)"
    ]
  }
}
EOF

# ===========================================================================
# 8) ADR + DOKÜMAN İSKELETLERİ
# ===========================================================================
write docs/adr/0001-platform-secimi.md <<'EOF'
# ADR-0001 — Platform ve istemci mimarisi

Durum: Kabul edildi

## Bağlam
Projeler hem web hem mobil uygulama olarak yayınlanacak. Geliştirme tek kişi
tarafından, güçlü .NET birikimiyle yapılıyor.

## Karar
- **.NET 10 (LTS)** hedeflenir; desteği 14 Kasım 2028'e kadar sürüyor.
  .NET 8 ve .NET 9 Kasım 2026'da destek dışına çıkıyor.
- Mimari **API-first**: tek ASP.NET Core Minimal API, iki istemci host'u.
- Ortak Razor bileşenleri **`src/Ui.Shared` Razor Class Library** içinde durur.
  `src/Web` (Blazor WASM) ve `src/Mobile` (MAUI Blazor Hybrid) bu kütüphaneye
  bağımlıdır; **birbirlerine referans vermezler.**
- `src/Shared` DTO/API sözleşmelerini ve istemcide çalıştırılabilen deterministik
  doğrulama kurallarını taşır; UI/HTTP/platform bağımsızdır.
  **Domain entity'leri istemci projelerine açılmaz.**

## Alternatifler
- **Blazor Server**: elendi. Kalıcı sunucu bağlantısına dayanır, mobile çıkış yolu yok.
- **React (web) + React Native/Expo (mobil)**: ekosistem daha geniş ve olgun,
  mobil render performansı daha iyi. Tek kişilik .NET odaklı bir kurulumda iki ayrı
  dil ve araç zinciri anlamına geldiği için elendi.
- **Mobile → Web referansı** (ortak bileşenleri Web'de tutmak): elendi.
  Host projeler arası bağımlılık üretir; RCL bunu ortadan kaldırır.

## Sonuçlar
- DTO/API sözleşmeleri ve deterministik doğrulama kuralları sunucu ile istemciler
  arasında tek kaynaktan gelir. Domain entity'leri paylaşılmaz; veritabanı, tenant
  veya yetki gerektiren doğrulama sunucuda authoritative kalır.
- Blazor WASM'ın ilk indirme boyutu React'e kıyasla büyüktür; halka açık,
  açılış hızının kritik olduğu sayfalarda ölçülmelidir.
- **MAUI sürüm ömrü .NET'ten kısadır.** Bir MAUI majör sürümü, ardılı çıktıktan
  sonra en az 6 ay destekleniyor: MAUI 10'un desteği 11 Mayıs 2027'de bitiyor,
  .NET 10 ise Kasım 2028'e kadar destekli. Mobil host projesi backend'den bağımsız
  ve daha sık yükseltilmek zorunda; bu yüzden ince tutulur, iş mantığı içermez.
  Ayrıca .NET 11'de MAUI mobil için runtime değişiyor: Preview 4 ile CoreCLR
  varsayılan oldu, Preview 6 ile Mono yolu tamamen kaldırıldı ve `UseMonoRuntime`
  kaçış kapısı kapandı. Yani .NET 11'e geçildiğinde CoreCLR'da bir performans
  regresyonu yaşanırsa Mono'ya dönmek mümkün değil. Mobil host'un ince tutulmasının
  gerekçelerinden biri budur; geçiş öncesi performans testi planlanmalı.
- **Expo'ya geçiş senaryosunda ne korunur:** backend ve HTTP API sözleşmesi
  aynen kalır. **Ne korunmaz:** Expo C# `Shared` assembly'sini ve FluentValidation
  kurallarını çalıştıramaz; TypeScript sözleşme üretimi ve istemci tarafı
  doğrulamanın yeniden yazılması gerekir. `Ui.Shared` tamamen yeniden yazılır.
  Yani maliyet "yalnızca UI" değildir; mobil UI + istemci DTO'ları + istemci
  doğrulaması yeni teknolojiye uyarlanır.
EOF

write docs/product/backlog.md <<'EOF'
# Backlog

Henüz hikaye yok. `product-owner` ajanı buraya yazar.
EOF

write docs/product/plan.md <<'EOF'
# Plan

## Aktif

## Sıradaki

## Tamamlanan
EOF

echo ""
echo "Kurulum tamam."
echo ""
echo "Sırada:"
echo "  1) chmod +x .claude/scripts/*.sh"
echo "  2) jq veya python3 kurulu olmalı — ikisi de yoksa guard'lar HER ŞEYİ engeller"
echo "  3) code .   → VS Code, Claude Code paneli"
echo "  4) Klasör güven (trust) diyaloğunu KABUL ET — yoksa agent hook'ları çalışmaz"
if [ "$NEWCOUNT" -gt 0 ]; then
  echo ""
  echo "  \033[31m!! DİKKAT\033[0m  $NEWCOUNT dosya .new olarak yazıldı; AKTİF olan hâlâ eski sürüm."
  echo "     Guard'lar ve ajanlar güncellenmeden yeni kurallar geçerli DEĞİLDİR."
  echo "     find .claude docs -name '*.new*'   → diff'leyip birleştir, sonra chmod +x .claude/scripts/*.sh"
  echo ""
fi
echo "  5) Guard'ların gerçekten aktif olduğunu kanıtla:"
echo "     bash .claude/scripts/verify-guards.sh"
echo "  6) NOT: hook'lar artık settings.json'da GLOBAL — ajan dosyalarında değil."
echo "     Ajan frontmatter'ındaki 'hooks:' alanı subagent çağrılarında"
echo "     tetiklenmiyor (bilinen Claude Code hatası). Bu yüzden merkezi kuruldu."
echo "  7) Oturum içi doğrulama:"
echo "     @architect src/Program.cs dosyasına bir satır ekle      → engellenmeli"
echo "     @code-reviewer 'dotnet format' çalıştır                 → engellenmeli"
echo "     @test-engineer 'dotnet ef migrations add X' çalıştır    → engellenmeli"
echo "     @backend-dev 'git push' çalıştır                        → engellenmeli"
echo "  8) Fail-closed kanıtı: chmod -x .claude/scripts/guard-write-path.sh"
echo "     sonra 1. testi tekrarla → 'guard bulunamadi' mesajıyla YİNE engellenmeli"
echo "     (sonra chmod +x ile geri al)"
echo ""
