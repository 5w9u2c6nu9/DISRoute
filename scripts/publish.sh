#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-$ROOT/artifacts/publish}"
CONFIG_ARGS=()
if [[ -n "${DISROUTE_NUGET_CONFIG:-}" ]]; then CONFIG_ARGS+=(--configfile "$DISROUTE_NUGET_CONFIG"); fi
rm -rf "$OUT" && mkdir -p "$OUT"
dotnet restore "$ROOT/DISRoute.slnx" "${CONFIG_ARGS[@]}" -m:1
publish(){
  local project="$1" rid="$2" out="$3"
  local -a restore_prop=()
  [[ -n "${DISROUTE_NUGET_CONFIG:-}" ]] && restore_prop+=("-p:RestoreConfigFile=$DISROUTE_NUGET_CONFIG")
  dotnet publish "$ROOT/$project" -c Release -r "$rid" --self-contained false -o "$OUT/$out" "${restore_prop[@]}"
}
publish src/DISRoute.Server/DISRoute.Server.csproj linux-x64 server-linux-x64
publish src/DISRoute.Server/DISRoute.Server.csproj win-x64 server-win-x64
publish src/DISRoute.CAD/DISRoute.CAD.csproj win-x64 cad-win-x64
publish src/DISRoute.Station/DISRoute.Station.csproj win-x64 station-win-x64
publish src/DISRoute.OperationsCenter/DISRoute.OperationsCenter.csproj win-x64 operations-center-win-x64
publish src/DISRoute.Mobile/DISRoute.Mobile.csproj win-x64 mobile-field-harness-win-x64
publish src/DISRoute.Station.Display/DISRoute.Station.Display.csproj linux-arm64 station-display-linux-arm64
publish src/DISRoute.LegacyBridge/DISRoute.LegacyBridge.csproj win-x64 legacy-bridge-win-x64
cp -a "$ROOT/deploy" "$OUT/deploy"
cp -a "$ROOT/docs/ops/PRODUCTION-DEPLOYMENT.md" "$OUT/PRODUCTION-DEPLOYMENT.md"
cp -a "$ROOT/docs/ops/UPGRADE-ROLLBACK.md" "$OUT/UPGRADE-ROLLBACK.md"
echo "Published DISRoute production artifacts to $OUT"
