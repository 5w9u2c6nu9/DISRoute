#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

python3 scripts/verify-source.py
dotnet --info

restore_args=(DISRoute.slnx)
if [[ -n "${DISROUTE_NUGET_CONFIG:-}" ]]; then
  restore_args+=(--configfile "$DISROUTE_NUGET_CONFIG")
fi
if [[ "${NUGET_CERT_REVOCATION_MODE:-}" == "offline" ]]; then
  echo "NuGet certificate revocation mode: offline (explicit environment setting)."
fi

dotnet restore "${restore_args[@]}" -p:Platform="Any CPU"
dotnet build DISRoute.slnx -c Release --no-restore -p:Platform="Any CPU"
dotnet src/DISRoute.SelfTest/bin/Release/net10.0/DISRoute.SelfTest.dll

echo "DISRoute solution build and self-test completed successfully."
