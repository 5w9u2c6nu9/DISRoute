# DISRoute HA / DR runbook

## Supported authority order

1. Dispatch Primary
2. Dispatch Local Standby
3. HQ Geographic DR

Dispatch application peers point at the same writable PostgreSQL service and compete for one session-level advisory lock. Exactly one peer that owns that lock may acknowledge operational writes. Pass 25 additionally requires that every authoritative PostgreSQL checkpoint transaction hold a scoped HA commit lease, lock and match the durable `ha_cluster_state` node/instance/lease-epoch token, and revalidate the live advisory/witness session immediately before commit. This prevents local drain/lease release from racing an in-flight checkpoint and prevents a returned old primary from making a late commit after another node advances the epoch. All other peers are fenced read-only.

The HQ DR application does **not** automatically become authoritative by default. Before HQ can accept writes, the database HA layer must first promote the HQ PostgreSQL replica to a writable primary. Automatic geographic application promotion additionally requires a separately hosted PostgreSQL witness connection shared by both sites. DISRoute refuses automatic geographic promotion without that witness.

## Failure behavior

- Losing the application lease session—or failing its active `SELECT 1` I/O verification—sets the affected node to `Fenced` and stops write acknowledgement.
- Standby application nodes do not run delivery/escalation side effects and do not checkpoint stale in-memory state back into PostgreSQL.
- A promoted standby reloads the latest hash-validated PostgreSQL runtime checkpoint after recording its new lease epoch, then re-pings the lease/witness session and re-proves local database writability before `AcceptingWrites=true`.
- Outbox publication is primary-only.
- A failed or stale node is ignored when determining the active writer.
- Readiness (`/health/ready`) returns HTTP 200 only for the current writable application primary. A standby returns 503 by design so a writer/load-balancer target can remove it.

## Planned local switchover

The Operations Center/System Administrator may drain the current node through `POST /api/admin/ha/local/drain`. Drain waits for any already-entered authoritative PostgreSQL commit scope on that node to finish before releasing the application lease; once released, the node remains out of the election for the requested interval. A healthy Dispatch standby should acquire a new lease epoch and become ready. `POST /api/admin/ha/local/resume` returns the drained node to the election; it remains standby while another healthy node owns the lease.

Use the same idempotency-safe client behavior during a switchover as during any transient 503: retry the command against the newly ready node using the same idempotency key.

## Geographic failover

Geographic failover is intentionally two-layered:

1. Database/platform automation promotes the HQ PostgreSQL replica and independently verifies replication/recovery-point acceptability. DISRoute then proves the local endpoint is non-recovery, non-read-only and actually writable before application promotion.
2. DISRoute application authority is enabled at HQ. For automatic application promotion, configure an independent witness and explicitly enable `HighAvailability:AllowAutomaticGeographicPromotion`.

Never enable automatic geographic promotion on two independent PostgreSQL primaries without a shared witness/quorum mechanism. That configuration can split brain and is rejected by the intended deployment model.

## Validation

Run `scripts/postgresql-ha-qualification.sh` against a **disposable** PostgreSQL database before production rollout. The script requires `DISROUTE_PGQUAL_ALLOW_DESTRUCTIVE=YES_I_UNDERSTAND` because it resets the target schema. It exercises concurrent migration startup, exact source-to-ledger SHA-256 verification, exactly-one-writer election, crash-after-commit/idempotent retry, an observable stale-standby reload proof, ambiguous provider `Submitting` recovery, old-primary return, planned drain/resume, migration-ledger tamper/backfill, and optional replica/backup checks. `scripts/ha-smoke.sh` remains the smaller legacy local-switchover harness.

Production acceptance additionally requires PostgreSQL physical replication/failover/PITR drills, network-partition testing, witness-loss testing, database-level STONITH/quorum validation where used, and target-site load testing. DISRoute does not claim that application fencing alone can safely promote two independently writable geographic databases.

## Pass 26 physical PostgreSQL failover contract

For a deployment where Dispatch and HQ PostgreSQL members can be promoted onto independent writable timelines, set `HighAvailability:DatabaseTopology=PhysicalFailover` on every HA application node. DISRoute then requires an independent witness and `HighAvailability:DatabaseFencingMode=External`; startup fails closed otherwise.

The sequence remains **Dispatch Primary → Dispatch Local Standby → HQ DR**, but database and application promotion are separate operations. Before database promotion, the PostgreSQL HA layer must prove the old database primary fenced. Only after the local database is writable and out of recovery may a DISRoute node acquire the independent application witness, advance the durable lease epoch, reload the latest checkpoint, and set `AcceptingWrites=true`.

A returning old database primary must never be started as a peer primary. Rejoin it as a standby using the deployment's supported rewind/reseed procedure. Pass 26 qualified archive-assisted `pg_rewind` in both failover directions and PITR from a physical base backup plus archived WAL; production retention and archive durability must be sized and monitored accordingly.
