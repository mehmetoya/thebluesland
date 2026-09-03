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
  backend-dev)                             set -- src tests tools .github ;;
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
