#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_ROOT:-/mnt/data/dotnet28}/dotnet"
PORT="${DISROUTE_E2E_PORT:-5128}"
BASE="http://127.0.0.1:${PORT}"
WORK="${DISROUTE_E2E_WORK:-/tmp/disroute-pass28-e2e}"
STATE="$WORK/disroute-state.json"
JOURNAL="$WORK/disroute-command-journal.ndjson"
SCENARIO_STATE="$WORK/scenario-state.json"
EVIDENCE="$WORK/pass28-operational-e2e.json"
LOG1="$WORK/server-before-restart.log"
LOG2="$WORK/server-after-restart.log"

rm -rf "$WORK"
mkdir -p "$WORK"

export DOTNET_ROOT="$(dirname "$DOTNET_BIN")"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/disroute-dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/tmp/disroute-nuget-packages}"
export NUGET_CERT_REVOCATION_MODE="${NUGET_CERT_REVOCATION_MODE:-offline}"
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="$BASE"
export Persistence__Provider=FileCheckpoint
export Persistence__CheckpointPath="$STATE"
export Persistence__JournalPath="$JOURNAL"
export Persistence__CheckpointSeconds=1
export Security__RequireAuthentication=false
export Security__RequireHttps=false
export Security__RequireMfa=false
export Security__RequireMutationHeader=false
# The HTTP provider is deliberately unreachable. The scenario proves an optional reporting
# provider can fail while the active CAD workflow remains available and recoverable.
export Integrations__FireReporting__Endpoint="http://127.0.0.1:1/neris"

SERVER_DLL="$ROOT/src/DISRoute.Server/bin/Release/net10.0/DISRoute.Server.dll"
[[ -x "$DOTNET_BIN" ]] || { echo "dotnet not found at $DOTNET_BIN" >&2; exit 2; }
[[ -f "$SERVER_DLL" ]] || { echo "Release server build not found at $SERVER_DLL" >&2; exit 2; }

