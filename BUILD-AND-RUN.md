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

The sequence performs the repository structural/branding checks, restores packages, Release-builds all 18 projects in `DISRoute.slnx` with warnings-as-errors, and runs `DISRoute.SelfTest`. The self-test covers incident intake, duplicate detection, response recommendation, assignments, Dispatch Commit, Station delivery/acknowledgement, Fire Watch, premise/preplan/CRG/hydrant, GIS, Mayday, command/groups/mutual aid, Mobile encrypted queue/sync, collaborative narrative, additional alarm/cancellation/AVL/corrections, Workforce rotations/overtime/timesheets/payroll, DMS approval workflow, RMS violation/hazmat/investigation/qualification, canonical RMS report lifecycle, RMS-linked NERIS projection/queue/reconciliation safety, persistence round-trip and audit-chain integrity. Pass 28 also locks the production role/permission matrix into SelfTest.



### Pass 29 stress/fault qualification

After a Release build, run the isolated FileCheckpoint stress suite:

```bash
DOTNET_BIN=/path/to/dotnet ./scripts/stress-qualification.sh
```

For a disposable PostgreSQL database, `scripts/postgresql-load-failover-qualification.py` runs retry-safe concurrent commands across an abrupt application-primary loss and verifies exactly-once durable results, journal drain and audit integrity. See `docs/ops/PASS-29-STRESS-QUALIFICATION.md`.

### Pass 28 operational end-to-end qualification

After the Release build, run the live cumulative operational scenario suite:

```bash
./scripts/operational-e2e-qualification.sh
```

It exercises six combined workflows through the real HTTP API, including a multi-unit active incident across an actual server restart, Mobile offline reconcile/replay, correction/merge/split history, RMS-to-NERIS degradation/recovery and audit/journal closure. See `docs/ops/OPERATIONAL-E2E-QUALIFICATION.md`.

For the same-active-incident application authority-transfer regression on a disposable PostgreSQL database:

```bash
DISROUTE_HA_TEST_CONNECTION_STRING='Host=127.0.0.1;Port=...;Database=...;Username=...;Password=...' ./scripts/ha-smoke.sh
```

The Pass 28 HA smoke dispatches one incident before authority transfer, continues and closes that same incident on the promoted application node, verifies the old writer is fenced, and validates the complete pre/post-transfer history after authority cycles again.

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

PostgreSQL production databases must use UTF8 encoding; DISRoute fails startup on incompatible encodings such as SQL_ASCII.

Pass 29 has been restored and Release-built in the implementation environment using the .NET 10 SDK, including all five Avalonia applications, with **0 warnings and 0 errors**. `DISRoute.SelfTest`, the Pass 29 stress/fault suite, PostgreSQL active-load failover, the six-scenario operational end-to-end suite, active-incident PostgreSQL application-HA continuity smoke, restart/backup/restore runtime smoke, Pass 25/26 PostgreSQL regressions, Pass 27 production-security regression and all five Linux/Xvfb Avalonia startup smokes pass. Target Windows/Raspberry Pi hardware, native mobile packaging, live vendor conformance, independent penetration/accessibility testing and agency acceptance remain external.


## Pass 30 release-candidate gates

DISRoute is versioned as `1.0.0-rc.1` and pins SDK `10.0.400` in `global.json`. All 18 projects carry `packages.lock.json`; production/CI restore should use locked mode. The online CI path sets `DISROUTE_ENABLE_NUGET_AUDIT=true`, which audits all direct/transitive NuGet packages and promotes NU1901–NU1904 advisory warnings to errors.

Generate an SBOM after restore:

```bash
SOURCE_DATE_EPOCH=<release-epoch> python3 scripts/generate-sbom.py . --output artifacts/DISRoute-1.0.0-rc.1-sbom.cdx.json
```

After producing the eight publish targets, compare two independent publish trees with `scripts/compare-publish-trees.py`. Then run `scripts/release-candidate-qualification.py` with a fixed `--source-date-epoch`; it packages the same publish payload twice and fails unless both deployment ZIPs are byte-identical.

See `docs/ops/RELEASE-CANDIDATE-ACCEPTANCE.md`, `KNOWN-LIMITATIONS.md`, and `RELEASE-CHECKLIST.md` before treating an implementation RC as approved for agency production.
