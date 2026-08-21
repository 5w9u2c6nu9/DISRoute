# Alerting, Notifications, and Messaging

## Alert philosophy

Interrupt only when a human decision/action is actually required. Correlate symptoms, deduplicate repeated states, and escalate progressively.

## Severity

- Critical — immediate safety/dispatch action required; persistent, audible, acknowledgement required.
- Warning — important issue requiring attention soon.
- Advisory — useful degraded/timing information, generally non-disruptive.
- Info — normal event/history.

Severity is based on operational impact, not simply provider type.

## State

- Active
- Claimed/Handling
- Acknowledged
- Resolved

Acknowledging means the user has seen the problem; it does not claim the problem is fixed.

## Correlation

A site/provider outage should generate one root alert with affected count rather than one alert per dependent client/resource. Individual resources still show local stale/offline state.

## Progressive timers

No-acknowledgement/response timers may move from normal -> advisory -> warning based on configurable incident priority/unit/policy thresholds. Avoid immediate noise.

## Mayday/radio emergency

Radio emergency activation and verbally declared Mayday are separate facts that can correlate to the same incident/person/unit. Critical presentation surfaces unit/person, crew, assignment, channel, location observations, active handler/acknowledgement, and provider state. Do not casually expose radio emergency clear/control unless a supported provider and explicit policy allow it.

## Role-aware routing

Dispatchers see operational failures; supervisors additionally see coverage/resource issues; administrators see certificates, backup, replication, disk, provider credentials, etc. Do not dump technical infrastructure warnings into dispatch unless operations are affected.

## Audible vocabulary

Keep the number of sounds small and recognizable: critical operational, warning, incoming operational message, normal info. Policy may restrict muting critical sounds.

## Messaging

Support:

- direct messages;
- operational channels;
- incident-scoped conversations;
- attachments/map references/preplan/CRG references;
- normal/important/urgent priority;
- delivery/read/acknowledgement state;
- retention/classification/audit.

Incident chat is separate from official narrative. Authorized users can promote a message into narrative with sender/time provenance.

Messaging is persistent digital coordination, not a replacement for mission-critical voice radio.

## Offline messaging

Messages may queue to offline clients and deliver on reconnect according to retention/security policy. Show queued/delivered/read states truthfully.

## Notification provider framework

Separate alert semantics from delivery channels. Providers may deliver push, email, pager/alert gateway or future notification mechanisms. A provider failure does not alter the authoritative alert/incident state, and normal chat delivery must not masquerade as mission-critical voice-radio delivery.
