# Radio, 9-1-1, Recording, and Communications Integration

## Vendor-neutral radio framework

DISRoute must not depend on one vendor, band, or protocol. A stable RadioProvider/RadioGateway contract supports multiple adapters simultaneously.

Potential adapter families include conventional analog signaling, P25 conventional, P25 trunked, TETRA, DMR, NXDN, vendor console APIs, serial/GPIO/IP gateways, and future standards/providers.

### Canonical radio events

Examples:

- RadioEmergencyActivated
- RadioEmergencyCleared
- RadioUnitIdReceived
- RadioPttStarted / RadioPttEnded
- RadioChannelChanged / RadioTalkgroupChanged
- RadioPresenceChanged
- RadioStatusReceived
- RadioTextReceived
- RadioLocationObserved

Provider-native details remain available for diagnostics/audit while CAD consumes canonical events.

### Capability discovery

Each provider declares what it can actually deliver/control. UI must not show unsupported buttons. Outbound control such as emergency clear, channel change or PTT requires a documented supported interface, explicit configuration, permissions, interlocks, timeouts and audit.

### Current deployment direction

Current portable-radio environment is conventional VHF/UHF. Exact signaling and dispatch console/gateway path remains an integration-selection decision. The architecture must support emergency/unit ID mapping without assuming a trunked core.

### Radio/person/unit correlation

Map native radio identity -> physical radio -> current person -> current crew/unit -> incident/operational group. Effective assignment changes with shift/detail/crew movement.

## 9-1-1 / NG9-1-1

Dedicated provider-neutral gateway supports legacy E9-1-1/ANI-ALI style data and modern NG9-1-1 data.

Canonical call data may include:

- provider call ID;
- caller/callback;
- ANI/ALI where present;
- structured civic/geodetic location such as PIDF-LO;
- location confidence/provenance;
- PSAP/call metadata;
- text/media references where supported;
- call state/timestamps.

Support standards-oriented EIDO exchange for call-handling/CAD/CAD-to-CAD/responder data where applicable.

The call-handling system remains authoritative for the call itself. Gateway failure never stops manual CAD call intake.

## Recording integration

The external recording platform remains system of record for audio/telephone recordings. DISRoute stores correlation/association metadata and opens/requests authorized playback rather than duplicating all recordings by default.

Correlation hierarchy:

1. direct incident ID metadata if supported;
2. strong match using radio channel/talkgroup/phone ANI/agent + time window;
3. possible match requiring user attach/dismiss.

Include both radio and telephone/9-1-1 recordings where the recorder exposes them.

Replay can synchronize CAD events, AVL, station delivery, and recording references.

## Deployment affinity

The 9-1-1 gateway, radio gateway, and recording integration are Dispatch-site-only. They connect through a Dispatch-local service endpoint capable of following the active Dispatch core. If the whole Dispatch site is lost and HQ DR becomes core authority, these integrations are explicitly degraded/unavailable until Dispatch connectivity returns.

## Initial hardware context

The current portable fleet includes Motorola APX 8000 radios operating on conventional VHF/UHF rather than a trunked system. The first adapter must therefore be selected around the actual conventional dispatch console/base/consolette/gateway and channel signaling in use, while the canonical RadioProvider contract remains vendor-neutral.

## Initial recording target

Eventide NexLog DX Series is the planned recording integration target. Exact model/software/API is an open integration decision. Include both radio and telephone/9-1-1 recording associations when exposed by the installed recorder environment.
