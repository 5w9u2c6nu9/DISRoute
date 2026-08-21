#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_ROOT:-/mnt/data/disroute-toolchain/dotnet}/dotnet"
PG_CS="${DISROUTE_PGQUAL_CONNECTION_STRING:-}"
PSQL_DSN="${DISROUTE_PGQUAL_PSQL_DSN:-}"
ALLOW="${DISROUTE_PGQUAL_ALLOW_DESTRUCTIVE:-}"
READONLY_CS="${DISROUTE_PGQUAL_READONLY_CONNECTION_STRING:-}"
REPLICA_CS="${DISROUTE_PGQUAL_REPLICA_CONNECTION_STRING:-}"
WITNESS_CS="${DISROUTE_PGQUAL_WITNESS_CONNECTION_STRING:-}"
RESTORE_DSN="${DISROUTE_PGQUAL_RESTORE_PSQL_DSN:-}"
PORT_A="${DISROUTE_PGQUAL_PORT_A:-5211}"
PORT_B="${DISROUTE_PGQUAL_PORT_B:-5212}"
PORT_C="${DISROUTE_PGQUAL_PORT_C:-5213}"
WORK="${DISROUTE_PGQUAL_WORK:-/tmp/disroute-pgqual}"

[[ "$ALLOW" == "YES_I_UNDERSTAND" ]] || {
  echo 'Refusing destructive PostgreSQL qualification. Set DISROUTE_PGQUAL_ALLOW_DESTRUCTIVE=YES_I_UNDERSTAND and use a disposable database.' >&2
  exit 2
}
[[ -n "$PG_CS" ]] || { echo 'Set DISROUTE_PGQUAL_CONNECTION_STRING to the disposable database using Npgsql connection-string syntax.' >&2; exit 2; }
[[ -n "$PSQL_DSN" ]] || { echo 'Set DISROUTE_PGQUAL_PSQL_DSN to the same disposable database using libpq URI/DSN syntax.' >&2; exit 2; }
command -v psql >/dev/null || { echo 'psql is required for PostgreSQL qualification.' >&2; exit 2; }
command -v curl >/dev/null || { echo 'curl is required for PostgreSQL qualification.' >&2; exit 2; }
command -v python3 >/dev/null || { echo 'python3 is required for PostgreSQL qualification.' >&2; exit 2; }
[[ -x "$DOTNET_BIN" ]] || { echo "dotnet not found at $DOTNET_BIN" >&2; exit 2; }

resolve_server_dll() {
  local p
  for p in \
    "$ROOT/src/DISRoute.Server/bin/Release/net10.0/DISRoute.Server.dll" \
    "$ROOT/src/DISRoute.Server/bin/linux/amd64/Release/net10.0/DISRoute.Server.dll"; do
    [[ -f "$p" ]] && { echo "$p"; return 0; }
  done
  find "$ROOT/src/DISRoute.Server/bin" -type f -path '*/Release/net10.0/DISRoute.Server.dll' -print -quit 2>/dev/null
}
SERVER_DLL="$(resolve_server_dll)"
[[ -n "$SERVER_DLL" && -f "$SERVER_DLL" ]] || { echo 'Build DISRoute.Server Release before qualification.' >&2; exit 2; }

rm -rf "$WORK" && mkdir -p "$WORK"
PID_A=""; PID_B=""; PID_C=""
cleanup() {
  for pid in "$PID_A" "$PID_B" "$PID_C"; do
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      kill "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
    fi
  done
}
trap cleanup EXIT

psql_value() { psql "$PSQL_DSN" -X -v ON_ERROR_STOP=1 -Atqc "$1"; }
psql_exec() { psql "$PSQL_DSN" -X -v ON_ERROR_STOP=1 -qc "$1"; }

EXPECTED_MIGRATION_LEDGER="$WORK/expected-migration-ledger.tsv"
ACTUAL_MIGRATION_LEDGER="$WORK/actual-migration-ledger.tsv"

