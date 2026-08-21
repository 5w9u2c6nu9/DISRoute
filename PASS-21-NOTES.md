# DISRoute Pass 21 — DMS / Forms / Approvals operational depth

Date: 2026-08-20

Base checkpoint: Pass 20  
Base source SHA-256: `9bed8f8a364b1ed21ae10933d0d366b3663df39800134d582670ffc1273234a3`

## Scope

Pass 21 deepens the existing DMS/forms/approval skeleton into an operational document and forms workflow without redesigning the established DISRoute HA/DR topology or replacing legacy compatibility endpoints.

## Implemented

- Added canonical DMS document summaries and immutable revision records with per-revision SHA-256 content identity.
- Added Draft -> approval -> Published lifecycle with approval-gated publication by default; previously published revisions become Superseded while their content remains immutable.
- Added sequential approval startup for document revisions and stronger approval workflow validation, including known-target agency checks and authenticated actor attribution.
- Added station/role/user/agency distribution scopes, version-specific required-read assignments, read/acknowledgement receipts and overdue-read evaluation.
- Required-read acknowledgement is idempotent and enforces recipient identity except for the explicit local-development harness identity.
- Added draft-only external asset references carrying file name, content type, storage reference, classification and optional SHA-256. Published revision asset packages are locked against later mutation.
- Added document/entity links for incidents, RMS reports, apparatus/units, stations, forms and personnel references, with same-agency validation where a stable GUID-backed entity exists.
- Hardened form templates and instances with schema/value JSON validation, published-template requirements and incident/agency consistency checks.
- Added immutable hash-backed form signature records containing signer, meaning, template version, values SHA-256, source and signing timestamp. The legacy sign path now produces the same durable signature metadata.
- Hardened document/form/approval HTTP actions so actor identity comes from the authenticated server context rather than trusting client-supplied actor text.
- Expanded the Windows Operations Center Documents & Forms area into operational DMS, Forms and Approvals workspaces for document/revision creation, approvals/publication, distribution/required reads, assets, entity links, template/form workflows, signatures and workflow-step decisions.
- Added typed client API coverage for the new DMS/form/approval surfaces.
- Added portable checkpoint persistence and PostgreSQL projection for DMS revisions, distributions, read receipts, asset references, entity links and form signatures.
- Bumped portable state schema to 17 and added PostgreSQL migration `017_dms_forms_depth.sql`.
- Runtime smoke now creates and approves a two-version DMS policy, records a required-read acknowledgement, stores a linked external asset reference and incident link, strong-signs a form, then proves those states survive a real server restart and staged backup restore.

## Verification

- Full .NET 10 Release build: **PASS — 17 projects, 0 warnings, 0 errors**.
- SelfTest: **PASS — incidents=7, units=6, audit=116, alerts=2**.
- Source verifier: **PASS — 108 hand-written C# files, 17 projects, 288 API route mappings, 17 PostgreSQL migrations**.
- Runtime persistence/idempotency/backup/staged-restore/historical-replay/RMS/NERIS/Workforce/DMS smoke: **PASS**.
- Avalonia startup smoke: **PASS — CAD, Station, Operations Center and Station Display**.
- Live PostgreSQL execution: **NOT RUN in the artifact environment**; migration 017 and projection code compile, but production PostgreSQL target qualification remains required.

## Boundaries / not claimed complete

- Required-read expansion from AD/Entra groups, station memberships and roles is not automatic yet. Non-User RequiredRead distributions currently require explicit recipient identities until a directory/group resolver is configured.
- `FormSignatureDto` is immutable, audited and hash-backed signer evidence. Pass 21 does **not** claim qualified/regulated cryptographic e-signature, PKI or legal-signature-provider certification.
- DMS assets are external storage/DMS references plus metadata and optional content hashes. Binary object storage is intentionally not embedded in the portable state file.
- Branded PDF rendering, production document printing, retention/disposition/legal-hold engines and production records-storage qualification remain future work.
- DMS classification is persisted metadata in this pass; deeper content-encryption/key-policy and classification-enforcement controls remain security/admin hardening work.
- Existing legacy document/form routes remain for compatibility. The new DMS revision APIs are the canonical operational document path.
- No production live-PostgreSQL qualification is claimed from this artifact environment.

All work from Passes 8–20 remains preserved.
