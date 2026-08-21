# DISRoute Security Plan

## Identity

Support:

- native DISRoute identity;
- Active Directory/LDAP;
- Windows/Kerberos/Negotiate where appropriate;
- Entra/OIDC/OAuth2 identity;
- multiple external identity bindings to one DISRoute user.

Identity source is not the authorization model.

## MFA

Supported: TOTP authenticator, passkey/WebAuthn/FIDO2/security key, recovery codes. No SMS.

Break-glass local administrator is protected with strong MFA/passkey, restricted use, conspicuous audit and periodic test/review.

## Authorization

Fine-grained permission examples:

- Incident.View/Create/Close/Reopen/Correct
- Unit.Assign/Reassign/OverrideAvailability
- Station.View/Operate/ConfigureRadio/ConfigurePrinter
- Camera.View/PTZ
- Preplan.View/Edit/Publish
- GIS.Draw/Share/ManageClosures
- Radio.View/Listen/ManageProvider
- Documents.View/Approve/Sign/ManageWorkflow
- Workforce.View/ManageSchedule/ApproveLeave/ApproveTimesheet
- NERIS.Validate/Submit
- Security.ManageUsers/ManageRoles
- System.ManageProviders/ManageModules/ManageUpdates

Permissions combine role + agency + resource/station scope + effective-time assignment + temporary grant. Server enforces all sensitive operations.

## Temporary duty scope

Temporary station assignment can grant appropriate station operational rights for its effective period and revoke immediately on reassignment/end. Do not grant agency-wide permissions merely because a person is temporarily at a station.

Offline authorization uses time-limited signed leases/tokens so expired scope does not remain indefinitely valid on disconnected devices.

## Device identity

Station wallboards, Mobile/MDTs, gateways, service workers and other non-human clients use registered device identities, certificates/keys or equivalent strong credentials, revocation, version/health inventory and scoped permissions.

## Secrets

Credentials, private keys, API tokens, camera credentials, provider secrets and signing material are server-side protected secrets. No permanent raw credentials in client URLs/config. Support rotation and expiry warnings.

## Transport / network

TLS for network interfaces. Treat network location/VLAN as defense-in-depth, not authentication. Apply rate limits, request size limits, input validation, anti-CSRF where applicable, secure headers and least-privilege service accounts.

## Audit

Immutable/tamper-evident direction for critical audit/event history. Record actor, device/workstation, operation ID, source, time, previous/new value, reason and security context as appropriate. Audit break-glass access, exports, restricted-record access, permission/config changes, signatures, module toggles, provider changes and retention/legal hold operations.

## Sensitive data

Classification supports Public/Internal/Operational/Sensitive/Restricted. Apply need-to-know, role scope, break-glass, export/redaction, cache and retention policies.

## Mobile

Encrypted cache, remote session/device revocation, lost-device controls, biometrics/passkeys where supported, optional MDM/root/jailbreak policy, short-lived offline authorization, controlled CRG/preplan/document caching.

## Secure development

- dependency scanning;
- no ignored vulnerability warnings;
- static analyzers;
- threat modeling for provider adapters/SSRF/input files;
- signed installers/releases;
- least-privilege runtime identities;
- secrets scanning;
- security tests for authorization boundaries and tenant isolation.