write_expected_migration_ledger() {
  python3 - "$ROOT/db/migrations" "$EXPECTED_MIGRATION_LEDGER" <<'PYLEDGER'
import hashlib,pathlib,re,sys
root=pathlib.Path(sys.argv[1]); out=pathlib.Path(sys.argv[2])
rows=[]
for p in sorted(root.glob('*.sql'), key=lambda x: x.name.lower()):
    m=re.match(r'^(\d+)_', p.name)
    if not m: raise SystemExit(f'invalid migration filename: {p.name}')
    # Match PostgreSqlDatabaseSafety: decode UTF-8 text, then hash its UTF-8 representation.
    text=p.read_text(encoding='utf-8')
    rows.append((int(m.group(1)),p.name,hashlib.sha256(text.encode('utf-8')).hexdigest()))
expected=list(range(1,len(rows)+1))
if [r[0] for r in rows] != expected:
    raise SystemExit(f'non-contiguous migration source sequence: {[r[0] for r in rows]}')
out.write_text(''.join(f'{v}\t{name}\t{sha}\n' for v,name,sha in rows),encoding='utf-8')
PYLEDGER
}

assert_migration_ledger_matches_source() {
  local dsn="${1:-$PSQL_DSN}" actual="${2:-$ACTUAL_MIGRATION_LEDGER}"
  psql "$dsn" -X -v ON_ERROR_STOP=1 -A -t -F $'\t' \
    -c "SELECT version, file_name, lower(sha256) FROM disroute_schema_migrations ORDER BY version" \
    >"$actual"
  if ! diff -u "$EXPECTED_MIGRATION_LEDGER" "$actual"; then
    echo 'PostgreSQL migration ledger does not exactly match the 20 published source migration identities.' >&2
    exit 1
  fi
}

start_node() {
  local slot="$1" node="$2" port="$3" cs="$4" site="$5" role="$6" auto_geo="$7" witness="$8" log="$9"
  local -a envs=(
    "ASPNETCORE_ENVIRONMENT=Development"
    "ASPNETCORE_URLS=http://127.0.0.1:$port"
    "Persistence__Provider=PostgreSQL"
    "ConnectionStrings__DISRoute=$cs"
    "Persistence__CheckpointSeconds=30"
    "HighAvailability__Enabled=true"
    "HighAvailability__NodeId=$node"
    "HighAvailability__Site=$site"
    "HighAvailability__ConfiguredRole=$role"
    "HighAvailability__HeartbeatSeconds=1"
    "HighAvailability__StaleNodeSeconds=4"
    "HighAvailability__AllowAutomaticGeographicPromotion=$auto_geo"
    "Security__RequireAuthentication=false"
    "Security__RequireHttps=false"
    "Security__RequireMfa=false"
    "Security__RequireMutationHeader=false"
    "Qualification__EnableCrashInjection=true"
  )
  [[ -n "$witness" ]] && envs+=("HighAvailability__WitnessConnectionString=$witness")
  env "${envs[@]}" "$DOTNET_BIN" "$SERVER_DLL" >"$log" 2>&1 &
  local pid=$!
  case "$slot" in A) PID_A="$pid";; B) PID_B="$pid";; C) PID_C="$pid";; esac
}

start_standalone() {
  local slot="$1" node="$2" port="$3" log="$4"
  env \
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="http://127.0.0.1:$port" \
    Persistence__Provider=PostgreSQL \
    ConnectionStrings__DISRoute="$PG_CS" \
    HighAvailability__Enabled=false \
    Security__RequireAuthentication=false \
    Security__RequireHttps=false \
    Security__RequireMfa=false \
    Security__RequireMutationHeader=false \
    "$DOTNET_BIN" "$SERVER_DLL" >"$log" 2>&1 &
  local pid=$!
  case "$slot" in A) PID_A="$pid";; B) PID_B="$pid";; C) PID_C="$pid";; esac
}

stop_slot() {
  local slot="$1" pid=""
  case "$slot" in A) pid="$PID_A";; B) pid="$PID_B";; C) pid="$PID_C";; esac
  if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
  case "$slot" in A) PID_A="";; B) PID_B="";; C) PID_C="";; esac
}

kill_slot_hard() {
  local slot="$1" pid=""
  case "$slot" in A) pid="$PID_A";; B) pid="$PID_B";; C) pid="$PID_C";; esac
  if [[ -n "$pid" ]]; then kill -KILL "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
  case "$slot" in A) PID_A="";; B) PID_B="";; C) PID_C="";; esac
}

