#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_ROOT:-$(dirname "$(command -v dotnet 2>/dev/null || echo /usr/bin/dotnet)")}/dotnet"
if [[ ! -x "$DOTNET_BIN" ]]; then
  DOTNET_BIN="$(command -v dotnet)"
fi

command -v xvfb-run >/dev/null 2>&1 || {
  echo "xvfb-run is required for the Linux UI smoke test." >&2
  exit 2
}

PORT="${DISROUTE_UI_SMOKE_PORT:-5119}"
BASE="http://127.0.0.1:${PORT}"
WORK="${DISROUTE_UI_SMOKE_WORK:-/tmp/disroute-ui-smoke}"
mkdir -p "$WORK"
rm -f "$WORK"/*.log "$WORK"/*.json "$WORK"/*.ndjson 2>/dev/null || true

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="$BASE"
export Persistence__Provider=FileCheckpoint
export Persistence__CheckpointPath="$WORK/disroute-state.json"
export Persistence__JournalPath="$WORK/disroute-command-journal.ndjson"
export DISROUTE_SERVER_URL="$BASE/"
export TERM="${TERM:-xterm}"

SERVER_DLL="$ROOT/src/DISRoute.Server/bin/Release/net10.0/DISRoute.Server.dll"
[[ -f "$SERVER_DLL" ]] || {
  echo "Release build not found. Build the solution before running UI smoke." >&2
  exit 2
}

(cd "$ROOT/src/DISRoute.Server" && "$DOTNET_BIN" "$SERVER_DLL" --urls "$BASE") >"$WORK/server.log" 2>&1 &
SERVER_PID=$!
cleanup() {
  if kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

for _ in $(seq 1 80); do
  if curl -fsS "$BASE/health/ready" >/dev/null 2>&1; then
    break
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    echo "Server exited during UI smoke startup." >&2
    cat "$WORK/server.log" >&2
    exit 1
  fi
  sleep 0.25
done
curl -fsS "$BASE/health/ready" >/dev/null

export DISROUTE_MOBILE_DEVICE_ID="ui-smoke-mobile"
curl -fsS -X POST "$BASE/api/security/devices" -H "Content-Type: application/json" -d '{"agencyId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","deviceId":"ui-smoke-mobile","name":"UI Smoke Mobile","role":"Mobile","platform":"Linux","certificateThumbprint":null}' >/dev/null

projects=(DISRoute.CAD DISRoute.Station DISRoute.OperationsCenter DISRoute.Station.Display DISRoute.Mobile)
for project in "${projects[@]}"; do
  dll="$ROOT/src/$project/bin/Release/net10.0/$project.dll"
  log="$WORK/$project.log"
  [[ -f "$dll" ]] || { echo "Missing $dll" >&2; exit 1; }

  set +e
  timeout --kill-after=2s 6s xvfb-run -a "$DOTNET_BIN" "$dll" >"$log" 2>&1
  rc=$?
  set -e

  if [[ "$rc" -ne 124 ]]; then
    echo "$project failed startup smoke (exit $rc)." >&2
    cat "$log" >&2
    exit 1
  fi
  if grep -Eiq 'Unhandled exception|fatal|crash' "$log"; then
    echo "$project emitted fatal startup output." >&2
    cat "$log" >&2
    exit 1
  fi
  echo "$project UI startup PASS"
done

echo "DISRoute UI smoke PASS — all five Avalonia applications remained healthy for the startup window."
