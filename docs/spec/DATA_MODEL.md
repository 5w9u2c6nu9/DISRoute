# DISRoute Canonical Data Model

## Identity conventions

Every major entity has an immutable internal identifier independent from human-readable agency numbers. Prefer UUID/ULID-style IDs for distributed/offline-safe generation.

External identifiers are modeled explicitly with type, source/provider, value, effective time, and provenance.

## Agency / organization

- Agency
- Division/District/Battalion
- Station
- ResponseArea
- OperationalGroup
- User/IdentityBinding
- PersonnelRecord
- DeviceIdentity

## Incident and calls

### Incident

- IncidentId
- AgencyId
- DisplayNumber
- Nature/Type
- Priority
- LifecycleState
- LocationId/current location snapshot
- PrimaryDispatcherId optional
- CommandUnit/person optional
- Created/closed/reopened timestamps
- version/concurrency token

### CallRecord

A call is distinct from an incident. Multiple 9-1-1 or administrative calls can attach to one incident; one call can later generate or relate to more than one incident.

Fields include provider call ID, caller, callback, supplied location, call timestamps, recording references, source, disposition, and incident links.

### Incident relationships

- duplicate/merged-into
- split-from/split-to
- related
- rekindle
- transfer
- mutual-aid relationship

Merging/splitting never destroys the original records.

## Location

Support:

- civic address;
- apartment/unit/floor;
- intersection;
- landmark;
- highway/route position;
- coordinates;
- waterway/river segment;
- pier/terminal/bridge span;
- moving target;
- approximate point/area/search polygon.

Track source, validation result, confidence, coordinates, and effective history. Dispatcher corrections preserve the originally supplied location.

## Premise

- premise/building/structure hierarchy;
- occupancy/use;
- access information;
- fire protection systems;
- utility shutoffs;
- hazards;
- sensitive notes/classification;
- permanent vs temporary information;
- effective/expiration dates;
- linked preplans, CRGs, inspections, permits, documents, cameras, historical incidents.

## Preplan

Global/shared by premise. Fields include version, draft/published/retired state, author/source, effective time, revision, structured operational content, document references, and audit trail.

## CRGAsset

- source file ID/hash;
- premise/building;
- floor name/number optional;
- overlay type optional;
- revision/version optional;
- upload/source/author metadata;
- georeferencing/rotation/scale/opacity optional;
- optimized derivative references.

Missing optional metadata cannot invalidate an otherwise usable asset.

## Resource / Unit / Apparatus

### OperationalUnit

Examples: E1, L1, BC1, M1. Fields include unit type, home station, agency, dispatchability rules, current operational state, capabilities, current physical apparatus, and active assignments.

### PhysicalApparatus

Separate internal record for VIN, make/model, maintenance, engine/pump hours, AVL/radio hardware, equipment, and history. Normal display name may match the operational unit designation.

### ResourceCapability

Extensible key/value or typed capability model for pump, tank, aerial, ALS/BLS, foam, rescue, marine, draft, staffing requirements, qualified operator requirements, specialty teams, etc.

## Unit state

Core/agency-configurable states include the operational concepts represented by On Air, At Quarters, Responding, At Scene, Off Duty, Out of Service, and Code 55 (Temporarily Off Duty). Relocation is modeled as a coverage/location assignment rather than conflated blindly with availability.

Additional EMS/fire states may include Assigned, Transporting, At Hospital, Clearing, Command, Fire Watch context, Training, and policy-defined variants.

Every state change carries source, occurred/received/committed times, actor/device/provider, operation ID, and correction history.

## Assignment

Assignments are explicit objects linking unit/resource to incident, station/coverage location, fire watch, training, or other operational commitment. Incident assignment and coverage relocation are separate concepts.

## Personnel and duty

- Person/PersonnelRecord
- permanent station/rank
- current duty assignment
- temporary detail
- acting rank/duty role
- unit crew assignment
- qualification/certification
- radio assignment
- effective start/end or until-relieved

Home assignment, current duty station, security scope, and operational role are related but not identical.

## Radio identity

- RadioDevice
- NativeRadioId
- Provider/System
- Channel/talkgroup capabilities
- Current person assignment
- Current unit/crew context
- history/effective time

## GIS operational objects

- RoadClosure/Restriction
- StagingArea
- CommandPost
- LandingZone
- EvacuationZone
- SearchArea
- Roadblock
- HazardMarker
- CustomPoint/Line/Polygon

All have creator/source, effective time, expiry, visibility scope, incident relationship, geometry, version, and audit history.

## Marine

- VesselObservation keyed by MMSI/other identity where available
- MarineUnit capabilities
- AIS source/provenance/freshness
- track points/history
- marine incident location/search area
- launch/berth/pier/channel/bridge GIS assets

## Alert

- AlertId
- Type/severity
- Correlation key/root cause
- affected entity/site/provider
- created/escalated/acknowledged/claimed/resolved state
- actor/handler
- actions
- durable history

Acknowledged does not equal resolved.

## Messaging

- DirectConversation
- Channel
- IncidentConversation
- Message
- Attachment/reference
- delivery/read/ack state
- priority
- classification/retention

Messages remain separate from incident narrative; authorized users may promote a message into narrative with provenance.

## DMS / forms

- Document
- DocumentVersion
- WorkflowTemplate
- WorkflowInstance
- WorkflowStep
- Approval/Attestation/Signature
- Distribution
- AcknowledgementTask
- StructuredFormTemplate
- FormInstance/FormVersion
- FormFieldValue

Signed/finalized versions are immutable.

## Workforce

- ShiftTemplate/Rotation
- Schedule
- DutyAssignment
- OpenShift
- OvertimeOpportunity/Offer/Assignment
- LeaveRequest
- ShiftTrade
- CallOut
- ActingAssignment
- Timesheet/TimeEntry
- LeaveBalance optional
- StaffingRule/QualificationRule
- ScheduleChangeEvent

## Timing

For externally sourced or safety-important facts, support:

- `OccurredAtUtc`
- `SourceTimestamp`
- `ReceivedAtUtc`
- `CommittedAtUtc`
- source timezone/clock quality when needed
- measured/suspected clock offset

Never overwrite original timestamps when applying a normalization/correction.

## Classification

Data/records may be classified as Public, Internal, Operational, Sensitive, or Restricted. Classification feeds authorization, export/redaction, retention, cache, and audit policies.