wait_live() {
  local base="$1" slot="$2" log="$3"
  local pid=""; case "$slot" in A) pid="$PID_A";; B) pid="$PID_B";; C) pid="$PID_C";; esac
  for _ in $(seq 1 160); do
    curl -fsS "$base/health/live" >/dev/null 2>&1 && return 0
    if [[ -n "$pid" ]] && ! kill -0 "$pid" 2>/dev/null; then cat "$log" >&2; return 1; fi
    sleep .25
  done
  cat "$log" >&2
  return 1
}

is_ready() { curl -fsS "$1/health/ready" >/dev/null 2>&1; }

wait_exactly_one_ready() {
  local a="$1" b="$2"
  for _ in $(seq 1 200); do
    local ra=0 rb=0
    is_ready "$a" && ra=1 || true
    is_ready "$b" && rb=1 || true
    if [[ $((ra+rb)) -eq 1 ]]; then [[ $ra -eq 1 ]] && echo "$a" || echo "$b"; return 0; fi
    sleep .25
  done
  return 1
}

wait_ready() {
  local base="$1"
  for _ in $(seq 1 200); do is_ready "$base" && return 0; sleep .25; done
  return 1
}

slot_for_base() { [[ "$1" == "http://127.0.0.1:$PORT_A" ]] && echo A || echo B; }
other_base() { [[ "$1" == "http://127.0.0.1:$PORT_A" ]] && echo "http://127.0.0.1:$PORT_B" || echo "http://127.0.0.1:$PORT_A"; }
other_slot() { [[ "$1" == A ]] && echo B || echo A; }

assert_persistence_status() {
  python3 - "$1" "$2" <<'PY'
import json,sys,urllib.request
base, expected = sys.argv[1:]
with urllib.request.urlopen(base+'/api/persistence/status',timeout=10) as r: x=json.load(r)
assert x['supportedSchemaVersion']==20, x
assert x['migrationIntegrityVerified'] is True, x
assert x['databaseRole']==expected, x
PY
}

make_incident_payload() {
  python3 - "$1" "$2" "$3" <<'PY'
import json,sys,urllib.request
base,path,key=sys.argv[1:]
def get(p):
  with urllib.request.urlopen(base+p,timeout=10) as r:return json.load(r)
a=get('/api/agencies')[0]; s=get('/api/stations')[0]
json.dump({'agencyId':a['id'],'stationId':s['id'],'nature':'PGQUAL Crash Commit','priority':'Emergency','address':'25 Fencing Token Way','crossStreets':'','municipality':'Qualification','callerName':'PGQUAL','callerPhone':'555-0025','notes':'crash after durable commit before response','idempotencyKey':key},open(path,'w'))
PY
}

assert_incident_count_in_memory() {
  python3 - "$1" "$2" "$3" <<'PYINCIDENTCOUNT'
import json,sys,urllib.request
base,payload_file,expected=sys.argv[1:]
payload=json.load(open(payload_file))
with urllib.request.urlopen(base+'/api/incidents',timeout=15) as r: incidents=json.load(r)
matches=[x for x in incidents if x.get('address')==payload['address'] and x.get('nature')==payload['nature']]
assert len(matches)==int(expected),(expected,matches)
PYINCIDENTCOUNT
}

retry_and_assert_single_incident() {
  python3 - "$1" "$2" <<'PY'
import json,sys,urllib.request
base,payload_file=sys.argv[1:]
payload=json.load(open(payload_file))
data=json.dumps(payload).encode()
req=urllib.request.Request(base+'/api/incidents',data=data,method='POST',headers={'Content-Type':'application/json','X-DISRoute-Request':'1'})
with urllib.request.urlopen(req,timeout=15) as r: incident=json.load(r)
with urllib.request.urlopen(base+'/api/incidents',timeout=15) as r: incidents=json.load(r)
matches=[x for x in incidents if x.get('address')==payload['address'] and x.get('nature')==payload['nature']]
assert len(matches)==1,(incident,matches)
assert matches[0]['id']==incident['id'],(incident,matches)
print(incident['id'])
PY
}

