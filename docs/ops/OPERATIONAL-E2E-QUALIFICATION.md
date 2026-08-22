# DISRoute Operational End-to-End Qualification

Pass 28 adds a live operational qualification harness that exercises DISRoute through the same HTTP command/query surface used by operational clients. The goal is not to claim vendor or agency acceptance; it is to prove that already-implemented bounded contexts remain coherent when combined into realistic workflows, including restart and authority-transfer boundaries.

## Run

Build the Release solution first, then run:

```bash
./scripts/operational-e2e-qualification.sh
```

The harness uses Development/FileCheckpoint mode in an isolated working directory and produces JSON evidence. It intentionally uses simulation/degraded provider paths rather than contacting production 911, radio, Eventide, NERIS or other vendor systems.

For application-node authority continuity on PostgreSQL, run the existing HA smoke against a disposable PostgreSQL database:

```bash
DISROUTE_HA_TEST_CONNECTION_STRING='Host=127.0.0.1;Port=...;Database=...;Username=...;Password=...' \
  ./scripts/ha-smoke.sh
```

## Qualified scenarios

1. **Routine call through close** — normalized E911-style intake, incident creation/linkage, duplicate check, response recommendation, unit assignment, Dispatch Commit fanout, Station acknowledgement, narrative and closure.
2. **Active structure fire across process restart** — premise/preplan/CRG/hydrant/road intelligence, multiple units, additional alarm, command, operational group, mutual aid, Mayday, incident-linked chat and DMS linkage survive an actual server stop/start while the incident is active.
3. **Mobile offline reconciliation** — an assigned device receives an offline package/lease, queues narrative/chat/unit-status operations, reconciles them, then safely ignores duplicate replay of the same operations.
4. **Correction / relationship / merge / split** — audited correction preserves original and corrected values, duplicate relationship is retained, merge closes the duplicate into the primary and split creates a separate linked incident without destroying history.
5. **RMS → NERIS degradation/recovery** — a closed CAD incident becomes a canonical RMS report, is reviewed/finalized, then its NERIS submission enters a deliberately unavailable provider path. Core CAD remains operational during the failure; the durable provider operation is later retried through the simulation provider and reaches Accepted.
6. **Audit and durability closure** — expected mobile/operational history is present, the audit hash chain validates and the journal is drained.

The PostgreSQL HA smoke additionally creates and dispatches one incident before planned application authority transfer, continues and closes that **same incident** on the promoted node, keeps the drained old writer fenced, then verifies both pre- and post-transfer activity plus audit integrity after authority later returns to the surviving original node.

## Pass 28 evidence

- `docs/ops/evidence/PASS-28-OPERATIONAL-E2E-EVIDENCE.json`
- `docs/ops/evidence/PASS-28-ACTIVE-INCIDENT-HA-EVIDENCE.json`
- `docs/ops/evidence/PASS-28-POSTGRESQL-HA-REGRESSION-EVIDENCE.json`
- `docs/ops/evidence/PASS-28-PHYSICAL-HA-REGRESSION-EVIDENCE.json`
- `docs/ops/evidence/PASS-28-PUBLISH-EVIDENCE.json`

## Boundaries

This qualification is deliberately conservative. The local process-restart scenario is not a production RTO/RPO measurement. The application HA scenario uses a disposable shared PostgreSQL primary and is distinct from the physical PostgreSQL promotion/rewind/PITR regression. Simulation/degraded provider paths do not certify live vendor systems. Windows Server SCM, Raspberry Pi hardware, native Android/iOS, signed installers, production penetration/load/accessibility testing and agency operational acceptance remain external qualification work.
