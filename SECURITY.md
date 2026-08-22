# DISRoute Security

The authoritative security requirements are in `docs/spec/SECURITY.md`.

Do not weaken server-side authorization, tenant isolation, MFA policy, device identity, secrets handling, audit integrity, dependency-security gates, or provider isolation for implementation convenience.


## Pass 27 production controls

Production now fails closed unless PostgreSQL, authentication, HTTPS and MFA are enabled. Deployment secrets may be supplied through `secret://env` or `secret://file`; sensitive configured values that require a protected reference are rejected when supplied as plaintext. Unix secret/PFX permissions are validated.

Direct Kestrel TLS and explicitly trusted reverse-proxy TLS are supported. Production adds HSTS/security headers, Kestrel server-header suppression, fixed-window request limiting, sanitized anonymous readiness and generic unexpected-error responses. Administrative deployment readiness and upgrade preflight remain protected API surfaces. Physical PostgreSQL failover continues to require an independent application witness and external database fencing. See `docs/ops/PRODUCTION-DEPLOYMENT.md`.


## Release-candidate supply chain

DISRoute 1.0.0-rc.1 pins the .NET SDK and all NuGet dependency graphs, generates a CycloneDX SBOM, requires deterministic deployment packaging, and runs online NuGet advisory audit in CI with known vulnerability warnings promoted to errors. Release signing remains an external deployment-identity requirement; no signing key or private certificate belongs in this repository.
