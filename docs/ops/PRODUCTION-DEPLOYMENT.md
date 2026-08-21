# DISRoute Production Deployment

Pass 27 production deployment supports the authoritative DISRoute Server on Windows Server or Linux, Windows-first interactive clients, and Linux ARM64 Station Display/wallboards. This document is an operational deployment contract, not vendor certification.

## Server prerequisites

- .NET 10 runtime matching the published framework-dependent build.
- PostgreSQL with the Pass 25/26 persistence and HA requirements.
- A production identity source appropriate to the agency (Windows/AD Negotiate and optional secure LDAP group expansion are implemented).
- TLS either directly on Kestrel or on an explicitly trusted reverse proxy.
- Three pre-provisioned 32-byte Base64 key files for MFA secret protection, offline authorization leases, and MFA sessions.
- For `PhysicalFailover`: an independent witness and external database fencing/STONITH supplied by the deployment HA layer.

Production refuses to start when authentication, HTTPS or MFA are disabled, when PostgreSQL is not the persistence provider, when physical failover lacks independent witness/external fencing, when configured provider/witness/TLS passwords are plaintext instead of secret references, or when a reverse proxy is enabled without an explicit trusted proxy/network list.

## Secret references

DISRoute production configuration supports:

- `secret://env/VARIABLE_NAME`
- `secret://file/path`

Relative file paths resolve below `Security:Secrets:Directory`. On Unix, secret files must not grant group/other permissions. PostgreSQL connection strings, HA witness strings, provider bearer tokens, LDAP bind passwords, TLS PFX passwords and MFA key material can be injected without checking plaintext into source configuration.

`DISROUTE_CONFIG_FILE` may point to an external JSON configuration file. Linux packaging uses `/etc/disroute/appsettings.Production.json`; Windows packaging uses `%ProgramData%\DISRoute\config\appsettings.Production.json`.

## TLS modes

### Direct Kestrel TLS

Set:

- `Security:Tls:CertificatePath`
- optional `Security:Tls:CertificatePassword` as a `secret://` reference
- `Security:Tls:Port` (default 5443)
- `Security:Tls:MinimumRemainingDays` (default 14)

Production validates that the PFX exists, is loadable, is not near expiry, and on Unix is not readable by group/other. Kestrel suppresses its `Server` response header. Production responses use HSTS plus restrictive API security headers.

### Trusted reverse proxy TLS

Set `Security:ReverseProxy:Enabled=true` and configure at least one `KnownProxies` or `KnownNetworks` entry. DISRoute never clears the framework's trust list and blindly accepts forwarded headers. `X-Forwarded-Proto` is honored only from configured trusted sources.

## Linux service

Publish `server-linux-x64`, then as root:

```bash
./deploy/linux/install.sh /path/to/server-linux-x64
```

The package installs a hardened `systemd` unit using an unprivileged `disroute` account, restrictive filesystem access, `NoNewPrivileges`, empty capability sets, private temporary/device namespaces and a 45-second graceful stop window. Provision configuration and secrets before starting the service.

## Windows Server service

Publish `server-win-x64`, open elevated PowerShell, then:

```powershell
.\deploy\windows\Install-DISRouteServer.ps1 -PublishDirectory C:\staging\server-win-x64
```

`DISRoute.Server --windows-service` implements the Windows Service Control Manager protocol directly. The installer creates `DISRouteServer`, uses its virtual service account, applies service recovery policy and ACLs Program Files read-only while granting the service write access only to its ProgramData tree. The service defaults to Production when launched in SCM mode.

The Windows service host is source/build qualified in the Linux artifact environment; final Windows Server acceptance still requires a target Windows Server because SCM itself cannot be executed in this sandbox.

## Rate limiting and request protections

Production uses a configurable fixed-window global limiter (default 600 requests/minute per authenticated identity or source IP), request-body limits, mutation guard header, authorization/MFA middleware, no-store API responses, CSP/frame/referrer/permissions headers and server-header suppression.

## Operational status

Authenticated administrators can inspect:

- `GET /api/admin/deployment/status`
- `GET /api/admin/upgrade/preflight`

Deployment status reports safe configuration fingerprint, disk headroom, persistence/migration integrity, journal state, HA role/epoch, backup recency warning and provider reconciliation warning without returning secret values. Upgrade preflight blocks while the local node is not the authoritative writer, the journal is not drained, migration integrity is invalid, or any provider operation is still at the durable `Submitting` boundary.

Anonymous `/health/ready` intentionally returns only boolean readiness checks; database location, node IDs and raw exception text are not exposed.

## Release packaging

`scripts/publish.sh` / `scripts/publish.ps1` publish:

- server: Linux x64 + Windows x64;
- CAD: Windows x64;
- Station: Windows x64;
- Operations Center: Windows x64;
- Mobile desktop field harness: Windows x64;
- Station Display: Linux ARM64;
- LegacyBridge: Windows x64;
- deployment/service files and production runbooks.

`scripts/package-deployment.py` creates a deployment ZIP plus per-file SHA-256 manifest and tests the resulting ZIP.

Native Android/iOS Mobile packaging, signed installer certificates, agency MDM distribution and actual Windows/Raspberry Pi target acceptance remain release/deployment-environment work and are not falsely claimed by Pass 27.
