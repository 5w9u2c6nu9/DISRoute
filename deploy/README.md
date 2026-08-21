# DISRoute deployment assets

DISRoute Server supports Windows Server and Linux. PostgreSQL is the production persistence target. The repository includes self-contained publish scripts plus Linux service/reverse-proxy templates and HA role examples.

## Development PostgreSQL

`docker compose -f deploy/docker-compose.dev.yml up -d` starts a disposable PostgreSQL target. The Server applies checked-in migrations automatically at startup; the mounted migration directory is retained for inspection/bootstrap compatibility.

## Production topology

The baseline authority order is:

**Dispatch Primary -> Dispatch Local Standby -> HQ Geographic DR**

Dispatch peers share the same writable PostgreSQL service and use DISRoute application fencing so only one server acknowledges operational writes. HQ DR does not automatically promote by default. Database promotion occurs in the database HA layer first; automatic geographic application promotion additionally requires an independent witness.

Example environment files are under `deploy/ha/`. Read `docs/ops/HA-DR-RUNBOOK.md` before enabling HA.

## Linux service

The self-contained Linux x64 publish places `DISRoute.Server` in the server package. Install it under `/opt/disroute/server`, set production configuration/secrets outside the package, and use the systemd template in `deploy/systemd/`.

Do not put database passwords, provider tokens, MFA signing material, certificate private keys, or LDAP bind credentials into the repository or image.
