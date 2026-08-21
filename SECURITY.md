# DISRoute Security

The authoritative security requirements are in `docs/spec/SECURITY.md`.

Do not weaken server-side authorization, tenant isolation, MFA policy, device identity, secrets handling, audit integrity, dependency-security gates, or provider isolation for implementation convenience.


## Pass 27 production controls

Production now fails closed unless PostgreSQL, authentication, HTTPS and MFA are enabled. Deployment secrets may be supplied through `secret://env` or `secret://file`; sensitive configured values that require a protected reference are rejected when supplied as plaintext. Unix secret/PFX permissions are validated.

Direct Kestrel TLS and explicitly trusted reverse-proxy TLS are supported. Production adds HSTS/security headers, Kestrel server-header suppression, fixed-window request limiting, sanitized anonymous readiness and generic unexpected-error responses. Administrative deployment readiness and upgrade preflight remain protected API surfaces. Physical PostgreSQL failover continues to require an independent application witness and external database fencing. See `docs/ops/PRODUCTION-DEPLOYMENT.md`.
