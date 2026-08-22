# DISRoute Release Checklist — 1.0.0-rc.1

## Repository / supply chain

- [ ] `main` points to the approved RC commit and fixed baseline branch.
- [x] `global.json` SDK pin is available on the build agent.
- [x] `dotnet restore DISRoute.slnx --locked-mode` succeeds.
- [ ] NuGet advisory audit runs against current online advisory data with `DISROUTE_ENABLE_NUGET_AUDIT=true`.
- [ ] CycloneDX SBOM is generated and archived with release artifacts.
- [ ] Source, deployment, manifest and SBOM SHA-256 values are recorded.

## Build / qualification

- [x] 18-project Release build has 0 warnings / 0 errors.
- [x] SelfTest and source verifier pass.
- [x] Pass 28 operational E2E and active-incident HA pass.
- [x] Pass 29 stress/load/failover suite passes.
- [x] Pass 25/26 PostgreSQL HA/PITR regressions pass.
- [x] Pass 27 Production security/deployment regression passes.
- [x] all five UI startup smokes pass.
- [x] two independent publish trees compare byte-for-byte identical.
- [x] deterministic deployment package is reproduced twice with identical SHA-256.

## Production acceptance — external

- [ ] Production PostgreSQL quorum/fencing implementation selected and tested.
- [ ] Dispatch/HQ network/site failure drill and target RPO/RTO accepted.
- [ ] Windows Server SCM install/update/rollback accepted.
- [ ] Raspberry Pi Station Display accepted on target hardware.
- [ ] Vendor integrations accepted against live systems.
- [ ] Signing identity applied to installer/release artifacts.
- [ ] Independent security review/penetration testing completed.
- [ ] Accessibility/operator/multi-monitor UAT completed.
- [ ] Training, backup/restore drill, DR runbook and go-live approval signed off.
