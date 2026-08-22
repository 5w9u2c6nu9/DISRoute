# DISRoute Pass 28 — End-to-End Operational Workflow Qualification

**Scope:** qualify the cumulative product as a coherent operational system, fix only issues exposed by that qualification, and preserve all Pass 25–27 HA/deployment/security guarantees. No scope reduction or HA/DR redesign.

## Implementation changes

- Added `scripts/operational-e2e-qualification.sh`, a six-scenario live HTTP qualification harness covering routine dispatch, an active multi-unit structure fire across server restart, Mobile offline reconciliation/duplicate replay, correction/merge/split history, RMS→NERIS provider degradation/recovery and final audit/journal integrity.
- Hardened `DISRoute.SelfTest` with the least-privilege role matrix used by the HTTP permission middleware: Dispatcher, StationOfficer, RecordsReporting and Administrator permissions now have explicit regression assertions.
- Extended `scripts/ha-smoke.sh` so application failover is qualified using the **same already-dispatched incident**. The promoted node must continue its narrative and close it; the drained old writer remains fenced; after authority later returns, both pre/post-failover activity and audit-chain integrity must still be present.
- Added Pass 28 operational qualification documentation and machine-readable evidence under `docs/ops/evidence`.

## Live operational qualification

The clean uninterrupted FileCheckpoint HTTP run passed **6/6 scenarios**:

- routine call-to-close;
- active structure fire across real process restart with command, group, mutual aid, site intelligence, chat and DMS linkage preserved;
- Mobile offline queue/reconciliation with three Accepted operations and duplicate replay reported as `DuplicateIgnored`;
- audited correction plus relationship/merge/split lifecycle;
- canonical RMS finalization and NERIS provider failure/recovery while core CAD continued accepting work;
- audit hash-chain validation and fully drained durable journal.

Evidence: `docs/ops/evidence/PASS-28-OPERATIONAL-E2E-EVIDENCE.json`.

The PostgreSQL application-HA active-incident smoke also passed: one dispatched incident crossed application authority, accepted post-failover narrative and closure on the promoted node, remained intact when the original node later reacquired authority, retained both pre/post-failover activity and finished with a valid audit chain.

Evidence: `docs/ops/evidence/PASS-28-ACTIVE-INCIDENT-HA-EVIDENCE.json`.

## Cumulative regression

- Pass 25 destructive PostgreSQL application-HA regression: **PASS**. Exact 20-file migration ledger, exactly-one-writer, ambiguous commit recovery, provider reconciliation, planned drain/resume, old-primary fencing, pre-ledger backfill and migration tamper rejection remain green. Measured application crash failover in this run: **529 ms** (qualification measurement, not SLA).
- Pass 26 physical PostgreSQL HA/DR/PITR regression: **PASS** after rerunning on a fresh isolated port set. Witness/database partitions fail closed, physical promotion, unsafe-old-primary application fencing, both rewind paths, failback, restored streaming protection and PITR include/exclude boundary remain green. Measured physical failover **1.975 s**, failback **2.234 s** (qualification measurements, not SLAs).
- Pass 27 Production deployment/security regression: **PASS** including fail-closed auth/MFA/secret/TLS behavior, direct Kestrel TLS, trusted reverse-proxy semantics, sanitized readiness, protected admin preflight, HSTS/security headers, rate limiting and `systemd` validation.
- Process restart/backup/staged restore: **PASS**.
- Production qualification crash injection: **rejected as required**.
- CAD, Station, Operations Center, Station Display and Mobile startup smokes: **PASS**.
- All eight documented cross-platform publish targets: **PASS**. Deployment package contains 314 payload files and independently matches its per-file SHA-256 manifest. Deployment ZIP SHA-256: `f4466b10c570075d55e93646613f18df5d28a47e228fe39184cf05d730feca39`.

## Explicit boundaries

Pass 28 does **not** claim live vendor certification, target Windows Server SCM acceptance, Raspberry Pi hardware acceptance, native Android/iOS packaging, signed installer certification, a particular production PostgreSQL STONITH/quorum implementation, production RPO/RTO, load/soak/penetration acceptance, complete accessibility acceptance or agency operational acceptance. Those remain Pass 29/30 and target-environment work.
