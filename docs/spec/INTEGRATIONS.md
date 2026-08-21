# DISRoute Integration and Provider Inventory

## Common provider contract requirements

Every provider should expose, where meaningful:

- configuration validation;
- capability discovery;
- health state and last successful interaction;
- source/provider identity;
- freshness/staleness;
- time provenance;
- retry/backoff/circuit breaker;
- idempotency/correlation key;
- diagnostics safe for export;
- permission/credential boundary;
- `NotConfigured`, `Available`, `Unavailable` state.

## Provider families

- Identity: native, AD/LDAP, OIDC/Entra.
- 9-1-1/NG9-1-1 call handling.
- Radio systems/gateways.
- Recording/playback.
- Station alerting: Zetron FSA and native/future providers.
- Printing.
- AVL/GPS.
- Base maps/GIS.
- Routing.
- Live traffic.
- Street imagery.
- AIS/marine traffic.
- Weather/tides/current.
- Video/ONVIF/RTSP.
- NERIS/fire reporting.
- Hospital/destination status.
- CAD-to-CAD/mutual aid/EIDO.
- Notifications/push/email/pager gateways.
- Payroll/time export.
- External document/e-signature/certificate providers.
- Object/file storage.

## Zetron FSA direction

Dedicated provider encapsulates the supplied FSA XML CAD SDK. Support station/resource addressing, alert state, resource allocation/deallocation, unit state, station state, output/PAS operation and other documented capabilities as needed.

Requested alert, confirmed station/provider state, Station display receipt, and unit response remain distinct facts.

Any PTT-related SDK capabilities remain tightly constrained and are not exposed to Mobile/general CAD by default.

## Video

ONVIF discovery plus direct IP/hostname entry from day one; manual RTSP fallback. Discovery is limited to administrator-approved camera networks/addresses. Credentials remain server-side. Logical camera identity is separate from physical endpoint. Support per-station placement, main/sub streams, PTZ capabilities/presets where authorized, health, and future event metadata.

## NERIS

Neutral FireReporting domain feeds a NERIS adapter. CAD prepopulates known dispatch/location/unit/timestamp data. Reporting can be Draft/Needs Completion/Ready/Validated/Submitted/Accepted or equivalent. Queue/retry provider failures. Submission failure never blocks CAD.

Historical migration may translate source reporting into canonical DISRoute records with explicit versioned mappings and warnings/manual review rather than silent guesses.

## CAD-to-CAD

Provider abstraction supports standards-oriented exchange where possible and proprietary partner adapters where required. Mutual-aid request/accept/resource/status exchange must preserve partner identifiers/provenance and never collapse outside-agency data into ambiguous local resources.

## Recording target

Initial real-world recording target is **Eventide NexLog DX Series**. Exact model/software version/API/MediaWorks capability remains to be verified. Keep the adapter independent from the recorder so DISRoute can associate/play authorized radio and telephone/9-1-1 recordings without becoming the recording system of record.

## Developer API and webhooks

Expose versioned authenticated APIs and outbound webhooks/event subscriptions for authorized future integrations. Provide sandbox/test modes and idempotency/correlation semantics. Direct database integration is not a supported integration contract.
