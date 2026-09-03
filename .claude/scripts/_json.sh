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