assert_provider_reconciliation() {
  python3 - "$1" "$2" <<'PY'
import json,sys,urllib.request
base,native=sys.argv[1:]
with urllib.request.urlopen(base+'/api/integrations/operations',timeout=15) as r: ops=json.load(r)
rows=[x for x in ops if native in x.get('requestJson','')]
assert len(rows)==1,rows
op=rows[0]
assert op['state']=='NeedsReconciliation' and op['possibleDuplicate'] is True,op
with urllib.request.urlopen(base+f"/api/integrations/operations/{op['id']}/attempts",timeout=15) as r: attempts=json.load(r)
assert attempts and attempts[-1]['state']=='UnknownAfterRestore',attempts
print(op['id'])
PY
}

BASE_A="http://127.0.0.1:$PORT_A"; BASE_B="http://127.0.0.1:$PORT_B"; BASE_C="http://127.0.0.1:$PORT_C"
EVIDENCE="$WORK/evidence.json"
START_EPOCH="$(date +%s%3N)"
write_expected_migration_ledger

printf '%s\n' '=== Phase 1: empty database, concurrent migrations, exactly-one-writer ==='
psql_exec 'DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public AUTHORIZATION CURRENT_USER;'
start_node A dispatch-a "$PORT_A" "$PG_CS" Dispatch PrimaryCandidate false "" "$WORK/a.log"
start_node B dispatch-b "$PORT_B" "$PG_CS" Dispatch LocalStandby false "" "$WORK/b.log"
wait_live "$BASE_A" A "$WORK/a.log"; wait_live "$BASE_B" B "$WORK/b.log"
ACTIVE="$(wait_exactly_one_ready "$BASE_A" "$BASE_B")" || { echo 'Expected exactly one writable DISRoute node.' >&2; exit 1; }
STANDBY="$(other_base "$ACTIVE")"
ACTIVE_SLOT="$(slot_for_base "$ACTIVE")"; STANDBY_SLOT="$(other_slot "$ACTIVE_SLOT")"
assert_persistence_status "$ACTIVE" WritablePrimary
[[ "$(psql_value 'SELECT count(*) FROM disroute_schema_migrations')" == "20" ]] || { echo 'Migration ledger count is not 20.' >&2; exit 1; }
assert_migration_ledger_matches_source
[[ "$(psql_value 'SELECT version FROM schema_info WHERE singleton=true')" == "20" ]] || { echo 'schema_info did not reach version 20.' >&2; exit 1; }
[[ "$(psql_value "SELECT count(*) FROM server_nodes WHERE accepting_writes AND last_heartbeat_utc > now()-interval '6 seconds'")" == "1" ]] || { echo 'Database reports more than one fresh application writer.' >&2; exit 1; }

echo "initial primary: $ACTIVE"

printf '%s\n' '=== Phase 2: crash after durable commit before response; stale standby promotion ==='
INCIDENT_PAYLOAD="$WORK/crash-incident.json"
make_incident_payload "$ACTIVE" "$INCIDENT_PAYLOAD" 'pgqual:crash-after-commit'
# The long-running standby loaded before this command and must not already contain the incident.
# This creates an observable stale-state baseline for the post-promotion reload proof.
assert_incident_count_in_memory "$STANDBY" "$INCIDENT_PAYLOAD" 0
set +e
curl -sS --max-time 12 -X POST "$ACTIVE/api/incidents" \
  -H 'Content-Type: application/json' -H 'X-DISRoute-Request: 1' \
  -H 'X-DISRoute-Qualification-Crash: after-durable-commit' \
  --data-binary "@$INCIDENT_PAYLOAD" >"$WORK/crash-response.txt" 2>"$WORK/crash-curl.err"
