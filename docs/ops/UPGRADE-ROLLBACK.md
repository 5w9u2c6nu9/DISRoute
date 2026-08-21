# DISRoute Upgrade and Rollback Runbook

## Before upgrading

1. Verify a recent backup and the PostgreSQL HA/DR layer is healthy.
2. Call `GET /api/admin/upgrade/preflight` on the authoritative application writer.
3. Do not proceed if the preflight reports a blocking failure.
4. Resolve any provider operation still in `Submitting`; review `NeedsReconciliation` warnings.
5. In HA, drain one application node at a time. Never upgrade all eligible writers simultaneously.
6. Preserve the current published application directory as an application rollback copy.

## Database migration rule

Published PostgreSQL migrations are immutable and SHA-256 sealed. Startup rejects altered historical migrations or a replicated ledger that does not match source. A binary may move a database forward through published migrations; application rollback across a schema change is allowed only when the older binary explicitly supports the resulting schema. Do not attempt to "roll back" by editing or deleting migration-ledger rows.

Pass 27 does not add a new schema migration; schema version remains 20.

## HA rolling sequence

For Dispatch-local peers sharing one database:

1. upgrade the standby;
2. start it and verify it remains healthy/standby;
3. drain the current primary;
4. verify the upgraded node becomes authoritative;
5. upgrade the former primary;
6. resume it as standby.

For physical PostgreSQL failover, application rolling upgrade does not replace database HA procedure. Maintain independent witness and external database fencing exactly as qualified in Pass 26.

## Windows service update

`deploy/windows/Update-DISRouteServer.ps1` stops the service, copies the existing binary directory to a timestamped rollback directory, deploys the new publish output, then restarts the service. If deployment/start fails, it restores the prior binary directory. Configuration/data live outside Program Files and are not replaced.

## Linux update

Stage the new publish directory, run upgrade preflight, stop/drain the service as appropriate, atomically replace `/opt/disroute/server` from the staged release, and start the unit. Keep the previous application directory until post-upgrade health and smoke checks complete.

## Rollback boundary

Application-file rollback is not database PITR. If an upgrade includes an irreversible schema/data migration and the previous binary cannot understand that schema, use the documented database backup/PITR recovery process and treat it as a coordinated recovery event. Never run two application releases against incompatible schemas in the same authoritative cluster.
