#!/usr/bin/env bash
# Local preview: installs the Tailwind toolchain if needed, rebuilds the stylesheet (it is not
# checked in: wwwroot/css/app.css is generated), then runs the web app. Serving a stale
# app.css is the usual cause of "the page looks wrong locally", so the CSS is always rebuilt.
#
# Usage:  scripts/dev.sh [extra dotnet run args]
# Env:    anything the app reads passes through, e.g.
#           WeeklyContent__Directory=/path/to/weekly ASPNETCORE_URLS=http://localhost:5699 scripts/dev.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
web="$root/src/TheBluesland.Web"

if [ ! -d "$web/node_modules" ]; then
  (cd "$web" && npm ci)
fi

(cd "$web" && npm run build:css)

exec dotnet run --project "$web" --no-launch-profile "$@"
