# DISRoute UI / UX Specification

## Core UX rule

DISRoute should reduce operator workload. Minimize clicks, modal dialogs, duplicate entry, technical error messages, alert fatigue, and ambiguous states. Prefer predictable keyboard-efficient workflows, progressive disclosure, smart defaults, and automatic handling of non-safety-critical repetitive work.

## Classic visual direction

The supplied desktop visual reference establishes the Classic theme direction:

- dense information-first workspace;
- thin borders and framed work areas;
- compact boxed fields;
- small functional typography;
- classic menu/command-strip character;
- grid/table-centric presentation;
- minimal wasted whitespace and decoration;
- strong single-screen operational coherence;
- colored status cells/rows used sparingly and meaningfully.

Do not reproduce obsolete technical limitations, awkward scaling, fixed-window constraints, or exact historical layout/button arrangement.

## Default CAD workspace

Factory layout should expose:

- Call Intake
- Incident Workspace
- Unit/Resource Board
- Active Incidents
- Map
- Live Activity
- Global Alert/health surface

All major panels are dockable/tabbable/resizable/floatable except intentionally global safety surfaces.

## Incident workspace

Persistent incident header includes incident number, nature, priority, address/location, key channels, command, elapsed time, assignments, collaboration presence, and active critical/warning banners.

Recommended tabs/panes:

- Overview
- Narrative
- Units
- Premise
- Preplan
- CRG
- Map
- Activity
- Attachments
- Reporting
- Marine/AIS context where applicable

Critical information is never hidden solely because a different tab is selected.

## Resource board

Dense configurable grid, with Compact/Standard/Expanded density options. Common columns:

`UNIT | STATUS | INCIDENT | ELAPSED | STATION | CREW | AVL | RADIO`

Support grouping/filtering by station, battalion/district, status, agency, resource type, capability, mutual aid, fire watch, OOS, stale AVL, etc.

## Status color semantics

Classic theme approximates the validated operational color language:

- On Air: green
- At Quarters: light/white
- Responding: red
- At Scene: light/white with red status text
- Off Duty: yellow
- Relocation: light blue/cyan
- Out of Service: black/dark with yellow text
- Code 55 / Temporarily Off Duty: magenta/pink

Use text/icon/label in addition to color.

## Quick command palette

A permission-aware `Ctrl+K` style command/search surface should support commands such as open incident, assign unit, change status, open premise, open CRG, fire watch, replay, and universal search.

## Alerts

Alerts are impact-based:

- Critical — immediate operational/safety action required.
- Warning — needs attention soon.
- Advisory — useful degraded/timing state.
- Info — normal activity/history.

Acknowledged, Claimed/Handling, and Resolved are separate states. Correlate common root causes and prevent alert storms.

## Error language

Dispatcher-facing messages explain operational impact, not raw exceptions. Technical details remain available to support/admin users.

## Accessibility

- keyboard alternatives for all critical actions;
- visible focus states;
- appropriate contrast;
- non-color status cues;
- configurable text/density within operational limits;
- audible critical alerts that cannot be casually disabled where policy requires.

## Mobile direction

The supplied tablet reference is interaction inspiration only. Preserve large one-tap status actions, dense at-a-glance data, strong semantic colors and shallow navigation; modernize heavily with responsive touch layouts, glove-friendly targets, current-incident-first design, map/preplan/CRG integration, offline cache, and realtime updates.

## Assigned status treatment

`Assigned` is an authoritative assignment fact even where the agency's traditional status-color legend does not define a unique color for it. Do not invent a historic color. The theme/agency status configuration supplies its visual treatment until a verified agency preference is defined.

## Localization

All user-facing labels and agency terminology should be resource-driven/localization-ready. Themes may change presentation, not workflow semantics.
