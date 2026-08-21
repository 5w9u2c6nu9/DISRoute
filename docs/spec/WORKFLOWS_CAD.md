# DISRoute CAD Operational Workflows

## 1. Call intake

- Hotkey/command opens New Call and focuses the first required field.
- Required fields are agency policy, not hard-coded globally.
- Location validation/search occurs while typing.
- Caller/callback, nature, priority, location, narrative, municipality, apt/floor, landmark/intersection/highway/coordinates and moving/approximate locations are supported.
- Incoming 9-1-1 data can prepopulate but never prevents dispatcher verification/correction.
- Duplicate detection recommends attach/merge/separate rather than forcing a choice.
- Multiple calls may attach to one incident.
- Premise/preplan/CRG/hazards surface without forcing navigation away from intake.

## 2. Incident create

- Server assigns authoritative incident number and commit time.
- Incident creation does not automatically tone/alert stations.
- Initial state snapshot and source provenance are durable.

## 3. Response recommendation

Response plans evaluate call type, location, priority, availability, apparatus capabilities, staffing/qualifications, special premise rules, mutual aid, time conditions, and policy. Recommendations remain dispatcher-controlled unless an explicit agency policy authorizes automation.

## 4. Dispatch Commit

One deliberate Dispatch action commits authoritative assignment/dispatch state and creates a durable DispatchAction ID. Downstream work fans out independently:

- station alert provider;
- Station display;
- run-sheet print;
- Mobile notification;
- radio/channel metadata;
- recording correlation metadata;
- other configured providers.

Delivery states are tracked independently. Retrying a failed printer or alert path must not duplicate assignments or already successful side effects.

## 5. Station delivery facts

Keep separate:

- assignment committed;
- alert requested;
- alert provider confirmed;
- Station display received;
- Station acknowledged;
- unit acknowledged;
- unit responding;
- AVL observed movement;
- unit on scene.

## 6. Unit response / AVL reconciliation

CAD status remains authoritative. AVL is observational. Examples:

- Assigned + moving observation -> advisory that unit appears to be moving.
- En Route + stationary at quarters past threshold -> advisory/warning.
- En Route + incident geofence observation -> suggest On Scene.

No silent state mutation by default.

## 7. Active incident operations

Support:

- command;
- divisions/groups;
- staging;
- additional alarms;
- mutual aid;
- channel assignment;
- unit reassignment;
- temporary map objects;
- personnel/crew association;
- emergency/Mayday events;
- simultaneous incidents;
- lightweight accountability foundation.

Complexity must be progressively disclosed so a normal call stays simple.

## 8. Fire watch / post-incident assignment

Fire Watch is a linked post-incident operational assignment, not a reason to keep the original emergency incident active forever.

Track unit/personnel, start, expected end, check interval, supervisor, relief history, notes/checks, and relationship to original incident. A unit on fire watch is committed unless policy explicitly allows otherwise. Rekindle can reopen or create a related incident per policy.

## 9. Clearing and closing

Clearing a unit is separate from closing an incident. Final-unit clear can offer Keep Active / Close Incident. Closure validation may warn about missing disposition/commander/reporting data but should not turn back-office paperwork into a CAD availability dependency.

Closing creates an immutable event/snapshot. Later corrections append history.

## 10. Additional alarm

Additional alarms are new dispatch actions/revisions on the same incident. Preserve which resources were part of each alarm and separately track delivery/print events.

## 11. Cancellation

Cancel dispatch creates an explicit cancellation event and sends cancellation/update messages to systems that received the original dispatch. Nothing is deleted.

## 12. Relocation / coverage

Relocation is separate from incident status. A unit may be Available while covering another station/area. Track home station, current coverage location, order/arrival time, reason, duration, and later incident assignment origin.

## 13. Road closure

Authorized dispatch/supervisor users can select/draw a road segment/intersection/area and create full/partial/directional/apparatus-specific restrictions with reason, source, start/end, and related incident. Once committed it propagates to all map clients and routing constraint caches in realtime.

## 14. Marine incident

Support waterway/coordinates/pier/bridge/shoreline/approximate or moving locations, marine response resources, AIS context, search areas, launch/access points, tides/current/weather when configured, and external/mutual-aid marine resources.

## 15. Incident close -> reporting

CAD closure is independent from reporting submission. CAD data prepopulates a neutral fire/EMS/reporting record. Provider/reporting outages never block CAD closure.

## 16. Merge / split / relate

Merge/split operations preserve original incident identities, call records, external IDs, narratives, attachments, and histories. Relationships are explicit and auditable.

## 17. Correction

Authorized corrections record original value, corrected value, actor, time, reason, and source evidence where applicable. Reports may use corrected values; Replay can show both original knowledge and later correction.
