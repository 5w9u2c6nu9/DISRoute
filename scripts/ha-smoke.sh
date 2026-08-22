#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_ROOT:-/mnt/data/dotnet10}/dotnet"
SERVER_DLL="$ROOT/src/DISRoute.Server/bin/Release/net10.0/DISRoute.Server.dll"
PG="${DISROUTE_HA_TEST_CONNECTION_STRING:-}"
PORT_A="${DISROUTE_HA_TEST_PORT_A:-5111}"
PORT_B="${DISROUTE_HA_TEST_PORT_B:-5112}"
WORK="${DISROUTE_HA_TEST_WORK:-/tmp/disroute-ha-smoke}"
STATEFILE="$WORK/active-incident.json"

[[ -n "$PG" ]] || { echo 'Set DISROUTE_HA_TEST_CONNECTION_STRING to a disposable PostgreSQL database.' >&2; exit 2; }
[[ -f "$SERVER_DLL" ]] || { echo "Build DISRoute.Server Release first: $SERVER_DLL not found" >&2; exit 2; }
rm -rf "$WORK" && mkdir -p "$WORK"

PID_A=""; PID_B=""
cleanup() {
  for pid in "$PID_A" "$PID_B"; do
    [[ -n "$pid" ]] && kill "$pid" 2>/dev/null || true
  done
}
trap cleanup EXIT

start_node() {
  local node="$1" port="$2" log="$3"
  env \
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="http://127.0.0.1:$port" \
    Persistence__Provider=PostgreSQL \
    ConnectionStrings__DISRoute="$PG" \
    HighAvailability__Enabled=true \
    HighAvailability__NodeId="$node" \
    HighAvailability__Site=Dispatch \
    HighAvailability__ConfiguredRole=LocalStandby \
    HighAvailability__HeartbeatSeconds=1 \
    HighAvailability__StaleNodeSeconds=4 \
    Security__RequireAuthentication=false \
    Security__RequireHttps=false \
    Security__RequireMfa=false \
    Security__RequireMutationHeader=false \
    "$DOTNET_BIN" "$SERVER_DLL" >"$log" 2>&1 &
  echo $!
}

wait_health() {
  local base="$1"
  for _ in $(seq 1 120); do
    curl -fsS "$base/health/live" >/dev/null 2>&1 && return 0
    sleep .25
  done
  return 1
}

wait_ready_one() {
  local a="$1" b="$2"
  for _ in $(seq 1 120); do
    local ra=0 rb=0
    curl -fsS "$a/health/ready" >/dev/null 2>&1 && ra=1 || true
    curl -fsS "$b/health/ready" >/dev/null 2>&1 && rb=1 || true
    if [[ $((ra+rb)) -eq 1 ]]; then
      [[ $ra -eq 1 ]] && echo "$a" || echo "$b"
      return 0
    fi
    sleep .25
  done
  return 1
}

BASE_A="http://127.0.0.1:$PORT_A"
BASE_B="http://127.0.0.1:$PORT_B"
PID_A=$(start_node dispatch-a "$PORT_A" "$WORK/a.log")
PID_B=$(start_node dispatch-b "$PORT_B" "$WORK/b.log")
wait_health "$BASE_A" || { cat "$WORK/a.log" >&2; exit 1; }
wait_health "$BASE_B" || { cat "$WORK/b.log" >&2; exit 1; }
ACTIVE=$(wait_ready_one "$BASE_A" "$BASE_B") || { echo 'Expected exactly one ready writer.' >&2; exit 1; }
[[ "$ACTIVE" == "$BASE_A" ]] && STANDBY="$BASE_B" || STANDBY="$BASE_A"

echo "initial primary: $ACTIVE"
python3 - "$ACTIVE" "$STATEFILE" <<'PY'
import json, sys, urllib.request
base,statefile=sys.argv[1:]
def call(path, method='GET', body=None):
    data=None if body is None else json.dumps(body).encode()
    req=urllib.request.Request(base+path,data=data,method=method,headers={'Content-Type':'application/json','X-DISRoute-Request':'1'})
    with urllib.request.urlopen(req,timeout=10) as r:
        raw=r.read().decode(); return r.status, json.loads(raw) if raw else None
