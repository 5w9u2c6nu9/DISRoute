# DISRoute — Build, run, verify and publish

## Prerequisite

Install the .NET 10 SDK. The repository targets `net10.0` / C# 14 and Avalonia 12.1.x.

## Full source/build/self-test verification

Windows PowerShell:

```powershell
./scripts/verify.ps1
```

Linux/macOS:

```bash
./scripts/verify.sh
```

The sequence performs the repository structural/branding checks, restores packages, Release-builds all 18 projects in `DISRoute.slnx` with warnings-as-errors, and runs `DISRoute.SelfTest`. The self-test covers incident intake, duplicate detection, response recommendation, assignments, Dispatch Commit, Station delivery/acknowledgement, Fire Watch, premise/preplan/CRG/hydrant, GIS, Mayday, command/groups/mutual aid, Mobile encrypted queue/sync, collaborative narrative, additional alarm/cancellation/AVL/corrections, Workforce rotations/overtime/timesheets/payroll, DMS approval workflow, RMS violation/hazmat/investigation/qualification, canonical RMS report lifecycle, RMS-linked NERIS projection/queue/reconciliation safety, persistence round-trip and audit-chain integrity.

### Linux UI startup smoke

On a Linux build host with `xvfb-run` available, after the Release build:

```bash
./scripts/ui-smoke.sh
```

This starts a development DISRoute Server and launches CAD, Station, Operations Center, Station Display and the Mobile field harness under a virtual X server. Each client must initialize and remain running for the smoke window without fatal output. This is a startup regression test, not a substitute for interactive Windows/Raspberry Pi acceptance testing.

For an intentionally offline package feed, `scripts/verify.sh` and `scripts/verify.ps1` accept `DISROUTE_NUGET_CONFIG` pointing at an alternate NuGet configuration.

## Development server

```bash
dotnet run --project src/DISRoute.Server/DISRoute.Server.csproj --environment Development
```

Default URL: `http://127.0.0.1:5099`

Development uses an embedded file-based checkpoint at `data/disroute-state.db` and automatically creates local MFA/offline-lease keys in the server data directory. Do not reuse those generated development keys as production secrets.

## Production PostgreSQL

Set a real PostgreSQL connection string before starting the Production server. Pass 27 supports protected secret references; for example put the Npgsql connection string in a protected file and configure:

```text
Security__Secrets__Directory=/etc/disroute/secrets
ConnectionStrings__DISRoute=secret://file/postgresql.txt
```

The server automatically runs the ordered SQL files in `db/migrations` before loading state. Production authentication and HTTPS cannot be disabled. Production also requires pre-provisioned MFA and offline-lease signing keys using either the configured Base64 settings or protected key files; do not generate/ship fixed keys in source control.

Production API mutations require `X-DISRoute-Request: 1`. DISRoute desktop clients set this automatically.

## Clients

```bash
dotnet run --project src/DISRoute.CAD/DISRoute.CAD.csproj
dotnet run --project src/DISRoute.Station/DISRoute.Station.csproj
dotnet run --project src/DISRoute.OperationsCenter/DISRoute.OperationsCenter.csproj
dotnet run --project src/DISRoute.Station.Display/DISRoute.Station.Display.csproj
dotnet run --project src/DISRoute.Mobile/DISRoute.Mobile.csproj
```

Set `DISROUTE_SERVER_URL` for a remote server. Station clients also accept `DISROUTE_STATION_ID`. The Mobile field harness accepts `DISROUTE_AGENCY_ID`, `DISROUTE_MOBILE_DEVICE_ID`, optional `DISROUTE_MOBILE_UNIT_ID`, and optional `DISROUTE_MOBILE_CACHE_KEY_BASE64`. Native Android/iOS packaging is not yet part of the publish scripts.

## Legacy migration

```bash
dotnet run --project src/DISRoute.LegacyBridge/DISRoute.LegacyBridge.csproj -- validate staging.json
dotnet run --project src/DISRoute.LegacyBridge/DISRoute.LegacyBridge.csproj -- import staging.json --server https://disroute.example --report import-report.json
```

The bridge intentionally consumes a canonical staging export rather than embedding proprietary legacy DataGate/COM dependencies into DISRoute core.

## Publish deployable artifacts

Windows:

```powershell
./scripts/publish.ps1
```

Linux/macOS:

```bash
./scripts/publish.sh
```

The scripts publish Windows and Linux server targets, Windows CAD/Station/Operations Center/Mobile field harness, Linux ARM64 Station Display, and LegacyBridge outputs under `artifacts/publish`. `scripts/package-deployment.py` produces an integrity-tested deployment ZIP with a per-file SHA-256 manifest. See `docs/ops/PRODUCTION-DEPLOYMENT.md` for Linux systemd and Windows Service deployment.

## Verification status of this implementation pass

This pass has been restored and Release-built in the implementation environment using the .NET 10 SDK, including all five Avalonia applications, with **0 warnings and 0 errors**. `DISRoute.SelfTest`, the restart/backup/restore runtime smoke, and the Linux/Xvfb Avalonia startup smoke pass. Production certification still requires target Windows/Raspberry Pi hardware, PostgreSQL HA/DR infrastructure, vendor integrations, security/load/accessibility testing and operational acceptance.
