# PostgreSQL Physical HA / DR Qualification

Pass 26 qualifies DISRoute against actual PostgreSQL physical streaming promotion, rewind, failback, independent application witnessing, network partitions, WAL archiving, and PITR.

## Safety model

`HighAvailability:DatabaseTopology=PhysicalFailover` changes the fencing assumptions. A PostgreSQL advisory lock stored on the local application database is **not** a shared fence after two database timelines can independently become writable. DISRoute therefore refuses physical-failover mode unless:

- `HighAvailability:WitnessConnectionString` points to an independent witness PostgreSQL service reachable by every application promotion candidate; and
- `HighAvailability:DatabaseFencingMode=External` declares that the PostgreSQL HA layer will fence the old database primary before promotion.

The DISRoute witness protects **application writer authority**. It does not implement database STONITH or PostgreSQL quorum.

## Harness

Run only against disposable infrastructure:

```bash
export DISROUTE_PGPHYS_ALLOW_DESTRUCTIVE=YES_I_UNDERSTAND
export DOTNET_ROOT=/path/to/dotnet
export DISROUTE_PGPHYS_PGROOT=/path/to/postgresql
bash scripts/postgresql-physical-ha-qualification.sh
```

The harness creates real Dispatch-primary, HQ-standby, witness, and PITR clusters; uses TCP proxies to remove individual database/witness paths; performs explicit database process fencing; promotes PostgreSQL; exercises unsafe old-primary return; uses archive-assisted `pg_rewind` in both directions; and validates an included/excluded PITR transaction boundary.

## Required production translation

The harness's `pg_ctl stop` is a deterministic qualification fence. Production must substitute a real HA manager/quorum/STONITH design that can prove the previous PostgreSQL primary cannot remain or become writable before another database member is promoted. Examples of implementation technology are intentionally not selected by DISRoute source code; deployment engineering must choose and validate one for the target environment.

Never enable `PhysicalFailover` merely because streaming replication exists. The independent witness and external database fencing requirements are fail-closed startup contracts.

## Evidence

The successful Pass 26 qualification evidence is checked into `docs/ops/evidence/PASS-26-POSTGRESQL-PHYSICAL-HA-EVIDENCE.json`. Timing fields are observed lab measurements only.
