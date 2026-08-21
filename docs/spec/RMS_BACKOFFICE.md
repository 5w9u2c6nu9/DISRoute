# DISRoute RMS and Back-Office Modules

## Principle

RMS modules share identities, premises, incidents, people, apparatus, documents, search, classifications, audit and provider framework. Do not build disconnected data silos.

## Fire reporting

Neutral reporting domain prefilled from CAD. User completes remaining report fields, validates, routes review if required, then submits through the configured reporting provider. Provider failure is asynchronous and never reopens/blocks CAD.

## Premises / occupancies

Structured occupancy/property, fire protection, access, utilities, hazards, documents, temporary operational notices, history, preplans and CRGs. Temporary information has effective/expiration dates to prevent permanent warning clutter.

## Fire prevention / inspections

Configurable templates/checklists by occupancy/type, scheduled/assigned inspection, violations/deficiencies, photos/attachments, code references, correction due dates, reinspection, compliance/closure, signatures and history.

## Permits

Agency-defined permit types and workflow: application -> review -> inspection if needed -> approve/deny -> issue -> expire/revoke. Optional payment provider may be added later.

## Hazmat

Structured material, UN/NA/CAS references where available, quantity/container/storage location, classification, SDS references, contacts and verification date. CAD receives concise operational responder information rather than full regulatory detail unless opened.

## Investigations

Generic investigation module for incident-related investigative work. Support investigator assignment, scene/evidence references, interviews/notes, photos/docs, persons/organizations, related incidents, timeline, disposition, external case numbers, and restricted sections.

## Personnel administration

Personnel record is separate from login identity. Store operationally relevant assignment/rank/status/contact/qualifications/training/equipment/radio history. Avoid turning DISRoute into payroll/benefits/medical HRIS unless explicitly scoped later.

## Apparatus maintenance / equipment

Physical apparatus record supports maintenance, hours/mileage, inspections, equipment, service/OOS history, AVL/radio device linkage. Equipment inventory focuses on operational equipment rather than warehouse ERP; support serials, assignment, inspection/service/expiry/OOS and future QR/NFC.

## EMS extension

Support EMS response levels, patient count, destination assignment, Transporting/At Hospital states and multiple transporting units/patients at the CAD operational level. Clinical ePCR remains an external system/provider boundary.

## MCI readiness

Base incident model supports multiple locations/search/triage/staging areas, large mutual-aid lists, multiple patients/destinations, and branch/division/group structure so a future MCI module does not require replacing the incident core.

## Law-enforcement scope

Deep police-specific workflows remain outside the initial implementation and require a dedicated future design/security review. Do not improvise law-enforcement data flows simply because the architecture can support another domain.

## Historical reporting translation

Where historical NFIRS records are migrated, use explicit versioned `NFIRS -> DISRoute canonical -> NERIS` mappings. Preserve provenance and require warning/manual review for non-1:1 mappings; never silently invent a NERIS value.

## Other optional legacy-function equivalents

Generic non-incident records, awards/service records, fund-drive/community-program records, and other agency-defined back-office areas may be implemented as optional modules/forms when an agency still needs them. They are not allowed to distort the core CAD domain.
