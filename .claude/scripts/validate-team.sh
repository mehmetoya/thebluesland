#!/usr/bin/env bash
# Agent, skill, settings ve guard bütünlüğünü doğrular.
# Kullanım: bash .claude/scripts/validate-team.sh
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/../.."
jq empty .claude/settings.json .claude/settings.local.json

EXPECTED_AGENTS="architect backend-dev code-reviewer test-engineer"
for name in $EXPECTED_AGENTS; do
  file=".claude/agents/$name.md"
  [ -f "$file" ] || { echo "Eksik agent: $file" >&2; exit 1; }
  grep -q "^name: $name$" "$file" || { echo "Agent adı hatalı: $file" >&2; exit 1; }
  grep -q '^description: ' "$file" || { echo "Description eksik: $file" >&2; exit 1; }
  grep -q '^model: ' "$file" || { echo "Model eksik: $file" >&2; exit 1; }
  grep -q '^effort: ' "$file" || { echo "Effort eksik: $file" >&2; exit 1; }
  grep -q '^maxTurns: ' "$file" || { echo "maxTurns eksik: $file" >&2; exit 1; }
done

[ ! -e .claude/agents/product-owner.md ] || { echo "product-owner agent olarak kalmış." >&2; exit 1; }
[ ! -e .claude/agents/project-manager.md ] || { echo "project-manager agent olarak kalmış." >&2; exit 1; }
[ -f .claude/skills/refine-story/SKILL.md ] || { echo "refine-story skill eksik." >&2; exit 1; }
[ -f .claude/skills/plan-work/SKILL.md ] || { echo "plan-work skill eksik." >&2; exit 1; }

if command -v claude >/dev/null 2>&1; then
  claude plugin validate .claude/agents
else
  echo "Bilgi: claude CLI PATH'te yok; yerel statik doğrulama kullanıldı."
fi

bash .claude/scripts/verify-guards.sh
echo "Takım yapılandırması geçerli."
