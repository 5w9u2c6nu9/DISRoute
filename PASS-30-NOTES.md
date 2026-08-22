# DISRoute Pass 30 — Release Candidate / Final Production Readiness

**Target release:** `1.0.0-rc.1`

Pass 30 freezes the implementation into a reproducible, dependency-locked release candidate. It does not convert external agency/hardware/vendor/security-assessment acceptance into repository claims.

## RC hardening / supply chain

- Product/assembly informational version pinned to `1.0.0-rc.1`.
- .NET SDK pinned exactly with `global.json`.
- all 18 projects use NuGet lock files; locked restore is a release gate.
- CI-only NuGet audit treats known NU1901–NU1904 advisories as errors.
- deterministic deployment packaging requires `SOURCE_DATE_EPOCH` and emits stable manifest metadata/ZIP timestamps/modes.
- CycloneDX 1.6 SBOM generator inventories direct and transitive packages from the lock files and records available package SHA-256 hashes.
- release-candidate qualification reproduces the deployment ZIP twice and requires byte-identical hashes.
- independent publish-tree comparator provides a byte-for-byte build-reproducibility gate.
- GitHub CI now performs locked restore, Release build, SelfTest, source verification and online package advisory audit.

## Release documentation

- `docs/ops/RELEASE-CANDIDATE-ACCEPTANCE.md`
- `docs/ops/KNOWN-LIMITATIONS.md`
- `docs/ops/RELEASE-CHECKLIST.md`
- `docs/ops/RELEASE-NOTES-1.0.0-rc.1.md`

## Final cumulative qualification on the frozen RC source

- locked restore across all 18 projects: PASS
- Release build: PASS, 0 warnings / 0 errors
- SelfTest: PASS — `incidents=7, units=6, audit=145, alerts=3`
- source verifier: PASS — 124 C# files / 18 projects / 313 API routes / 20 migrations
- runtime restart / backup / staged restore: PASS
- Pass 28 operational E2E: 6/6 PASS
- Pass 29 stress / hostile-input / rate-limit isolation: PASS
- all five Avalonia startup smokes: PASS
- Pass 25 PostgreSQL HA regression: PASS; local crash-failover measurement 525 ms
- Pass 28 active-incident application HA continuity on UTF-8 PostgreSQL: PASS
- Pass 29 active-load application failover: PASS; 80/80 commands durable exactly once, local failover measurement 928.7 ms
- Pass 27 Production security / deployment / TLS qualification: PASS
- Pass 26 physical PostgreSQL HA / witness partition / rewind / failback / PITR regression: PASS; local physical failover 2.579 s, failback 1.735 s
- complete eight-target release publish: PASS
- independent publish-tree rebuild: 318/318 files byte-identical
- published-dependency verification: PASS; 24 shipped package versions, 0 outside frozen lock graph

All timing figures above are implementation-lab measurements, not production SLA/RPO/RTO guarantees. External acceptance items remain exactly as listed in `docs/ops/RELEASE-CANDIDATE-ACCEPTANCE.md` and `docs/ops/KNOWN-LIMITATIONS.md`.

Final source/deployment/SBOM/release-manifest hashes are generated only after the authoritative Git commit/tree identifiers are fixed.
