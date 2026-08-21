# DISRoute Maps, Routing, Traffic, Marine, Preplans, and CRGs

## GIS goal

DISRoute Maps is an operational GIS workspace rather than a decorative basemap.

## Layers

### Operational

- incidents
- units/AVL
- stations
- premises/preplans
- CRGs
- hydrants
- water mains/branch lines
- hospitals
- cameras
- road closures/restrictions
- command/staging/search/evacuation/LZ objects

### Situational

- live traffic/congestion
- crashes/road incidents
- construction/lane restrictions
- radar/weather alerts
- lightning/wildfire where configured
- AIS/marine traffic
- marine weather/tides/current where configured

### Custom/import

- Esri File Geodatabase
- GeoJSON
- KML/KMZ
- WMS/WMTS
- agency/provider feeds

Unknown source attributes should be preserved during import where practical.

## Water-network reference dataset

The supplied current File Geodatabase contains 4,412 water-main line features, 1,642 hydrant points, and 1,644 hydrant branch lines. The current main export does not contain reliable per-segment diameter values, so DISRoute must not infer them. If a later source contains a diameter attribute, the importer should map/update it without rebuilding the GIS from scratch.

## Layer behavior

- opacity/z-order
- refresh interval
- labels/clustering
- filtering
- per-user presets/agency defaults
- provider health/freshness/staleness
- provenance

Stale observations never look live.

## Incident Focus

Incident Focus suppresses irrelevant noise and emphasizes the active incident, responding units, hydrants/water network, hazards, preplan/CRG, cameras, closures, routes, and incident map objects. Marine calls emphasize vessels, marine units, search areas, shore/pier access, waterways, and marine conditions.

## Road closures/restrictions

Authorized users can create full closure, lane restriction, directional restriction, bridge/apparatus restriction, caution/advisory, planned/future, and emergency closures.

Fields include geometry/road segment, source, reason, related incident, start, expected end/expiry, direction/lane, severity, notes, creator, revision.

Commit behavior:

- realtime propagation to CAD, GIS, Station, Mobile, supervisor views;
- routing constraint cache updated immediately;
- audit/Replay event;
- automatic expiry or stale-closure review prompt;
- manual clear/update with history.

## Routing

Routing is provider-neutral and capability-driven. Recommended route/ETA may consider:

- traffic congestion;
- crashes/closures/construction;
- dispatcher-created restrictions;
- agency preferred/avoid routes;
- bridge height/weight;
- vehicle dimensions/weight;
- turn/road restrictions;
- temporary hazards;
- apparatus type/capabilities.

Traffic is advisory observational data with freshness/provenance. If traffic becomes stale/unavailable, routing falls back gracefully. Dispatchers/units can always override a recommendation.

Dynamic reroutes should be suggested with reason and time impact rather than silently changing the driver's route.

## Preplans

Preplans are globally stored by premise/building and surfaced wherever relevant. Support draft/published versions, effective dates, structured fire protection/access/hazard data, documents/images, audit, and preservation of historical revision for Replay.

## CRGs

- TIFF source for georeferenced tactical floor overlay.
- PDF defaults to preplan/document use.
- floor selector for multi-floor premises;
- hide selector for a single available floor;
- multiple overlay types on one floor;
- bulk TIFF upload with per-file floor selector and custom floor names;
- metadata optional when unavailable;
- original source authoritative;
- optimized tiles/previews for clients;
- encrypted controlled Mobile offline cache.

## Street imagery

Optional provider. `NotConfigured` means no panel and no asset requests. Available imagery respects provider licensing/attribution. Premises may store preferred heading/FOV where useful.

## Marine operations

Boats/watercraft are first-class operational units. Support marine capabilities, home berth/launch point, rescue/fire/EMS attributes, waterway incident locations, bridge/pier/terminal/shore references, moving incidents/search areas, mutual aid, and marine-specific map layers.

## AIS

Provider-neutral marine traffic abstraction supports external/network feed first and future local receiver. Features:

- MMSI/name/call sign/type where available;
- position/speed/course/heading;
- track/history;
- age/staleness;
- source provenance;
- deduplication/merge of same vessel across feeds;
- source priority/failover, e.g. local receiver primary, external secondary.

AIS never becomes authoritative CAD unit status.
