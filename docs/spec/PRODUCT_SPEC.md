# DISRoute Product Specification

## Product vision

DISRoute is a modular multi-agency public-safety operations platform centered on fast, calm, collaborative dispatch. It combines CAD, Station, Mobile, GIS/routing, communications integration, workforce scheduling, documents/forms, records/back-office, supervision and system administration behind one secure event-driven platform.

## Platform baseline

- Full interactive operational workstations are **Windows-first**, including CAD dispatcher/call-taker, Supervisor, full Station workstation, Admin/Operations Center, and desktop RMS/Workforce/Documents workflows.
- Raspberry Pi/Linux ARM64 is reserved as a first-class target for **Station Display / wallboard / information-screen roles only**. It is not a replacement target for full Windows workstations.
- DISRoute Server/backend must support deployment on **Windows Server or Linux** with equivalent core behavior.
- Vendor-specific adapters may require a particular OS, but that requirement is isolated to the adapter/gateway.
- See `PLATFORM_TARGETS.md` for the authoritative role/platform matrix and packaging/testing requirements.

## DISRoute CAD

Required capabilities:

- keyboard-efficient Call Intake;
- structured/uncertain/moving/marine locations;
- duplicate caller/incident detection;
- response-plan recommendations;
- authoritative Dispatch Commit with independent provider delivery state;
- dense Resource Board with agency status language/color tokens;
- assignment/reassignment, additional alarms, mutual aid, command/groups/staging;
- unit response/status and AVL reconciliation;
- relocation/coverage;
- fire watch/post-incident assignments;
- incident closure/reopen/merge/split/corrections;
- premise/preplan/CRG context;
- shared operational GIS/map objects;
- road closures/restrictions;
- realtime multi-dispatcher collaboration;
- incident chat/activity/audit;
- supervisor overrides/coverage/handoff;
- Replay and records/after-action export.

## DISRoute Station

The full interactive Station workstation is a Windows client. A separate Station Display deployment profile targets Raspberry Pi/Linux ARM64 for read-mostly wall displays.

- per-station device identity and configuration;
- Windows full-workstation view and workflows according to role;
- Raspberry Pi/Linux ARM64 wallboard/status display profile;
- normal wallboard/status view;
- incident takeover on assignment;
- map/address/cross streets/nature/assigned apparatus/timers;
- relevant hazards/preplan/CRG;
- optional imagery/cameras/radio feeds;
- station-scoped radio profile selecting centrally approved feeds;
- station alert acknowledgement/status;
- event-driven run-sheet printing with fallback/reprint/update handling;
- local operational cache during brief network/core outages;
- configurable alert zones/groups for station devices/areas when supported;
- no anonymous/unscoped station URL;
- Raspberry Pi wall displays remain read-mostly and centrally managed; full station configuration/workflows remain on Windows workstations unless explicitly authorized through another client.

## DISRoute Mobile

- rugged touch-first responder workflow;
- large one-tap status controls;
- current-incident-first design;
- map/preplan/CRG;
- unit/crew/channel information;
- incident messaging;
- listen-only authorized radio streaming;
- offline encrypted cache/queued status reconciliation;
- remote session/device revocation and mobile security controls.

## DISRoute Maps

- provider-neutral base maps;
- operational incidents/units/stations/premises/hydrants/water network/cameras;
- preplans and multi-floor TIFF CRG overlays;
- AVL and historical tracks;
- traffic/congestion/crashes/construction/closures;
- traffic-aware apparatus-aware routing;
- dispatcher-entered shared road restrictions;
- weather/situational provider layers;
- marine/AIS traffic and search-area support;
- GeoJSON/KML/KMZ/WMS/WMTS/File Geodatabase import;
- shared incident map objects and Replay.

## DISRoute Radio

- vendor-neutral multi-provider RadioGateway framework;
- current conventional VHF/UHF deployment support, with Motorola APX 8000 portable-radio environment as an initial real-world integration target;
- emergency/unit-ID correlation to person/unit/incident;
- capability-driven P25 conventional/trunked, analog signaling, DMR/NXDN/TETRA/vendor gateway expansion;
- no assumption that GPS is available merely because a portable contains GPS;
- Mobile streaming is listen-only through authorized Icecast feeds;
- outbound radio control is separately permissioned and only through documented supported adapters.

## DISRoute Connect

Provider framework for 9-1-1/NG9-1-1, radio, Zetron FSA, Eventide NexLog DX Series recording integration, AVL, GIS/base maps, routing, traffic, street imagery, AIS, weather/tides, cameras, NERIS, CAD-to-CAD, notifications, payroll/time export, e-signature and future systems.

## DISRoute Workforce

Public-safety scheduling/time management:

- recurring rotations;
- daily station/unit staffing;
- minimum staffing/qualification rules;
- leave/PTO/call-outs;
- overtime/open shifts;
- shift trades;
- details/acting assignments/training;
- live schedule changes;
- employee self-service;
- timesheets/time categories;
- approvals;
- payroll-ready exports;
- printable current daily roster and optional same-day change history with `as of` timestamp.

## DISRoute Documents

- centralized role/classification-aware document repository;
- versioning and full-text search;
- policy/document review/publish/acknowledgement workflow;
- reusable structured form designer;
- form-specific approval/signature workflows;
- conditional/sequential/parallel routing;
- returned-for-change/deny/delegation/escalation;
- acknowledgements, workflow approvals, approval signatures and formal signatures as distinct concepts;
- immutable signed/finalized versions;
- legal hold/retention/audit;
- links to incidents/premises/inspections/permits/personnel/apparatus/training/investigations.

## DISRoute RMS

- fire reporting + NERIS adapter;
- historical NFIRS-to-canonical-to-NERIS migration/mapping support;
- premises/occupancies;
- prevention/inspections/violations;
- permits;
- hazmat;
- investigations;
- operational personnel/qualifications;
- apparatus/equipment maintenance;
- EMS operational extension;
- generic non-incident/agency-defined record types as optional future modules;
- police-specific deep workflows intentionally deferred to a dedicated future design.

## DISRoute Admin / Operations Center

- platform health and human-readable degraded state;
- client connection monitoring;
- clock synchronization/drift monitoring;
- database/replication/backups/restore tests;
- device/version inventory;
- provider/certificate/secret health;
- stations/resources/response plans/GIS/provider configuration;
- users/roles/scopes/MFA/devices;
- theme/workspace administration;
- module enable/disable per agency;
- staged configuration publish;
- support diagnostics;
- training/simulation;
- HA/DR status.

## Themes / branding

- Classic, Light, Dark built in;
- versioned third-party/agency theme package contract;
- agency logo uploaded/configured for desktop/admin branding without recompilation;
- user theme/density choice unless policy locks it;
- same information architecture across themes;
- operational semantic colors protected by accessibility/safety rules.

## Optional module behavior

When a module/provider is not enabled/configured, its panels/navigation/jobs/alerts disappear cleanly and the layout reflows. Existing data/config remains preserved unless explicitly archived/deleted.

## Localization and terminology

User-facing strings, statuses, forms, report templates and agency terminology must be configurable/localization-ready. Do not hard-code one agency's wording into domain logic.

## Data ownership

Agencies can export their operational records in documented usable formats subject to permissions, classifications, retention and audit. No feature requires direct database access by customers/clients.
