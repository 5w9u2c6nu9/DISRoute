# Observability, Client Monitoring, Clock Health, and Diagnostics

## Client connection monitoring

Track Dispatch, Station, Mobile/MDT, supervisor, wallboard/kiosk, gateway and service clients.

Per client/device expose:

- identity/type;
- user/device/station/unit scope;
- app version;
- connection state;
- last heartbeat;
- realtime/WebSocket health;
- round-trip latency where measurable;
- reconnect count;
- pending sync/backlog;
- last successful sync;
- clock offset where available;
- Normal/Degraded/Reconnecting/Offline-Cached/Offline/Syncing state.

A process being alive is not sufficient proof of a healthy client; application heartbeat and sync state matter.

## Historical connection timeline

Preserve meaningful disconnect/reconnect/degraded/sync events for troubleshooting and incident Replay correlation. Group site/provider/common-network failures to avoid alert storms.

## Clock monitoring

Monitor server, database host, clients, Station, Mobile/MDT, gateways and other endpoints where trustworthy measurements are possible.

Starting configurable thresholds may be:

- Healthy < 500 ms
- Advisory 500 ms–2 s
- Warning 2–5 s
- Critical > 5 s

These are defaults, not immutable policy.

Distinguish measured clock offset from suspected timestamp skew. Preserve source timestamp and normalization metadata rather than rewriting source evidence.

## Time service view

Operations Center shows primary/secondary time sources, endpoint synchronization counts, max drift, affected device/site, last sync, and common-source correlation.

## Performance telemetry

Track p50/p95/p99 latency for CAD commit, realtime delivery, database queries, search, provider calls; connected clients; event/outbox backlog; AVL/AIS ingest; CPU/memory/storage; replication lag; queue depth; cache health.

Alerts should react to sustained degradation rather than one slow request.

## Provider health

Every provider shows configured state, capability state, last successful interaction, latency/freshness, error reason, retry/circuit-breaker state, and affected operational functions.

## Starting heartbeat policy

A reasonable initial policy is client heartbeat around every 5 seconds, degraded suspicion around 15 seconds without healthy application heartbeat, and offline around 30 seconds, with faster transport-level disconnect detection when available. Profiles are configurable by client type/site.
