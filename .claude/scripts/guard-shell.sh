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
