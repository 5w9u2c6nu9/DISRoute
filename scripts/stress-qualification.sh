#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-$(command -v dotnet || true)}"
[[ -n "$DOTNET_BIN" && -x "$DOTNET_BIN" ]] || { echo 'Set DOTNET_BIN to the .NET 10 dotnet executable.' >&2; exit 2; }
EVIDENCE="${PASS29_EVIDENCE:-$ROOT/docs/ops/evidence/PASS-29-STRESS-EVIDENCE.json}"
exec python3 "$ROOT/scripts/stress-qualification.py" --root "$ROOT" --dotnet "$DOTNET_BIN" --evidence "$EVIDENCE" "$@"
