# DISRoute Station and Mobile

## Station platform split

DISRoute Station has two deliberately different deployment profiles. They share server contracts, station identity/scope, realtime delivery state, theming primitives where appropriate, and cached operational data, but they are not interchangeable.

### Full Station Workstation — Windows

The full firehouse workstation is a Windows-first interactive client. Depending on role/permissions it may support:

- active incident and apparatus/status views;
- acknowledgement and station operational actions;
- messaging;
- preplan/CRG navigation;
- station radio/feed selection from centrally approved sources;
- printer/run-sheet controls;
- camera/imagery interaction where authorized;
- station-scoped configuration;
- temporary-duty/effective permission changes.

The full Station workstation is not a Raspberry Pi target.

### Station Display / Wallboard — Raspberry Pi/Linux ARM64

Raspberry Pi is a first-class target only for read-mostly wall displays/information screens such as apparatus-bay, day-room, watch-room, or similar large-screen station displays.

The wall-display profile must:

- boot directly into a full-screen/kiosk DISRoute view;
- auto-start and recover via watchdog;
- reconnect automatically and reconcile current state;
- use a registered station/device identity;
- receive centrally managed layout/configuration;
- report version, heartbeat, clock status, connectivity, sync state, and health;
- cache the current incident and relevant station data so a short network outage does not blank the display;
- support centrally managed signed updates;
- clearly show offline/degraded/stale state;
- remain read-mostly by default with no assumption of full workstation actions or locally stored agency-wide credentials.

Raspberry Pi OS 64-bit / Linux ARM64 is the primary embedded target. Pi 4 and Pi 5 are candidate hardware classes; final minimum hardware is determined by performance testing.

### Normal station display

Configurable station layouts may include large map, active calls, apparatus/staffing, weather/warnings, cameras, approved radio-feed status, agency information, and other enabled modules/capabilities. Optional panels disappear when not configured or when their module is disabled.

### Incident takeover

When the station receives an assignment, the configured workstation/display automatically focuses the call and shows as appropriate for that device role:

- nature/priority;
- address/cross streets/location;
- assigned apparatus;
- elapsed time;
- operational narrative/notes appropriate for station display;
- map;
- preplan/CRG availability;
- critical hazards;
- channel information;
- configured camera/imagery panels.

### Station radio profile

Central Admin defines approved radio/stream providers and credentials. Authorized users on a full Windows Station workstation can choose which approved channels/feeds that firehouse uses, their order/favorites/default, local labels, output device and station presentation without receiving agency-wide provider credentials.

A Raspberry Pi wall display consumes only the centrally assigned presentation/feed configuration required for its display role; it is not the default station radio-configuration console.

A station may have its own approved local receiver/gateway in the future; this still plugs into the central provider model.

### Temporary station permissions

Effective station permissions can follow a temporary duty assignment and automatically expire/revert. Access is role + scope + effective time, not permanent base-role mutation. Full interactive actions are exposed on authorized Windows clients; a wall display remains read-mostly.

## Run-sheet printing

Event-driven durable job:

`StationAssignedToIncident -> generate -> queue -> primary printer -> fallback -> record outcome`.

Per station configure primary/fallback printers, copies, paper/orientation, updated-sheet policy, alert types, health, and manual test/reprint.

Operationally significant revisions may generate clearly marked `UPDATED` run sheets. Minor corrections do not automatically spam printers.

Restart/retry must not duplicate completed jobs. Printing does not depend on a Raspberry Pi wall display being the print host unless an explicit future deployment profile says otherwise.

## Station continuity

Once an incident is received, cache its operational package locally on applicable Station clients so brief central/network loss does not blank the wallboard/workstation. On reconnect, reconcile to current active state rather than replaying every intermediate view.

## Mobile

Touch-first and responsive; phones and rugged tablets can use different layouts while sharing the same operational model.

Required principles:

- large one-tap status controls;
- current incident dominates while assigned/responding;
- strong semantic status colors plus text;
- shallow navigation;
- map/preplan/CRG one tap away;
- crew/unit information;
- realtime CAD updates;
- secure offline cache/queued status changes;
- lost-device/session revocation controls;
- biometrics/passkeys where appropriate;
- optional MDM/root/jailbreak policy hooks.

### Mobile radio

Listen-only. No PTT/transmit controls. Icecast is the standard streaming backend design, with DISRoute authorization in front. Show only permitted feeds; source/stream health is visible. Incident channel shortcuts may be surfaced automatically when configured.

## Alert zones/groups

Station alert configuration may define zones/groups such as bunk areas, lights, speakers, printers or company-specific destinations when the selected alerting provider exposes those capabilities. Assignment rules determine which zones are activated; lack of acknowledgement may escalate according to agency policy, but DISRoute does not autonomously make safety-critical dispatch decisions unless explicitly configured.


## Pass 23 implementation status

The Mobile requirements above now have a server-authoritative field foundation: active Mobile/MDT device registration, effective-Workforce unit scoping, current incident/crew/Mayday/site-intelligence/DMS/chat/radio package, encrypted stale cache, explicit offline queue, signed unit-status authorization lease, stale-state conflict detection and a touch-first `DISRoute.Mobile` Avalonia UI. Realtime refresh does not auto-submit queued writes.

The current Mobile UI is validated through a desktop-hosted Avalonia harness. Native Android/iOS packaging/signing, target-OS secure storage/biometric UX, MDM/root/jailbreak controls, push/background delivery, production mobile map rendering/offline tiles, in-app audio and real-device acceptance remain required before Mobile can be considered a production phone/tablet release.
