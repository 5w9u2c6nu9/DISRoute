# DISRoute Pass 29 — Load, Resilience, Security & Failure-Stress Qualification

**Scope:** deliberately stress and fault the cumulative Pass 28 product, fix defects exposed by qualification, preserve the prior HA/DR/deployment/operational guarantees, and avoid unrelated feature expansion.

## Hardening changes

- Health endpoints (`/health`, `/health/live`, `/health/ready`) are explicitly exempt from the global application rate limiter so an abusive client sharing a source address with a supervisor/load balancer cannot turn healthy nodes into false-negative health checks.
- Rate-limit rejection now returns `429`, `Retry-After: 60`, `Cache-Control: no-store` and a small structured response so clients have a deterministic backoff contract.
- Normal host cancellation no longer produces false failure logs from `AlertEscalationWorker`, `DispatchDeliveryWorker` or the NERIS submission loop.
- NERIS provider validation/submission cancellation caused by host shutdown is rethrown as cancellation rather than being persisted as a provider validation/submission failure.
- Pass 27 production-security qualification was updated to throttle a protected API endpoint instead of health probes and now verifies health-probe exemption, `Retry-After`, and explicit Production rejection of qualification crash injection.
- Added reusable `scripts/stress-qualification.py` / `scripts/stress-qualification.sh` and `scripts/postgresql-load-failover-qualification.py`.
- PostgreSQL startup now fails closed unless `server_encoding` is UTF8. This prevents operational Unicode (including run-sheet/narrative punctuation) from reaching a database that cannot safely represent DISRoute JSON/text. A deliberately initialized SQL_ASCII database was rejected at startup, and the active-incident HA continuity scenario was rerun successfully against a real PostgreSQL 18.6 UTF8 database.

## Pass 29 local stress qualification

The final isolated FileCheckpoint run passed all phases:

- 96 concurrent incident creates with no failed acknowledgements;
- 256 concurrent read requests;
- 32-way same-idempotency-key replay storm collapsed to one incident;
- 16-way optimistic update contention produced exactly one successful writer and 15 concurrency conflicts;
- acknowledged incident and narrative survived immediate SIGKILL and restart;
- malformed JSON returned 4xx, oversize body returned 413, overlong idempotency key failed closed;
- deliberate rate-limit exhaustion produced 429 + `Retry-After: 60`, while eight readiness probes continued to return 200;
- process RSS delta remained bounded in the local run and file-descriptor count returned with zero growth;
- mutation journal drained with zero failed entries.

Final checkpoint rerun measurements: create p50 62.46 ms, p95 306.79 ms, p99 320.87 ms; read p50 12.55 ms, p95 28.19 ms, p99 34.63 ms; RSS delta 85,596 KiB; FD delta 0. These are qualification-environment observations, **not production sizing numbers or SLAs**.

Evidence: `docs/ops/evidence/PASS-29-STRESS-EVIDENCE.json`.

## PostgreSQL active-load failover

A two-node shared-primary PostgreSQL application-HA test ran 80 retry-safe incident commands across an abrupt kill of the authoritative application node. Fifty commands required at least one retry in the final checkpoint rerun. All 80 commands were present exactly once after failover, the durable mutation journal drained, audit integrity remained valid, and the survivor became authoritative in 1,105.6 ms in this local run.

Evidence: `docs/ops/evidence/PASS-29-POSTGRESQL-LOAD-FAILOVER-EVIDENCE.json`.

## PostgreSQL encoding safety

A deliberately initialized SQL_ASCII PostgreSQL database was rejected before DISRoute became operational, with the startup error explicitly requiring UTF8. The same active-incident application-HA continuity scenario was then rerun against a PostgreSQL 18.6 database reporting `server_encoding=UTF8`; dispatch, Unicode-bearing run-sheet persistence, authority transfer, post-failover narrative, closure and authority reacquisition all passed.

Evidence: `docs/ops/evidence/PASS-29-POSTGRESQL-UTF8-EVIDENCE.json`.

## Cumulative HA/security regression

- Pass 25 PostgreSQL application-HA qualification: **PASS**; 20 immutable migrations, exactly-one-writer, ambiguous commit recovery, provider reconciliation, drain/resume, old-primary fencing, ledger backfill and tamper rejection remain green. Final checkpoint rerun crash failover measurement: 1,042 ms.
- Pass 26 physical PostgreSQL HA/DR/PITR qualification: **PASS**; witness/database partitions fail closed, physical promotion, old-primary application fencing, both rewind paths, planned failback, restored streaming protection and PITR include/exclude boundary remain green. Final checkpoint rerun measurements: 2,649 ms physical failover, 1,766 ms failback.
- Pass 27 Production security/deployment qualification: **PASS** after adapting its rate-limit assertion to the Pass 29 health-isolation contract.
- Pass 28 operational E2E and active-incident continuity remain covered by their existing harnesses and are rerun as final gates.

## Explicit boundaries

Pass 29 does not claim agency production capacity, Internet/WAN chaos certification, external penetration-test certification, live vendor load/conformance, target Windows Server or Raspberry Pi hardware endurance, signed installer acceptance, native Android/iOS release, a particular production PostgreSQL STONITH/quorum product, or production SLA/RPO/RTO guarantees. Those remain deployment/Pass 30 release-candidate acceptance items.

## Final checkpoint gates

On the frozen Pass 29 code before packaging:

- clean offline restore: **PASS**;
- full 18-project Release build: **PASS — 0 warnings / 0 errors**;
- SelfTest: **PASS — incidents=7, units=6, audit=145, alerts=3**;
- source verifier: **PASS — 124 C# / 18 projects / 313 API routes / 20 migrations**;
- Pass 29 FileCheckpoint stress/fault qualification: **PASS**;
- Pass 29 PostgreSQL active-load application failover: **PASS**;
- Pass 25 PostgreSQL HA regression on UTF8 PostgreSQL: **PASS**;
- Pass 26 physical PostgreSQL HA/DR/rewind/failback/PITR regression: **PASS**;
- Pass 27 Production security/deployment regression on UTF8 PostgreSQL: **PASS**;
- Pass 28 six-scenario operational E2E rerun: **6/6 PASS**;
- Pass 28 active-incident application-HA continuity on UTF8 PostgreSQL: **PASS**;
- runtime restart/backup/staged-restore regression: **PASS**;
- CAD, Station, Operations Center, Station Display and Mobile startup smokes: **PASS**;
- all eight documented production publish targets: **PASS**;
- deployment package integrity: **PASS — 314 payload files**, ZIP SHA-256 `e0425ffa15db7624bcb1abe23ed1db28535d8f825c9d2555e769c1b36ce04b98`.

All timing/resource figures above are qualification-environment observations, not production guarantees.