_,ag=call('/api/agencies'); _,st=call('/api/stations'); _,units=call('/api/units')
status,incident=call('/api/incidents','POST',{'agencyId':ag[0]['id'],'stationId':st[0]['id'],'nature':'HA Active Incident','priority':'Emergency','address':'1 Failover Way','crossStreets':'Lease Ave / Epoch St','municipality':'Test','callerName':'HA','callerPhone':'555-HA','notes':'active before planned switchover','idempotencyKey':'ha-smoke-before'})
assert status==201 and incident['status']=='Open', incident
unit=next(x for x in units if x.get('incidentId') is None)
_,incident=call(f"/api/incidents/{incident['id']}/assign",'POST',{'unitId':unit['id'],'expectedIncidentVersion':incident['version'],'allowReassignment':True,'idempotencyKey':'ha-smoke-assign'})
_,dispatch=call(f"/api/incidents/{incident['id']}/dispatch",'POST',{'expectedIncidentVersion':incident['version'],'unitIds':[unit['id']],'deliveryProviders':['StationDisplay','RunSheet'],'idempotencyKey':'ha-smoke-dispatch'})
incident=dispatch['incident']
_,activity=call(f"/api/incidents/{incident['id']}/narrative",'POST',{'text':'Narrative committed before application failover.','idempotencyKey':'ha-smoke-before-narrative'})
assert activity['incidentId']==incident['id'] and incident['status']=='Dispatched', (incident,activity)
json.dump({'incidentId':incident['id'],'unitId':unit['id'],'number':incident['number']},open(statefile,'w'),indent=2)
PY

curl -fsS -X POST "$ACTIVE/api/admin/ha/local/drain" -H 'Content-Type: application/json' -H 'X-DISRoute-Request: 1' -d '{"seconds":120,"reason":"HA smoke planned switchover during active incident"}' >/dev/null
for _ in $(seq 1 120); do
  curl -fsS "$STANDBY/health/ready" >/dev/null 2>&1 && break
  sleep .25
done
curl -fsS "$STANDBY/health/ready" >/dev/null || { echo 'Standby did not take over.' >&2; exit 1; }
if curl -fsS -X POST "$ACTIVE/api/incidents" -H 'Content-Type: application/json' -H 'X-DISRoute-Request: 1' -d '{}' >/dev/null 2>&1; then
  echo 'Drained node incorrectly accepted an operational write.' >&2; exit 1
fi

# Continue the exact same incident after the standby becomes authoritative. This is the Pass 28
# end-to-end continuity check: failover is not qualified merely by creating a second incident.
python3 - "$STANDBY" "$STATEFILE" <<'PY'
import json, sys, urllib.request
base,statefile=sys.argv[1:]
state=json.load(open(statefile))
def call(path, method='GET', body=None):
    data=None if body is None else json.dumps(body).encode()
    req=urllib.request.Request(base+path,data=data,method=method,headers={'Content-Type':'application/json','X-DISRoute-Request':'1'})
    with urllib.request.urlopen(req,timeout=10) as r:
        raw=r.read().decode(); return r.status, json.loads(raw) if raw else None
_,incident=call(f"/api/incidents/{state['incidentId']}")
assert incident['number']==state['number'] and incident['status']=='Dispatched', incident
_,activity=call(f"/api/incidents/{incident['id']}/narrative",'POST',{'text':'Same active incident continued on promoted application node.','idempotencyKey':'ha-smoke-after-narrative'})
assert activity['incidentId']==incident['id'], activity
_,incident=call(f"/api/incidents/{incident['id']}")
_,incident=call(f"/api/incidents/{incident['id']}/close",'POST',{'expectedVersion':incident['version'],'reason':'HA continuity scenario complete','idempotencyKey':'ha-smoke-close-after-failover'})
assert incident['status']=='Closed', incident
PY

curl -fsS -X POST "$ACTIVE/api/admin/ha/local/resume" -H 'Content-Type: application/json' -H 'X-DISRoute-Request: 1' -d '{"reason":"HA smoke resume"}' >/dev/null
sleep 2
curl -fsS "$STANDBY/health/ready" >/dev/null || { echo 'Current primary lost authority when old primary resumed.' >&2; exit 1; }

if [[ "$STANDBY" == "$BASE_A" ]]; then kill "$PID_A"; wait "$PID_A" 2>/dev/null || true; PID_A=""; survivor="$BASE_B"; else kill "$PID_B"; wait "$PID_B" 2>/dev/null || true; PID_B=""; survivor="$BASE_A"; fi
for _ in $(seq 1 120); do
  curl -fsS "$survivor/health/ready" >/dev/null 2>&1 && break
  sleep .25
done
curl -fsS "$survivor/health/ready" >/dev/null || { echo 'Resumed survivor did not reacquire authority.' >&2; exit 1; }

python3 - "$survivor" "$STATEFILE" <<'PY'
import json, sys, urllib.request
base,statefile=sys.argv[1:]
state=json.load(open(statefile))
def get(path):
    with urllib.request.urlopen(base+path,timeout=10) as r:
        return json.loads(r.read().decode())
incident=get(f"/api/incidents/{state['incidentId']}")
activity=get(f"/api/incidents/{state['incidentId']}/activity")
audit=get('/api/audit/verify')
assert incident['status']=='Closed' and incident['number']==state['number'], incident
messages=[x.get('message','') for x in activity]
assert any('before application failover' in x for x in messages), messages
assert any('continued on promoted application node' in x for x in messages), messages
assert audit['valid'] is True, audit
PY

echo "DISRoute HA active-incident smoke PASS — survivor primary: $survivor"
