# Performance and Capacity Targets

These are engineering SLO targets to design/test against, not production guarantees until validated on deployment hardware.

## Priority order under load

1. CAD safety-critical transactions
2. Realtime incident/unit collaboration
3. Station/Mobile operational delivery
4. 9-1-1/radio/provider emergency events
5. GIS/AVL/routing
6. search/reporting
7. admin/background work
8. high-volume situational feeds/analytics

Optional/high-volume features must not exhaust resources required by core CAD.

## LAN performance targets

- Create incident: < 500 ms p95
- Commit dispatch: < 750 ms p95
- Assign/reassign: < 500 ms p95
- Unit status change: < 500 ms p95
- Narrative commit: < 500 ms p95
- Realtime peer operational update: 250–500 ms p95
- Presence indicator: < 500 ms typical
- Internal propagation of received Critical radio/Mayday event: < 250 ms p95 target
- Open active incident: < 750 ms p95
- Exact ID/unit search: < 300 ms p95
- Common indexed search: < 1 s p95
- broad/full-text search: < 2 s p95

## Reconnection

- realtime reconnect target < 5 s under ordinary short outages;
- active-state reconciliation < 5 s typical;
- normal small backlog sync < 10 s;
- client becomes operational before low-priority historical work completes.

## Reference capacity direction

Design for at least:

- 1,000+ concurrent connected clients total;
- 100 Dispatch/Supervisor;
- 100 Station/wallboard;
- 500 Mobile/MDT;
- 5,000+ configured units/resources;
- 500+ active incidents;
- 250+ stations;
- 25,000+ personnel;
- 1,000,000+ premises/addresses;
- millions of historical incidents.

These are headroom targets, not licensing limits.

## AVL

Design for thousands of tracked units and burst ingestion of several thousand position messages/sec without freezing CAD clients. Coalesce rendering independently from durable observation ingestion.

## AIS

Design for 10,000+ known vessel objects and bursty thousands of messages/sec, geographically filtered/prioritized so remote irrelevant vessel traffic does not burden operational clients.

## GIS

- Incident Focus < 1 s typical
- layer toggle < 1 s typical
- locally cached CRG floor change < 1 s
- road closure commit to routing constraint use < 1 s normal target
- map interaction smooth at expected operational density

Use spatial indexing/tiling/server-side queries rather than pushing full datasets to clients.

## Workforce / DMS

- today's schedule open < 1 s typical
- staffing change commit < 500 ms
- staffing deficiency recalculation < 1 s
- daily roster PDF < 5 s typical
- DMS metadata search < 1 s
- typical full-text DMS search < 2 s
- workflow/approval commit < 500 ms typical
- signature finalization < 1 s server commit target, correctness prioritized.

## Load shedding

Never drop/delay first: CAD transactions, radio emergencies/Maydays, unit status, 9-1-1 events, station dispatch delivery.

Degrade first: AIS historical processing, traffic visualization refresh, camera thumbnails, analytics, background indexing, bulk exports, nonurgent admin jobs.
