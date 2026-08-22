# DISRoute 1.0.0-rc.1 Release Notes

`1.0.0-rc.1` is the first release-candidate checkpoint of the cumulative DISRoute implementation.

## Included

- Windows-first CAD, Station and Operations Center applications; dedicated Linux ARM64 Station Display; Mobile shared field package plus desktop validation harness.
- Server-authoritative CAD workflows, realtime collaboration, Station delivery, command/mutual-aid, GIS/site intelligence, RMS/NERIS, Workforce, DMS/forms, internal chat, integration gateway and audit/export/recovery surfaces.
- PostgreSQL production persistence with immutable 20-migration ledger, durable command/idempotency semantics, application HA coordination, physical-HA safety contract, backup/PITR qualification and UTF-8 database enforcement.
- Production TLS/reverse-proxy, authentication/MFA policy, secret references, request limits/rate limiting, protected deployment/upgrade readiness and hardened Linux/Windows service packaging.
- End-to-end operational, restart, HA, physical failover/PITR, stress, hostile-input and active-load failover qualification harnesses.
- RC supply-chain controls: exact SDK pin, NuGet lock files, online CI advisory gate, CycloneDX SBOM and deterministic release packaging.

## Compatibility / upgrade

The production schema remains **version 20**; Pass 30 does not introduce a database migration. Follow `UPGRADE-ROLLBACK.md` and the protected `/api/admin/upgrade/preflight` gate before changing application binaries.

## Not certified by this RC

See `KNOWN-LIMITATIONS.md` and `RELEASE-CANDIDATE-ACCEPTANCE.md` for external Windows/Raspberry Pi hardware, native mobile, signing, vendor conformance, production DB quorum/fencing, penetration/accessibility and agency-UAT gates.
