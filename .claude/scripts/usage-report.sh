#!/usr/bin/env bash
# Claude Code oturumlarındaki yaklaşık etkili kullanımı ve retry sinyallerini raporlar.
# Kullanım: bash .claude/scripts/usage-report.sh [claude-project-log-directory]
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
PROJECT_KEY="${ROOT//\//-}"
LOG_DIR="${1:-$HOME/.claude/projects/$PROJECT_KEY}"

[ -d "$LOG_DIR" ] || {
  echo "Claude proje kayıt dizini bulunamadı: $LOG_DIR" >&2
  exit 1
}
command -v jq >/dev/null || { echo "jq gerekli." >&2; exit 1; }

shopt -s nullglob
MAIN_FILES=("$LOG_DIR"/*.jsonl)
SUB_FILES=("$LOG_DIR"/*/subagents/*.jsonl)
ALL_FILES=("${MAIN_FILES[@]}" "${SUB_FILES[@]}")
[ "${#ALL_FILES[@]}" -gt 0 ] || { echo "JSONL kaydı bulunamadı." >&2; exit 1; }

summarize() {
  local label="$1"
  shift
  if [ "$#" -eq 0 ]; then
    printf '%-12s calls=%-5s effective=%s\n' "$label" 0 0
    return
  fi
  jq -s --arg label "$label" '
    [.[] | select(.message.usage != null and .message.model != "<synthetic>")]
    | unique_by(.message.id)
    | {
        label: $label,
        calls: length,
        effective: (map(
          (.message.usage.input_tokens // 0)
          + 2 * (.message.usage.cache_creation_input_tokens // 0)
          + 0.1 * (.message.usage.cache_read_input_tokens // 0)
          + 5 * (.message.usage.output_tokens // 0)
        ) | add // 0)
      }
    | "\(.label)\tcalls=\(.calls)\teffective=\(.effective | floor)"
  ' -r "$@"
}

echo "Claude usage report"
echo "logs: $LOG_DIR"
summarize "main" "${MAIN_FILES[@]}"
summarize "subagents" "${SUB_FILES[@]}"
printf 'subagent_files\t%s\n' "${#SUB_FILES[@]}"

jq -s '
  [ .[]
    | select(.message.content != null)
    | .message.content[]?
    | select(.type == "tool_result" and (.content | type) == "string")
    | select(.content | contains("PreToolUse:") and contains("Engellendi:"))
    | .tool_use_id
  ] | unique | length
' -r "${ALL_FILES[@]}" | awk '{print "blocked_tool_calls\t" $1}'

echo "Hedefler: görev başına <=1 subagent, nested=0, hook retry <=2, tam build/test <=1."