CRASH_RC=$?
set -e
[[ $CRASH_RC -ne 0 ]] || { echo 'Crash injection unexpectedly returned a normal HTTP response.' >&2; exit 1; }
if [[ "$ACTIVE_SLOT" == A ]]; then wait "$PID_A" 2>/dev/null || true; PID_A=""; else wait "$PID_B" 2>/dev/null || true; PID_B=""; fi
FAILOVER_START="$(date +%s%3N)"
wait_ready "$STANDBY" || { echo 'Standby did not promote after crash.' >&2; exit 1; }
FAILOVER_END="$(date +%s%3N)"
# Before sending any retry, the newly promoted process must already expose the crash-boundary
# incident. Because its pre-crash in-memory baseline lacked it, this proves promotion reloaded the
# authoritative committed PostgreSQL checkpoint rather than promoting stale process memory.
assert_incident_count_in_memory "$STANDBY" "$INCIDENT_PAYLOAD" 1
INCIDENT_ID="$(retry_and_assert_single_incident "$STANDBY" "$INCIDENT_PAYLOAD")"
echo "committed incident survived ambiguous response: $INCIDENT_ID"

# Restart the crashed old primary. It must not steal authority from the survivor.
if [[ "$ACTIVE_SLOT" == A ]]; then start_node A dispatch-a "$PORT_A" "$PG_CS" Dispatch PrimaryCandidate false "" "$WORK/a-restart.log"; wait_live "$BASE_A" A "$WORK/a-restart.log"; else start_node B dispatch-b "$PORT_B" "$PG_CS" Dispatch LocalStandby false "" "$WORK/b-restart.log"; wait_live "$BASE_B" B "$WORK/b-restart.log"; fi
sleep 2
is_ready "$STANDBY" || { echo 'Returning old primary displaced the current authority unexpectedly.' >&2; exit 1; }
if is_ready "$ACTIVE"; then echo 'Both old and new nodes are ready after old-primary return.' >&2; exit 1; fi
ACTIVE="$STANDBY"; ACTIVE_SLOT="$STANDBY_SLOT"; STANDBY="$(other_base "$ACTIVE")"; STANDBY_SLOT="$(other_slot "$ACTIVE_SLOT")"

printf '%s\n' '=== Phase 3: crash at durable provider Submitting boundary ==='
AGENCY_ID="$(python3 - "$ACTIVE" <<'PY'
import json,sys,urllib.request
with urllib.request.urlopen(sys.argv[1]+'/api/agencies',timeout=10) as r: print(json.load(r)[0]['id'])
PY
)"
NATIVE_RADIO="PGQUAL-RADIO-$(date +%s)"
cat >"$WORK/provider-crash.json" <<JSON
{"agencyId":"$AGENCY_ID","incidentId":null,"unitId":null,"nativeRadioId":"$NATIVE_RADIO","providerKey":"simulation-radio","idempotencyKey":"pgqual:provider-submitting"}
JSON
set +e
curl -sS --max-time 12 -X POST "$ACTIVE/api/integrations/radio/clear-emergency" \
  -H 'Content-Type: application/json' -H 'X-DISRoute-Request: 1' \
  -H 'X-DISRoute-Qualification-Crash: integration-after-submitting' \
  --data-binary "@$WORK/provider-crash.json" >"$WORK/provider-crash-response.txt" 2>"$WORK/provider-crash-curl.err"
PROVIDER_CRASH_RC=$?
set -e
[[ $PROVIDER_CRASH_RC -ne 0 ]] || { echo 'Provider crash injection unexpectedly returned a normal HTTP response.' >&2; exit 1; }
if [[ "$ACTIVE_SLOT" == A ]]; then wait "$PID_A" 2>/dev/null || true; PID_A=""; else wait "$PID_B" 2>/dev/null || true; PID_B=""; fi
wait_ready "$STANDBY" || { echo 'Standby did not promote after provider-boundary crash.' >&2; exit 1; }
OP_ID="$(assert_provider_reconciliation "$STANDBY" "$NATIVE_RADIO")"
echo "ambiguous provider operation recovered as NeedsReconciliation: $OP_ID"
curl -fsS -X POST "$STANDBY/api/integrations/operations/$OP_ID/reconcile" \
  -H 'Content-Type: application/json' -H 'X-DISRoute-Request: 1' \
  -d '{"resolution":"Not-Sent","providerReference":null,"notes":"Qualification harness: provider call was intentionally never executed."}' >/dev/null

