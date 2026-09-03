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
chk "reviewer: git grep izinli"        "$(run "$SH" '{"tool_name":"Bash","agent_type":"code-reviewer","tool_input":{"command":"git grep TODO"}}')" 0
chk "reviewer: pwd izinli"             "$(run "$SH" '{"tool_name":"Bash","agent_type":"code-reviewer","tool_input":{"command":"pwd"}}')" 0
chk "reviewer: find izinli"            "$(run "$SH" '{"tool_name":"Bash","agent_type":"code-reviewer","tool_input":{"command":"find src -name *.cs"}}')" 0
chk "reviewer: find exec engelli"      "$(run "$SH" '{"tool_name":"Bash","agent_type":"code-reviewer","tool_input":{"command":"find src -exec id"}}')" 2
chk "test-engineer: ef engelli"        "$(run "$SH" '{"tool_name":"Bash","agent_type":"test-engineer","tool_input":{"command":"dotnet ef migrations add X"}}')" 2
chk "backend-dev: ef migr izinli"      "$(run "$SH" '{"tool_name":"Bash","agent_type":"backend-dev","tool_input":{"command":"dotnet ef migrations add X"}}')" 0
chk "backend-dev: quoted filter izinli" "$(run "$SH" '{"tool_name":"Bash","agent_type":"backend-dev","tool_input":{"command":"dotnet test --filter \"Category=Unit\""}}')" 0
chk "backend-dev: proje ici abs izinli" "$(run "$SH" "{\"tool_name\":\"Bash\",\"agent_type\":\"backend-dev\",\"tool_input\":{\"command\":\"dotnet test $PWD/tests/TheBluesland.UnitTests/TheBluesland.UnitTests.csproj\"}}")" 0
chk "backend-dev: ef remove engelli"   "$(run "$SH" '{"tool_name":"Bash","agent_type":"backend-dev","tool_input":{"command":"dotnet ef migrations remove"}}')" 2
chk "backend-dev: proje disi engelli"  "$(run "$SH" '{"tool_name":"Bash","agent_type":"backend-dev","tool_input":{"command":"dotnet test ../baska"}}')" 2
chk "architect: Bash tamamen engelli"  "$(run "$SH" '{"tool_name":"Bash","agent_type":"architect","tool_input":{"command":"git status"}}')" 2
chk "tanimsiz ajan: engelli"           "$(run "$SH" '{"tool_name":"Bash","agent_type":"hayali-ajan","tool_input":{"command":"git status"}}')" 2
chk "ana oturum (agent_type yok): serbest" "$(run "$SH" '{"tool_name":"Bash","tool_input":{"command":"git status; ls"}}')" 0
chk "bozuk json engellendi"            "$(run "$SH" 'bozuk{{{')" 2

echo "write guard (agent_type gömülü JSON ile):"
chk "architect: docs izinli"           "$(run "$WP" '{"tool_name":"Write","agent_type":"architect","tool_input":{"file_path":"docs/a.md"}}')" 0
chk "architect: src engelli"           "$(run "$WP" '{"tool_name":"Write","agent_type":"architect","tool_input":{"file_path":"src/P.cs"}}')" 2
chk "backend-dev: Web izinli"          "$(run "$WP" '{"tool_name":"Write","agent_type":"backend-dev","tool_input":{"file_path":"src/TheBluesland.Web/P.cs"}}')" 0
chk "backend-dev: tools izinli"        "$(run "$WP" '{"tool_name":"Write","agent_type":"backend-dev","tool_input":{"file_path":"tools/spotify-playlist-fetcher/P.cs"}}')" 0
chk "backend-dev: workflow izinli"     "$(run "$WP" '{"tool_name":"Write","agent_type":"backend-dev","tool_input":{"file_path":".github/workflows/ci.yml"}}')" 0
chk "backend-dev: docs engelli"        "$(run "$WP" '{"tool_name":"Write","agent_type":"backend-dev","tool_input":{"file_path":"docs/x.md"}}')" 2
chk "test-engineer: tests izinli"      "$(run "$WP" '{"tool_name":"Write","agent_type":"test-engineer","tool_input":{"file_path":"tests/A.cs"}}')" 0
chk "code-reviewer: her yazma engelli" "$(run "$WP" '{"tool_name":"Write","agent_type":"code-reviewer","tool_input":{"file_path":"docs/x.md"}}')" 2
chk "traversal engellendi"             "$(run "$WP" '{"tool_name":"Write","agent_type":"architect","tool_input":{"file_path":"docs/../src/a.cs"}}')" 2
chk "bos girdi engellendi"             "$(run "$WP" '')" 2
chk "ana oturum (agent_type yok): serbest" "$(run "$WP" '{"tool_name":"Write","tool_input":{"file_path":"src/x.cs"}}')" 0
echo ""
echo "$P geçti, $F kaldı"
[ "$F" -eq 0 ] || exit 1