PID=""
cleanup() {
  if [[ -n "${PID:-}" ]] && kill -0 "$PID" 2>/dev/null; then
    kill "$PID" 2>/dev/null || true
    wait "$PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

start_server() {
  local log="$1"
  (cd "$ROOT/src/DISRoute.Server" && "$DOTNET_BIN" "$SERVER_DLL" --urls "$BASE") >"$log" 2>&1 &
  PID=$!
  for _ in $(seq 1 120); do
    if curl -fsS "$BASE/health/ready" >/dev/null 2>&1; then
      return 0
    fi
    if ! kill -0 "$PID" 2>/dev/null; then
      echo "DISRoute.Server exited before becoming ready" >&2
      cat "$log" >&2
      exit 1
    fi
    sleep .25
  done
  echo "DISRoute.Server did not become ready" >&2
  cat "$log" >&2
  exit 1
}

stop_server() {
  if [[ -n "${PID:-}" ]] && kill -0 "$PID" 2>/dev/null; then
    kill "$PID"
    wait "$PID" 2>/dev/null || true
  fi
  PID=""
}

start_server "$LOG1"

python3 - "$BASE" "$SCENARIO_STATE" "$EVIDENCE" <<'PY'
import json, sys, time, urllib.error, urllib.parse, urllib.request
from datetime import datetime, timezone

base, state_path, evidence_path = sys.argv[1:]
results = []

def check(condition, message):
    if not condition:
        raise AssertionError(message)

def call(path, method="GET", body=None, expected=None):
    data = None if body is None else json.dumps(body).encode()
    headers = {"X-DISRoute-Request": "1"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(base + path, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=15) as response:
            raw = response.read().decode()
            payload = json.loads(raw) if raw else None
            if expected is not None and response.status not in expected:
                raise AssertionError(f"{method} {path}: expected {expected}, got {response.status}: {payload}")
            return response.status, payload
    except urllib.error.HTTPError as exc:
        raw = exc.read().decode()
        if expected is not None and exc.code in expected:
            try: return exc.code, json.loads(raw) if raw else None
            except json.JSONDecodeError: return exc.code, raw
        raise RuntimeError(f"{method} {path} -> HTTP {exc.code}: {raw}") from exc

def record(name, **details):
    results.append({"scenario": name, "status": "PASS", **details})

_, agencies = call('/api/agencies')
_, stations = call('/api/stations')
_, units = call('/api/units')
check(len(agencies) == 1 and len(stations) >= 2 and len(units) >= 4, "seed operational topology missing")
agency = agencies[0]
station = stations[0]
other_station = stations[1]

# Provision the development identity so chat/mobile actor-scoped services use the same identity
# contract as the clients instead of bypassing their membership rules.
call('/api/security/users/local-development', 'PUT', {
    'roles': ['Dispatcher', 'Supervisor', 'Administrator', 'RecordsReporting', 'Fire'],
    'totpEnrolled': False, 'passkeyEnrolled': False, 'breakGlass': True, 'state': 'Active'
})

# Scenario 1: routine call -> duplicate check -> recommendation -> assignment -> station delivery -> close.
_, routine_call = call('/api/calls', 'POST', {
    'agencyId': agency['id'], 'source': 'E911', 'providerCallId': 'PASS28-ROUTINE-001',
    'callerName': 'Routine Caller', 'callbackNumber': '555-2801',
    'suppliedLocation': '28 Routine Way', 'nature': 'Medical Assist', 'incidentId': None
})
status, routine = call('/api/incidents', 'POST', {
    'agencyId': agency['id'], 'stationId': station['id'], 'nature': 'Medical Assist', 'priority': 'Routine',
    'address': '28 Routine Way', 'crossStreets': 'Baseline Ave / Stable St', 'municipality': 'DISRoute Test',
    'callerName': 'Routine Caller', 'callerPhone': '555-2801', 'notes': 'Pass 28 routine workflow',
    'idempotencyKey': 'pass28:routine:create'
}, expected={201})
call(f"/api/calls/{routine_call['id']}/link", 'POST', {'incidentId': routine['id'], 'actor': 'pass28-dispatch'})
_, dupes = call('/api/cad/duplicate-check', 'POST', {
    'agencyId': agency['id'], 'nature': routine['nature'], 'address': routine['address'],
    'callerPhone': routine['callerPhone'], 'latitude': None, 'longitude': None
})
check(any(x['incidentId'] == routine['id'] for x in dupes), "routine duplicate detection did not return active incident")
_, rec = call('/api/cad/response-recommendation', 'POST', {
    'agencyId': agency['id'], 'incidentId': routine['id'], 'nature': routine['nature'],
    'priority': routine['priority'], 'stationId': station['id'], 'requiredCapabilities': []
})
check(rec['units'], "routine response recommendation returned no units")
routine_unit = rec['units'][0]['unitId']
_, routine = call(f"/api/incidents/{routine['id']}/assign", 'POST', {
    'unitId': routine_unit, 'expectedIncidentVersion': routine['version'], 'allowReassignment': True,
    'idempotencyKey': 'pass28:routine:assign'
})
_, routine_dispatch = call(f"/api/incidents/{routine['id']}/dispatch", 'POST', {
    'expectedIncidentVersion': routine['version'], 'unitIds': [routine_unit],
    'deliveryProviders': ['StationDisplay', 'RunSheet', 'StationAlert'], 'idempotencyKey': 'pass28:routine:dispatch'
})
check(len(routine_dispatch['deliveries']) == 3, "routine dispatch fan-out incomplete")
_, station_deliveries = call(f"/api/station/deliveries?stationId={station['id']}")
routine_station_delivery = next(x for x in station_deliveries if x['incidentId'] == routine['id'])
_, acked = call(f"/api/station/deliveries/{routine_station_delivery['id']}/acknowledge", 'POST', {
    'deviceId': 'pass28-station-device', 'actor': 'ignored-by-api'
})
check(acked['ackState'] == 'Acknowledged', "routine station acknowledgement missing")
_, snapshot = call(f"/api/station/{station['id']}/snapshot")
check(any(x['id'] == routine['id'] for x in snapshot['incidents']), "routine incident missing from station snapshot")
_, activity = call(f"/api/incidents/{routine['id']}/narrative", 'POST', {
    'text': 'Routine unit arrived and patient contact established.', 'idempotencyKey': 'pass28:routine:narrative'
})
check(activity['incidentId'] == routine['id'], "routine narrative not linked")
_, routine = call(f"/api/incidents/{routine['id']}")
_, routine = call(f"/api/incidents/{routine['id']}/close", 'POST', {
    'expectedVersion': routine['version'], 'reason': 'Routine workflow complete', 'idempotencyKey': 'pass28:routine:close'
})
check(routine['status'] == 'Closed', "routine incident did not close")
record('routine-call-to-close', incidentId=routine['id'], dispatchDeliveries=3)

# Scenario 2: prepare site intelligence, then run a multi-unit structure fire and deliberately
# leave it active across a real server process restart.
_, premise = call('/api/premises', 'POST', {
    'agencyId': agency['id'], 'name': 'Pass 28 Warehouse', 'address': '2800 Scenario Avenue',
    'occupancy': 'Warehouse', 'accessInformation': 'Knox box at Alpha entrance',
    'fireProtection': 'Wet sprinkler / standpipe', 'utilityShutoffs': 'Bravo-side exterior',
    'hazards': 'Propane cylinders in rear cage', 'classification': 'Operational'
})
_, preplan = call('/api/preplans', 'POST', {
    'agencyId': agency['id'], 'premiseId': premise['id'], 'title': 'Pass 28 Warehouse Preplan',
    'operationalContent': 'Alpha entrance; sprinkler FDC on Side A; roof access Side C.', 'author': 'pass28'
})
call(f"/api/preplans/{preplan['id']}/publish", 'POST', {'actor': 'pass28', 'notes': 'qualification'})
call('/api/crg-assets', 'POST', {
    'agencyId': agency['id'], 'premiseId': premise['id'], 'fileName': 'pass28-floor1.tif',
    'sha256': '28' * 32, 'floor': '1', 'overlayType': 'Floor', 'revision': 1,
    'rotation': 0, 'scale': 1, 'opacity': 1, 'source': 'Pass28'
})
call('/api/gis/hydrants', 'POST', {
    'agencyId': agency['id'], 'assetId': 'PASS28-H1', 'address': '2800 Scenario Avenue',
    'latitude': 40.0, 'longitude': -74.0, 'mainSizeInches': 8, 'staticPsi': 65,
    'residualPsi': 50, 'flowGpm': 1400, 'status': 'InService'
})

_, structure = call('/api/incidents', 'POST', {
    'agencyId': agency['id'], 'stationId': station['id'], 'nature': 'Structure Fire', 'priority': 'Emergency',
    'address': '2800 Scenario Avenue', 'crossStreets': 'Alpha St / Bravo St', 'municipality': 'DISRoute Test',
    'callerName': 'Warehouse Security', 'callerPhone': '555-2828', 'notes': 'Smoke from loading dock',
    'idempotencyKey': 'pass28:structure:create'
})
call('/api/gis/map-objects', 'POST', {
    'agencyId': agency['id'], 'incidentId': structure['id'], 'kind': 'IncidentLocation', 'name': 'Structure fire scene',
    'geometryGeoJson': '{"type":"Point","coordinates":[-74.0,40.0]}', 'visibilityScope': 'Incident', 'endsAtUtc': None
})
now = datetime.now(timezone.utc).isoformat().replace('+00:00', 'Z')
_, restriction = call('/api/gis/road-restrictions', 'POST', {
    'agencyId': agency['id'], 'name': 'Pass 28 fireground closure', 'description': 'Scenario Avenue closed for fireground operations',
    'latitude': 40.0, 'longitude': -74.0, 'radiusMeters': 150, 'startsAtUtc': now, 'endsAtUtc': None
}, expected={201})
_, site = call(f"/api/incidents/{structure['id']}/site-intelligence")
check(any(x['id'] == premise['id'] for x in site['premises']), "structure fire premise missing from site intelligence")
check(any(x['premiseId'] == premise['id'] for x in site['publishedPreplans']), "published preplan missing from site intelligence")
check(any(x['premiseId'] == premise['id'] and x['floor'] == '1' for x in site['crgAssets']), "CRG floor missing from site intelligence")
check(any(x['hydrant']['assetId'] == 'PASS28-H1' for x in site['hydrants']), "hydrant missing from site intelligence")
check(any(x['restriction']['id'] == restriction['id'] for x in site['roadRestrictions']), "road restriction missing from site intelligence")

_, current_units = call('/api/units')
available = [x for x in current_units if x.get('incidentId') is None][:4]
check(len(available) >= 4, "structure fire scenario requires four available units")
assigned_ids = []
for idx, unit in enumerate(available[:3], 1):
    _, structure = call(f"/api/incidents/{structure['id']}/assign", 'POST', {
        'unitId': unit['id'], 'expectedIncidentVersion': structure['version'], 'allowReassignment': True,
        'idempotencyKey': f'pass28:structure:assign:{idx}'
    })
    assigned_ids.append(unit['id'])
_, dispatch = call(f"/api/incidents/{structure['id']}/dispatch", 'POST', {
    'expectedIncidentVersion': structure['version'], 'unitIds': assigned_ids,
    'deliveryProviders': ['StationDisplay', 'RunSheet', 'StationAlert'], 'idempotencyKey': 'pass28:structure:dispatch'
})
structure = dispatch['incident']
# Additional alarm assigns the fourth available unit and creates another dispatch action.
_, additional = call(f"/api/incidents/{structure['id']}/additional-alarm", 'POST', {
    'expectedIncidentVersion': structure['version'], 'unitIds': [available[3]['id']],
    'deliveryProviders': ['StationDisplay', 'RunSheet'], 'actor': 'pass28-dispatch',
    'idempotencyKey': 'pass28:structure:second-alarm'
})
assigned_ids.append(available[3]['id'])
call('/api/incident-operations/command', 'POST', {
    'agencyId': agency['id'], 'incidentId': structure['id'], 'commandName': 'Incident Command',
    'unitId': assigned_ids[0], 'person': 'Pass 28 IC', 'actor': 'pass28-command'
})
_, group = call('/api/incident-operations/groups', 'POST', {
    'agencyId': agency['id'], 'incidentId': structure['id'], 'name': 'Division A', 'type': 'Division',
    'leader': 'Pass 28 Division Supervisor', 'channel': 'Fireground 2', 'memberUserIds': []
})
_, mutual = call('/api/mutual-aid', 'POST', {
    'agencyId': agency['id'], 'incidentId': structure['id'], 'requestedAgency': 'Neighbor Fire Department',
    'resourceType': 'Engine', 'quantity': 1, 'requestedBy': 'pass28-command'
})
call(f"/api/mutual-aid/{mutual['id']}/Accepted", 'POST', {})
_, mayday = call('/api/mayday', 'POST', {
    'agencyId': agency['id'], 'incidentId': structure['id'], 'unitId': assigned_ids[1],
    'person': 'Pass 28 Firefighter', 'location': 'Division A / Floor 2', 'channel': 'Fireground 2',
    'declaredBy': 'pass28-command'
})
call(f"/api/mayday/{mayday['id']}/acknowledge", 'POST', {'assignedTo': None, 'notes': 'RIT acknowledged'})

_, chat = call('/api/chat/channels', 'POST', {
    'ownerAgencyId': agency['id'], 'type': 'Group', 'name': 'Pass 28 Structure Fire', 'topic': 'Operational coordination',
    'stationId': None, 'incidentId': structure['id'], 'participatingAgencyIds': [agency['id']],
    'memberUserIds': ['local-development'], 'allowedRoles': ['Dispatcher', 'Supervisor', 'Fire'], 'operationalGroupId': group['id']
})
_, chat_message = call(f"/api/chat/channels/{chat['id']}/messages", 'POST', {
    'body': 'Division A reports extension to second floor.', 'priority': 'Important', 'classification': 'Operational',
    'linkedIncidentId': structure['id'], 'mentions': ['local-development'], 'attachments': [],
    'idempotencyKey': 'pass28:structure:chat:1'
})

_, dms = call('/api/documents/dms', 'POST', {
    'agencyId': agency['id'], 'title': 'Pass 28 Structure Fire Operational Worksheet',
    'classification': 'Operational', 'content': 'Initial incident worksheet for qualification scenario.'
})
_, dms_link = call(f"/api/documents/dms/{dms['id']}/links", 'POST', {
    'documentVersion': 1, 'entityType': 'Incident', 'entityKey': structure['id'], 'relationship': 'OperationalWorksheet'
})
check(dms_link['entityKey'].lower() == structure['id'].lower(), "DMS incident link mismatch")

# Flush while the structure fire is active, then restart the actual server process.
call('/api/persistence/flush', 'POST', {})
_, structure = call(f"/api/incidents/{structure['id']}")
check(structure['status'] != 'Closed', "structure fire unexpectedly closed before restart")
state = {
    'agencyId': agency['id'], 'stationId': station['id'], 'otherStationId': other_station['id'],
    'structureIncidentId': structure['id'], 'structureNumber': structure['number'], 'assignedUnitIds': assigned_ids,
    'chatChannelId': chat['id'], 'chatMessageId': chat_message['id'], 'groupId': group['id'],
    'mutualAidId': mutual['id'], 'maydayId': mayday['id'], 'premiseId': premise['id'],
    'restrictionId': restriction['id'], 'dmsId': dms['id'], 'results': results
}
with open(state_path, 'w', encoding='utf-8') as f:
    json.dump(state, f, indent=2)
print('Pass 28 pre-restart scenarios PASS')
PY

[[ -s "$STATE" ]] || { echo "Pass 28 checkpoint was not written" >&2; exit 1; }
[[ -s "$JOURNAL" ]] || { echo "Pass 28 journal was not written" >&2; exit 1; }
stop_server
start_server "$LOG2"

python3 - "$BASE" "$SCENARIO_STATE" "$EVIDENCE" <<'PY'
import json, sys, time, urllib.error, urllib.request
from datetime import datetime, timezone

base, state_path, evidence_path = sys.argv[1:]
state = json.load(open(state_path, encoding='utf-8'))
results = state['results']

def check(condition, message):
    if not condition:
        raise AssertionError(message)

def call(path, method="GET", body=None, expected=None):
    data = None if body is None else json.dumps(body).encode()
    headers = {"X-DISRoute-Request": "1"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(base + path, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=15) as response:
            raw = response.read().decode()
            payload = json.loads(raw) if raw else None
            if expected is not None and response.status not in expected:
                raise AssertionError(f"{method} {path}: expected {expected}, got {response.status}: {payload}")
            return response.status, payload
    except urllib.error.HTTPError as exc:
        raw = exc.read().decode()
        if expected is not None and exc.code in expected:
            try: return exc.code, json.loads(raw) if raw else None
            except json.JSONDecodeError: return exc.code, raw
        raise RuntimeError(f"{method} {path} -> HTTP {exc.code}: {raw}") from exc

def record(name, **details):
    results.append({"scenario": name, "status": "PASS", **details})

agency_id = state['agencyId']
incident_id = state['structureIncidentId']
_, ready = call('/health/ready')
check(ready['status'] == 'ready' and ready['checks']['mutationJournalDrained'], "server did not recover ready after active-incident restart")
_, structure = call(f'/api/incidents/{incident_id}')
check(structure['number'] == state['structureNumber'] and structure['status'] != 'Closed', "active structure fire did not survive restart")
_, commands = call(f'/api/incident-operations/command?incidentId={incident_id}')
check(any(x['commandName'] == 'Incident Command' for x in commands), "command state missing after restart")
_, groups = call(f'/api/incident-operations/groups?incidentId={incident_id}')
check(any(x['id'] == state['groupId'] for x in groups), "operational group missing after restart")
_, mutual = call(f'/api/mutual-aid?incidentId={incident_id}')
check(any(x['id'] == state['mutualAidId'] and x['state'] == 'Accepted' for x in mutual), "mutual aid state missing after restart")
_, site = call(f'/api/incidents/{incident_id}/site-intelligence')
check(any(x['id'] == state['premiseId'] for x in site['premises']), "site intelligence missing after restart")
_, chat_history = call(f"/api/chat/channels/{state['chatChannelId']}/messages?limit=50")
check(any(x['id'] == state['chatMessageId'] for x in chat_history), "incident chat missing after restart")
_, dms_links = call(f"/api/documents/dms/{state['dmsId']}/links")
check(any(x['entityKey'].lower() == incident_id.lower() for x in dms_links), "DMS operational link missing after restart")
record('active-incident-process-restart', incidentId=incident_id, preserved=['command','group','mutual-aid','site-intelligence','chat','dms-link'])

# Scenario 3: mobile/offline queue reconciles against the same still-active structure fire.
call('/api/security/users/local-development', 'PUT', {
    'roles': ['Dispatcher', 'Supervisor', 'Administrator', 'RecordsReporting', 'Fire'],
    'totpEnrolled': False, 'passkeyEnrolled': False, 'breakGlass': True, 'state': 'Active'
})
device_id = 'pass28-mobile-device'
call('/api/security/devices', 'POST', {
    'agencyId': agency_id, 'deviceId': device_id, 'name': 'Pass 28 Mobile', 'role': 'Mobile',
    'platform': 'Android', 'certificateThumbprint': None
})
unit_id = state['assignedUnitIds'][0]
_, mobile = call('/api/mobile/sync', 'POST', {
    'agencyId': agency_id, 'deviceId': device_id, 'sinceUtc': None, 'queuedOperations': [],
    'unitId': unit_id, 'offlineAuthorizationLease': None
})
package = mobile['operationalPackage']
check(package['currentIncident']['id'] == incident_id, "mobile package not scoped to active structure fire")
_, lease = call('/api/me/offline-lease', 'POST', {
    'agencyId': agency_id, 'userId': 'local-development', 'deviceId': device_id,
    'permissions': ['unit.change-status'], 'validMinutes': 30
})
observed = package['selectedUnit']
target_status = 'OnScene' if observed['status'] != 'OnScene' else 'EnRoute'
now = datetime.now(timezone.utc).isoformat().replace('+00:00', 'Z')
ops = [
    {'operationId': '28000000-0000-0000-0000-000000000001', 'kind': 'Narrative', 'incidentId': incident_id,
     'payloadJson': json.dumps({'text':'Pass 28 offline field narrative reconciled after restart.'}), 'occurredAtUtc': now},
    {'operationId': '28000000-0000-0000-0000-000000000002', 'kind': 'ChatMessage', 'incidentId': incident_id,
     'payloadJson': json.dumps({'channelId':state['chatChannelId'], 'text':'Pass 28 offline field chat reconciled.'}), 'occurredAtUtc': now},
    {'operationId': '28000000-0000-0000-0000-000000000003', 'kind': 'UnitStatus', 'incidentId': incident_id,
     'payloadJson': json.dumps({'unitId':unit_id, 'fromStatus':observed['status'], 'toStatus':target_status,
                                'observedUnitUpdatedAtUtc':observed['updatedAtUtc']}), 'occurredAtUtc': now}
]
_, reconciled = call('/api/mobile/sync', 'POST', {
    'agencyId': agency_id, 'deviceId': device_id, 'sinceUtc': None, 'queuedOperations': ops,
    'unitId': unit_id, 'offlineAuthorizationLease': lease['token']
})
check(all(x['state'] == 'Accepted' for x in reconciled['operationResults']), "mobile offline operations were not accepted")
_, duplicate = call('/api/mobile/sync', 'POST', {
    'agencyId': agency_id, 'deviceId': device_id, 'sinceUtc': None, 'queuedOperations': ops,
    'unitId': unit_id, 'offlineAuthorizationLease': lease['token']
})
check(all(x['state'] == 'DuplicateIgnored' for x in duplicate['operationResults']), "mobile operation idempotency failed")
record('mobile-offline-reconciliation', incidentId=incident_id, operations=3, duplicateReplay='DuplicateIgnored')

# Scenario 4: correction plus explicit relationship/merge/split workflows preserve history.
_, structure = call(f'/api/incidents/{incident_id}')
_, correction = call(f'/api/incidents/{incident_id}/corrections', 'POST', {
    'expectedVersion': structure['version'], 'field': 'CallerName', 'correctedValue': 'Warehouse Operations Manager',
    'reason': 'Caller identity confirmed by dispatch callback', 'actor': 'pass28-dispatch',
    'idempotencyKey': 'pass28:structure:caller-correction'
})
check(correction['originalValue'] != correction['correctedValue'], "incident correction did not preserve original/corrected values")

_, primary = call('/api/incidents', 'POST', {
    'agencyId': agency_id, 'stationId': state['stationId'], 'nature': 'Alarm Activation', 'priority': 'Routine',
    'address': '2810 Merge Way', 'crossStreets': '', 'municipality': 'DISRoute Test', 'callerName': 'Merge A',
    'callerPhone': '555-2810', 'notes': 'primary merge incident', 'idempotencyKey': 'pass28:merge:primary'
})
_, duplicate_incident = call('/api/incidents', 'POST', {
    'agencyId': agency_id, 'stationId': state['stationId'], 'nature': 'Alarm Activation', 'priority': 'Routine',
    'address': '2810 Merge Way', 'crossStreets': '', 'municipality': 'DISRoute Test', 'callerName': 'Merge B',
    'callerPhone': '555-2811', 'notes': 'duplicate merge incident', 'idempotencyKey': 'pass28:merge:duplicate'
})
call(f"/api/incidents/{duplicate_incident['id']}/relationships", 'POST', {
    'agencyId': agency_id, 'targetIncidentId': primary['id'], 'relationshipType': 'Duplicate', 'actor': 'pass28-dispatch'
})
_, merged = call(f"/api/incidents/{duplicate_incident['id']}/merge", 'POST', {
    'targetIncidentId': primary['id'], 'expectedSourceVersion': duplicate_incident['version'],
    'expectedTargetVersion': primary['version'], 'reason': 'Same alarm panel and caller location',
    'actor': 'pass28-supervisor', 'idempotencyKey': 'pass28:merge:execute'
})
check(merged['status'] == 'Closed', "merged source incident did not close")
_, primary = call(f"/api/incidents/{primary['id']}")
_, split = call(f"/api/incidents/{primary['id']}/split", 'POST', {
    'expectedVersion': primary['version'], 'nature': 'Elevator Entrapment', 'priority': 'Emergency',
    'address': '2810 Merge Way', 'notes': 'Separate event discovered during alarm investigation',
    'actor': 'pass28-supervisor', 'idempotencyKey': 'pass28:split:execute'
})
_, relationships = call(f"/api/incidents/relationships?incidentId={primary['id']}")
check(any(x['relationshipType'] == 'MergedInto' and x['sourceIncidentId'] == duplicate_incident['id'] for x in relationships), "merge relationship history missing")
check(any((x['relationshipType'] == 'SplitFrom' and x['sourceIncidentId'] == split['id']) or
          (x['relationshipType'] == 'SplitTo' and x['targetIncidentId'] == split['id']) for x in relationships), "split relationship history missing")
record('correction-merge-split', correctionId=correction['id'], mergedIncidentId=duplicate_incident['id'], splitIncidentId=split['id'])

# Resolve the Mayday before closing the structure fire, then close and hand off to RMS.
call(f"/api/mayday/{state['maydayId']}/resolve", 'POST', {'assignedTo': None, 'notes': 'Member located; Mayday resolved'})
_, structure = call(f'/api/incidents/{incident_id}')
_, structure = call(f"/api/incidents/{incident_id}/close", 'POST', {
    'expectedVersion': structure['version'], 'reason': 'Fire controlled; overhaul complete',
    'idempotencyKey': 'pass28:structure:close'
})
check(structure['status'] == 'Closed', "structure fire did not close after restart/mobile workflow")
_, rms = call(f"/api/rms/incident-reports/from-incident/{incident_id}", 'POST', {
    'agencyId': agency_id, 'reportType': 'FireIncident', 'summary': 'Pass 28 structure-fire end-to-end report',
    'idempotencyKey': 'pass28:rms:create'
})
_, validation = call(f"/api/rms/incident-reports/{rms['id']}/validation")
check(validation['canonicalReady'] is True, "closed structure fire not canonically ready for RMS")
_, rms = call(f"/api/rms/incident-reports/{rms['id']}/submit-review", 'POST', {
    'expectedVersion': rms['version'], 'notes': 'Pass 28 records review', 'idempotencyKey': 'pass28:rms:submit-review'
})
_, rms = call(f"/api/rms/incident-reports/{rms['id']}/review", 'POST', {
    'expectedVersion': rms['version'], 'decision': 'approve', 'notes': 'Pass 28 finalization',
    'idempotencyKey': 'pass28:rms:approve'
})
check(rms['status'] == 'Finalized', "RMS report not finalized")
_, neris = call(f"/api/rms/incident-reports/{rms['id']}/neris/build", 'POST', {
    'profileVersion': 'DISRoute-NERIS-Bridge-2026.1', 'idempotencyKey': 'pass28:neris:build'
})
_, neris = call(f"/api/rms/neris-submissions/{neris['id']}/queue", 'POST', {
    'providerKey': 'http-fire-reporting', 'idempotencyKey': 'pass28:neris:queue-failing'
})
for _ in range(100):
    _, neris = call(f"/api/rms/neris-submissions/{neris['id']}")
    if neris['state'] == 'Failed':
        break
    time.sleep(.1)
check(neris['state'] == 'Failed', "deliberately unreachable NERIS provider did not fail as expected")
# The provider failure must not block core CAD; create and close another incident while it remains failed.
_, degraded = call('/api/incidents', 'POST', {
    'agencyId': agency_id, 'stationId': state['stationId'], 'nature': 'Provider Degradation Probe', 'priority': 'Routine',
    'address': '28 Degraded Provider Way', 'crossStreets': '', 'municipality': 'DISRoute Test',
    'callerName': 'Qualification', 'callerPhone': '555-2899', 'notes': 'CAD must remain available',
    'idempotencyKey': 'pass28:degraded:create'
})
_, degraded = call(f"/api/incidents/{degraded['id']}/close", 'POST', {
    'expectedVersion': degraded['version'], 'reason': 'Core CAD remained available', 'idempotencyKey': 'pass28:degraded:close'
})
check(degraded['status'] == 'Closed', "provider failure blocked core CAD mutation")
_, neris = call(f"/api/rms/neris-submissions/{neris['id']}/retry", 'POST', {
    'providerKey': 'simulation-fire-reporting', 'confirmPossibleDuplicate': False,
    'idempotencyKey': 'pass28:neris:retry-simulation'
})
for _ in range(100):
    _, neris = call(f"/api/rms/neris-submissions/{neris['id']}")
    if neris['state'] in ('Accepted', 'Confirmed'):
        break
    time.sleep(.1)
check(neris['state'] in ('Accepted', 'Confirmed'), "NERIS retry through simulation provider did not recover")
record('rms-neris-provider-degradation', rmsReportId=rms['id'], nerisSubmissionId=neris['id'], recoveredState=neris['state'])

# Final whole-system audit/history checks.
_, activity = call(f'/api/incidents/{incident_id}/activity')
messages = [x.get('message','') for x in activity]
check(any('Pass 28 offline field narrative' in x for x in messages), "reconciled mobile narrative missing from incident history")
_, chat_history = call(f"/api/chat/channels/{state['chatChannelId']}/messages?limit=100")
check(any(x['body'] == 'Pass 28 offline field chat reconciled.' for x in chat_history), "reconciled mobile chat missing")
_, audit = call('/api/audit')
_, audit_verify = call('/api/audit/verify')
check(audit_verify['valid'] is True, "tamper-evident audit chain invalid after full scenarios")
actions = {x['action'] for x in audit}
for expected_action in ['CallCreated','CallLinked','DispatchCommitted','StationAcknowledged','CommandChanged','MutualAidRequested','MaydayDeclared','IncidentRelationshipCreated','IncidentsMerged','IncidentSplit']:
    check(expected_action in actions, f"expected audit action missing: {expected_action}")
_, journal = call('/api/persistence/journal-status')
check(journal['queued'] == journal['persisted'] and journal['pendingAuthoritativeBatches'] == 0, "mutation journal not drained after scenarios")
_, ready = call('/health/ready')
check(ready['status'] == 'ready' and ready['checks']['authoritativeWriter'], "server not ready after whole-system scenarios")
record('audit-and-durability-closure', auditRecords=len(audit), auditChainValid=True, journalDrained=True)

report = {
    'qualification': 'DISRoute Pass 28 operational end-to-end',
    'completedAtUtc': datetime.now(timezone.utc).isoformat().replace('+00:00','Z'),
    'serverMode': 'Development/FileCheckpoint process-restart qualification',
    'scenarios': results,
    'totals': {'scenariosPassed': len(results), 'scenariosFailed': 0},
    'nonClaims': [
        'This local process-restart harness is not a production RTO/RPO measurement.',
        'Authentication transport/AD/Windows Integrated Authentication is qualified separately from these development-mode operational scenarios.',
        'Live 911/radio/Eventide/NERIS vendor systems are not contacted; simulation/degraded provider paths are used.'
    ]
}
with open(evidence_path, 'w', encoding='utf-8') as f:
    json.dump(report, f, indent=2)
print(json.dumps(report, indent=2))
PY

echo "DISRoute Pass 28 operational end-to-end qualification PASS"
echo "Evidence: $EVIDENCE"