# Bring the crashed node back as standby for planned handoff.
if [[ "$ACTIVE_SLOT" == A ]]; then start_node A dispatch-a "$PORT_A" "$PG_CS" Dispatch PrimaryCandidate false "" "$WORK/a-restart2.log"; wait_live "$BASE_A" A "$WORK/a-restart2.log"; else start_node B dispatch-b "$PORT_B" "$PG_CS" Dispatch LocalStandby false "" "$WORK/b-restart2.log"; wait_live "$BASE_B" B "$WORK/b-restart2.log"; fi
ACTIVE="$STANDBY"; ACTIVE_SLOT="$STANDBY_SLOT"; STANDBY="$(other_base "$ACTIVE")"; STANDBY_SLOT="$(other_slot "$ACTIVE_SLOT")"

printf '%s\n' '=== Phase 4: planned drain/resume and old-primary return ==='
curl -fsS -X POST "$ACTIVE/api/admin/ha/local/drain" -H 'Content-Type: application/json' -H 'X-DISRoute-Request: 1' -d '{"seconds":120,"reason":"Pass 25 qualification planned switchover"}' >/dev/null
wait_ready "$STANDBY" || { echo 'Standby did not take over after planned drain.' >&2; exit 1; }
if is_ready "$ACTIVE"; then echo 'Drained node remained ready/writable.' >&2; exit 1; fi
curl -fsS -X POST "$ACTIVE/api/admin/ha/local/resume" -H 'Content-Type: application/json' -H 'X-DISRoute-Request: 1' -d '{"reason":"Pass 25 qualification resume"}' >/dev/null
sleep 2
is_ready "$STANDBY" || { echo 'Current primary lost authority when drained node resumed.' >&2; exit 1; }
[[ "$(psql_value "SELECT count(*) FROM server_nodes WHERE accepting_writes AND last_heartbeat_utc > now()-interval '6 seconds'")" == "1" ]] || { echo 'Exactly-one-writer database assertion failed after planned handoff.' >&2; exit 1; }

printf '%s\n' '=== Phase 5: pre-ledger upgrade/backfill and migration tamper fail-fast ==='
stop_slot A; stop_slot B
psql_exec 'DROP TABLE disroute_schema_migrations;'
start_standalone A pgqual-backfill "$PORT_A" "$WORK/backfill.log"
wait_live "$BASE_A" A "$WORK/backfill.log"; wait_ready "$BASE_A"
[[ "$(psql_value 'SELECT count(*) FROM disroute_schema_migrations')" == "20" ]] || { echo 'Pre-ledger migration backfill did not recreate all 20 hashes.' >&2; exit 1; }
assert_migration_ledger_matches_source
stop_slot A
CORRECT_HASH="$(awk -F '\t' '$1 == 1 {print $3}' "$EXPECTED_MIGRATION_LEDGER")"
psql_exec "UPDATE disroute_schema_migrations SET sha256 = repeat('0',64) WHERE version=1;"
start_standalone A pgqual-tamper "$PORT_A" "$WORK/tamper.log"
for _ in $(seq 1 80); do
  if [[ -n "$PID_A" ]] && ! kill -0 "$PID_A" 2>/dev/null; then break; fi
  sleep .25
done
if [[ -n "$PID_A" ]] && kill -0 "$PID_A" 2>/dev/null; then echo 'Server started despite tampered migration hash.' >&2; exit 1; fi
wait "$PID_A" 2>/dev/null || true; PID_A=""
grep -qi 'integrity mismatch' "$WORK/tamper.log" || { echo 'Tampered migration failed, but not with the expected integrity error.' >&2; cat "$WORK/tamper.log" >&2; exit 1; }
psql_exec "UPDATE disroute_schema_migrations SET sha256='$CORRECT_HASH' WHERE version=1;"
start_standalone A pgqual-post-tamper "$PORT_A" "$WORK/post-tamper.log"
wait_live "$BASE_A" A "$WORK/post-tamper.log"; wait_ready "$BASE_A"; assert_persistence_status "$BASE_A" WritablePrimary
assert_migration_ledger_matches_source
stop_slot A

