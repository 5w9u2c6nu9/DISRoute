# DISRoute Documents and Forms

## DMS goals

Centralized permissioned document management for policies/SOPs, forms, inspections, permits, preplans, training/personnel operational documents, investigations, after-action records and agency-defined material.

## Document lifecycle

Configurable states may include Draft, In Review, Changes Requested, Approved, Published, Superseded, Retired. Automatic historical versioning and side-by-side version comparison are desirable.

## Policy/publication workflow

Publication workflow is distinct from individual form approval. It may contain sequential/parallel reviewers, required approvers/signers, comments/change requests, due dates, reminders/escalations, publish/distribute actions, periodic review, and acknowledgement assignments.

## Signature semantics

Keep separate:

- acknowledgement: user confirms receipt/read;
- workflow approval: user authorizes next workflow step;
- approval signature: user formally approves exact version in a defined role;
- formal document signature: authenticated signature over exact immutable version.

Preserve signer identity, role, timestamp, document version/hash, signing meaning and authentication method. Require reauthentication/MFA where policy dictates.

A signed/finalized version cannot be edited. Changes create a new version/amendment and may require new approvals/signatures.

## Employee/user task queue

Unified `My Tasks` view may contain documents to acknowledge, documents to sign, workflow approvals, forms returned for change, periodic review, and other actionable tasks.

## Distribution and acknowledgement

Target users, roles, groups, stations, divisions or organizational units. Track delivered/opened/acknowledged where appropriate, due/overdue counts, reminders and compliance reporting.

## Structured forms

Forms are structured DISRoute data, not merely PDFs. Form designer direction includes fields such as text/date/dropdown/checkbox/person/station/unit/incident/premise/attachment/signature/repeating table.

Each form template can define its own approval/signature workflow independent from document publication.

Example states:

`Draft -> Submitted -> Supervisor Review -> Approved/Denied/Returned -> Finalized`

## Form workflow

Support:

- sequential and parallel approvals;
- all/any/N-of-M requirements;
- conditional routing from field values, station, role, amount, severity, category, etc.;
- chain-of-command/station routing;
- return for changes with comment;
- deny with reason;
- delegation/escalation;
- separation of duties;
- step-specific signature/attestation semantics;
- immutable finalized form version;
- amended/reopened version with full history.

Possible forms include apparatus/equipment requests, training requests, leave, personnel action, incident correction, after-action, inspection/permit approvals, purchasing/admin requests and agency-created forms.

## Integration

Documents/forms can link to incident, premise, inspection, permit, person, qualification, apparatus, station, investigation, training, after-action and records export. Universal Search respects permissions/classification.

## Retention/legal hold

Versioned retention classes, legal holds, controlled deletion/redaction and audited downloads/exports. External e-signature/certificate providers fit behind a neutral adapter.
