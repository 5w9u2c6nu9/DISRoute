# DISRoute — Pass 14 Notes

**Date:** 2026-08-20  
**Base:** Pass 13 (`363bc56cf29efccc07e728cd30b1719edeba1ccf5594a3d9d4b51f03ed4effd1`)  
**Theme:** Windows CAD site intelligence, CRG floor selection, hydrant proximity and dispatcher road restrictions

## Implemented

- Added a server-authoritative incident site-intelligence projection that combines:
  - address-matched premise records;
  - currently published preplans;
  - registered CRG/TIFF assets;
  - hydrant/water-supply records;
  - active dispatcher road restrictions;
  - active incident/global map objects;
  - an incident scene point when one is available from an incident Point map object.
- Site intelligence does not invent geospatial certainty. When an incident scene point is absent, the API explicitly warns that hydrant/restriction proximity cannot be calculated and limits hydrant results to address-matched site records.
- When a scene point exists, hydrants are distance-ranked within 3 km and active road restrictions are distance-ranked within their radius plus a 5 km operational context window.
- Added `GET /api/incidents/{id}/site-intelligence` and typed Windows-client access.
- Added a CAD `SITE INTEL` workspace exposing premise hazards/access/fire-protection/utilities, published preplans, hydrant/water data, map objects and active road restrictions.
- Added the required CRG floor selector. Registered TIFF/CRG assets can be filtered by floor (including `UNSPECIFIED`) while retaining overlay/revision/rotation/scale/opacity/source metadata.
- Added CAD scene/map Point creation so dispatchers can establish authoritative incident coordinates without silently geocoding an address.
- Added dispatcher road-restriction creation with explicit latitude/longitude, radius and duration.
- Added road-restriction lifecycle completion via `POST /api/gis/road-restrictions/{id}/end`; ended restrictions disappear from live site intelligence.
- Historical Replay now reconstructs a road restriction as active when the selected historical timestamp falls within its start/end interval, even if the restriction was ended later.

## Regression coverage

`DISRoute.SelfTest` now proves:

- premise-to-incident address matching;
- published preplan inclusion;
- CRG floor asset inclusion;
- scene-coordinate extraction from incident GeoJSON Point objects;
- nearby hydrant distance calculation;
- road-restriction proximity calculation;
- road-restriction end lifecycle;
- removal of ended restrictions from current site intelligence; and
- historical replay preserves the restriction's active state for a timestamp before its later end.

## Verification completed

- Full 17-project .NET 10 Release build: **PASS — 0 warnings / 0 errors**.
- `DISRoute.SelfTest`: **PASS — incidents=3, units=6, audit=74, alerts=2**.
- Source verifier: **PASS — 98 hand-written C# files, 17 projects, 232 API routes, 13 migrations**.
- File-checkpoint runtime persistence/restart/idempotency/backup/restore/replay smoke: **PASS**.
- Linux/Xvfb startup smoke: **PASS for CAD, Station, Operations Center and Station Display**.
- Live PostgreSQL execution remains **NOT RUN** because this artifact environment has no PostgreSQL runtime.

## HA/DR preservation

No HA/DR, fencing, durable-idempotency, PostgreSQL authoritative publication-barrier, primary-only side-effect or historical incident replay behavior was removed or weakened. Dispatch Primary -> Dispatch Local Standby -> HQ DR remains unchanged.

## Still intentionally not claimed complete

This pass makes site intelligence operational in CAD but does not claim production GIS/provider completion. Address geocoding, production routing/traffic feeds, FileGDB hydrant import, target CRG/TIFF content storage/rendering, target-specific map engines, and target-agency GIS acceptance still require their selected providers/data and deployment qualification. Incident correction/relationships/merge-split, richer supervisor workflows and workspace/multi-monitor depth also remain to be completed.