printf '%s\n' '=== Optional Phase 6: read-only/HQ replica startup ==='
REPLICA_TESTED=false
RO_CS="$READONLY_CS"
[[ -z "$RO_CS" ]] && RO_CS="$REPLICA_CS"
if [[ -n "$RO_CS" ]]; then
  start_node C hq-dr "$PORT_C" "$RO_CS" HQ GeographicDR false "$WITNESS_CS" "$WORK/hq-replica.log"
  wait_live "$BASE_C" C "$WORK/hq-replica.log"
  if is_ready "$BASE_C"; then echo 'Read-only/HQ replica unexpectedly became writable.' >&2; exit 1; fi
  assert_persistence_status "$BASE_C" ReadOnlyReplica
  REPLICA_TESTED=true
  stop_slot C
else
  echo 'SKIP: set DISROUTE_PGQUAL_READONLY_CONNECTION_STRING or DISROUTE_PGQUAL_REPLICA_CONNECTION_STRING to qualify read-only replica startup.'
fi

printf '%s\n' '=== Optional Phase 7: logical backup/restore target ==='
RESTORE_TESTED=false
if [[ -n "$RESTORE_DSN" ]] && command -v pg_dump >/dev/null && command -v pg_restore >/dev/null; then
  pg_dump "$PSQL_DSN" --format=custom --file="$WORK/disroute-pgqual.dump"
  pg_restore --clean --if-exists --no-owner --dbname="$RESTORE_DSN" "$WORK/disroute-pgqual.dump"
  [[ "$(psql "$RESTORE_DSN" -X -v ON_ERROR_STOP=1 -Atqc 'SELECT count(*) FROM disroute_schema_migrations')" == "20" ]] || { echo 'Restored database migration ledger is incomplete.' >&2; exit 1; }
  assert_migration_ledger_matches_source "$RESTORE_DSN" "$WORK/restored-migration-ledger.tsv"
  [[ "$(psql "$RESTORE_DSN" -X -v ON_ERROR_STOP=1 -Atqc "SELECT count(*) FROM incidents WHERE id='$INCIDENT_ID'")" == "1" ]] || { echo 'Restored database is missing the committed crash-boundary incident.' >&2; exit 1; }
  RESTORE_TESTED=true
else
  echo 'SKIP: set DISROUTE_PGQUAL_RESTORE_PSQL_DSN and provide pg_dump/pg_restore to run logical recovery qualification.'
fi

FAILOVER_MS=$((FAILOVER_END-FAILOVER_START))
END_EPOCH="$(date +%s%3N)"
python3 - "$EVIDENCE" "$FAILOVER_MS" "$START_EPOCH" "$END_EPOCH" "$REPLICA_TESTED" "$RESTORE_TESTED" "$INCIDENT_ID" "$OP_ID" "$EXPECTED_MIGRATION_LEDGER" <<'PY'
import json,sys,datetime
path,failover,start,end,replica,restore,incident,operation,ledger_path=sys.argv[1:]
hashes=[]
for line in open(ledger_path,encoding="utf-8"):
  version,file_name,sha256=line.rstrip("\n").split("\t")
  hashes.append({"version":int(version),"fileName":file_name,"sha256":sha256})
obj={
  'result':'PASS',
  'qualifiedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
  'migrationLedgerCount':20,
  'migrationHashesVerifiedAgainstPublishedSource':True,
  'migrationHashes':hashes,
  'schemaVersion':20,
  'concurrentMigrationStartup':True,
  'exactlyOneWriter':True,
  'crashAfterCommitBeforeResponse':True,
  'staleStandbyReload':True,
  'ambiguousResponseIncidentId':incident,
  'providerSubmittingCrashNeedsReconciliation':True,
  'providerOperationId':operation,
  'plannedDrainResume':True,
  'oldPrimaryReturnFenced':True,
  'migrationLedgerBackfill':True,
  'migrationTamperRejected':True,
  'readOnlyReplicaStartupQualified':replica.lower()=='true',
  'logicalBackupRestoreQualified':restore.lower()=='true',
  'measuredCrashFailoverMilliseconds':int(failover),
  'elapsedMilliseconds':int(end)-int(start)
}
json.dump(obj,open(path,'w'),indent=2)
print(json.dumps(obj,indent=2))
PY

echo "DISRoute PostgreSQL/HA qualification PASS — evidence: $EVIDENCE"
