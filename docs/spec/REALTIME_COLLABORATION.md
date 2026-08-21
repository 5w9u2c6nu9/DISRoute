# Realtime Multi-Dispatcher Collaboration

Realtime collaboration is a top-level architecture requirement.

## Presence

When an incident is open, authorized users can see who else is viewing/working it and, where useful, the panel/field being edited. Presence is ephemeral and not treated as permanent audit history.

Examples:

- `JD — Call Intake`
- `ML — Narrative`
- `RS — Units`

## Collaborative free text

Narrative/operational-note fields may support CRDT/OT-style multi-user editing. Dispatchers should see remote typing/cursors with minimal visual noise. Persist meaningful revisions/checkpoints rather than every keystroke.

## Structured operational fields

Fields such as location, nature, priority, status, assignment, command, channel, close/reopen and cancellation use authoritative server transactions and version checks. Concurrent same-field edits never silently overwrite each other.

If a user commits from an obsolete version, show the current value, their proposed value, actor/time of the intervening change, and appropriate resolution choices.

## Unit assignments

Transient drag/hover presence may be shown, but only the completed server transaction matters. If two users attempt to assign the same resource to conflicting incidents, only one valid authoritative assignment can win; the other user receives a clear conflict and alternative action.

## Shared map collaboration

Committed incident map objects, road closures, staging, command posts, search areas, hazards, and similar objects propagate immediately to all authorized clients. CRG/floor-view presence may be shown as an ephemeral cue.

## Live activity strip

A compact live strip may show recent committed activity and collaboration cues, e.g. assignment, priority change, station delivery failure, or another dispatcher working a field. It is not a replacement for the durable Activity/Audit history.

## Offline/reconnect

Disconnected clients display an unmistakable degraded banner. Queue only operations permitted offline. On reconnect:

- fetch current revisions;
- deduplicate by operation ID;
- merge non-conflicting work;
- surface true conflicts;
- preserve occurred and received times;
- purge/restrict cached data if authorization scope changed.

## Soft ownership

Incidents may have a primary dispatcher/handler for coordination, but ownership is soft and never blocks authorized collaboration. Supervisors can reassign primary responsibility with audit history.
