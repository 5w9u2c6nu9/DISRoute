#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_ROOT:-/mnt/data/disroute-toolchain/dotnet}/dotnet"
PGROOT="${DISROUTE_PGPHYS_PGROOT:-/mnt/data/disroute-pg18/percona-postgresql18}"
PG_BIN="$PGROOT/bin"
export LD_LIBRARY_PATH="$PGROOT/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
ALLOW="${DISROUTE_PGPHYS_ALLOW_DESTRUCTIVE:-}"
WORK="${DISROUTE_PGPHYS_WORK:-/tmp/disroute-p26-physical}"
PG_RUN_USER="${DISROUTE_PGPHYS_RUN_USER:-nobody}"
PRIMARY_PORT="${DISROUTE_PGPHYS_PRIMARY_PORT:-55431}"
STANDBY_PORT="${DISROUTE_PGPHYS_STANDBY_PORT:-55432}"
WITNESS_PORT="${DISROUTE_PGPHYS_WITNESS_PORT:-55433}"
PITR_PORT="${DISROUTE_PGPHYS_PITR_PORT:-55434}"
DISPATCH_DB_PROXY="${DISROUTE_PGPHYS_DISPATCH_DB_PROXY:-56431}"
HQ_DB_PROXY="${DISROUTE_PGPHYS_HQ_DB_PROXY:-56432}"
DISPATCH_WITNESS_PROXY="${DISROUTE_PGPHYS_DISPATCH_WITNESS_PROXY:-56531}"
HQ_WITNESS_PROXY="${DISROUTE_PGPHYS_HQ_WITNESS_PROXY:-56532}"
APP_DISPATCH_PORT="${DISROUTE_PGPHYS_APP_DISPATCH_PORT:-5311}"
APP_HQ_PORT="${DISROUTE_PGPHYS_APP_HQ_PORT:-5312}"

[[ "$ALLOW" == "YES_I_UNDERSTAND" ]] || { echo 'Refusing destructive physical-HA qualification. Set DISROUTE_PGPHYS_ALLOW_DESTRUCTIVE=YES_I_UNDERSTAND.' >&2; exit 2; }
for x in initdb pg_ctl psql createdb pg_basebackup pg_rewind; do [[ -x "$PG_BIN/$x" ]] || { echo "missing $PG_BIN/$x" >&2; exit 2; }; done
[[ -x "$DOTNET_BIN" ]] || { echo "dotnet missing at $DOTNET_BIN" >&2; exit 2; }
command -v runuser >/dev/null; command -v curl >/dev/null; command -v python3 >/dev/null
SERVER_DLL="$(find "$ROOT/src/DISRoute.Server/bin" -type f -path '*/Release/net10.0/DISRoute.Server.dll' -print -quit 2>/dev/null || true)"
[[ -n "$SERVER_DLL" ]] || { echo 'Build DISRoute.Server Release before physical HA qualification.' >&2; exit 2; }

P_DATA="$WORK/primary"; S_DATA="$WORK/standby"; W_DATA="$WORK/witness"; BASE_DATA="$WORK/pitr-base"; PITR_DATA="$WORK/pitr"; ARCHIVE="$WORK/archive"
APP_DISPATCH_PID=""; APP_HQ_PID=""; PXY_DDB=""; PXY_HDB=""; PXY_DW=""; PXY_HW=""
START_MS="$(date +%s%3N)"

as_pg(){ runuser -u "$PG_RUN_USER" -- env LD_LIBRARY_PATH="$LD_LIBRARY_PATH" "$@"; }
prepare_owned(){ mkdir -p "$1"; chmod 700 "$1"; if [[ "$(id -u)" == 0 ]]; then chown -R "$PG_RUN_USER" "$1"; fi; }
pg_start(){ local data="$1" port="$2" log="$3"; touch "$log"; chmod 666 "$log"; as_pg "$PG_BIN/pg_ctl" -D "$data" -l "$log" -w start -o "-h 127.0.0.1 -p $port" >/dev/null; }
pg_stop(){ local data="$1"; as_pg "$PG_BIN/pg_ctl" -D "$data" -m fast -w stop >/dev/null 2>&1 || true; }
pg_value(){ local port="$1" db="$2" q="$3"; "$PG_BIN/psql" -h 127.0.0.1 -p "$port" -U "$PG_RUN_USER" -d "$db" -X -v ON_ERROR_STOP=1 -Atqc "$q"; }
pg_exec(){ local port="$1" db="$2" q="$3"; "$PG_BIN/psql" -h 127.0.0.1 -p "$port" -U "$PG_RUN_USER" -d "$db" -X -v ON_ERROR_STOP=1 -qc "$q"; }
wait_pg(){ local port="$1"; for _ in $(seq 1 160); do "$PG_BIN/pg_isready" -h 127.0.0.1 -p "$port" -U "$PG_RUN_USER" >/dev/null 2>&1 && return 0; sleep .125; done; return 1; }
wait_recovery(){ local port="$1" expected="$2"; for _ in $(seq 1 240); do [[ "$(pg_value "$port" postgres 'select pg_is_in_recovery()' 2>/dev/null || true)" == "$expected" ]] && return 0; sleep .125; done; return 1; }

stop_proxy_pid(){ local pid="${1:-}"; if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then kill -KILL "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi; }
start_proxy(){ local listen="$1" target="$2" log="$3"; python3 "$ROOT/scripts/tcp-fault-proxy.py" --listen-port "$listen" --target-port "$target" >"$log" 2>&1 & local pid=$! ok=false; for _ in $(seq 1 80); do
    if ! kill -0 "$pid" 2>/dev/null; then cat "$log" >&2; return 1; fi
    if python3 - "$listen" <<'PYPROXY'
import socket,sys
s=socket.socket(); s.settimeout(.25)
try:s.connect(('127.0.0.1',int(sys.argv[1])))
except OSError:raise SystemExit(1)
finally:s.close()
PYPROXY
    then ok=true; break; fi; sleep .05; done
  [[ "$ok" == true ]] || { echo "TCP proxy failed on $listen" >&2; cat "$log" >&2; return 1; }; echo "$pid"; }

start_dispatch_db_proxy(){ stop_proxy_pid "$PXY_DDB"; PXY_DDB="$(start_proxy "$DISPATCH_DB_PROXY" "$PRIMARY_PORT" "$WORK/proxy-dispatch-db.log")"; }
start_hq_db_proxy(){ stop_proxy_pid "$PXY_HDB"; PXY_HDB="$(start_proxy "$HQ_DB_PROXY" "$STANDBY_PORT" "$WORK/proxy-hq-db.log")"; }
start_dispatch_witness_proxy(){ stop_proxy_pid "$PXY_DW"; PXY_DW="$(start_proxy "$DISPATCH_WITNESS_PROXY" "$WITNESS_PORT" "$WORK/proxy-dispatch-witness.log")"; }
start_hq_witness_proxy(){ stop_proxy_pid "$PXY_HW"; PXY_HW="$(start_proxy "$HQ_WITNESS_PROXY" "$WITNESS_PORT" "$WORK/proxy-hq-witness.log")"; }

kill_app(){ local pid="${1:-}"; if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then kill -KILL "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi; }
start_app(){ local which="$1" port="$2" dbproxy="$3" witnessproxy="$4" site="$5" role="$6" autogeo="$7" log="$8"
  local cs="Host=127.0.0.1;Port=$dbproxy;Database=disroute;Username=$PG_RUN_USER;Pooling=false;Timeout=2;Command Timeout=5"
  local wcs="Host=127.0.0.1;Port=$witnessproxy;Database=postgres;Username=$PG_RUN_USER;Pooling=false;Timeout=2;Command Timeout=5"
  env ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://127.0.0.1:$port" \
    Persistence__Provider=PostgreSQL ConnectionStrings__DISRoute="$cs" Persistence__CheckpointSeconds=2 \
    HighAvailability__Enabled=true HighAvailability__NodeId="$which" HighAvailability__Site="$site" HighAvailability__ConfiguredRole="$role" \
    HighAvailability__HeartbeatSeconds=1 HighAvailability__StaleNodeSeconds=4 HighAvailability__AllowAutomaticGeographicPromotion="$autogeo" \
    HighAvailability__DatabaseTopology=PhysicalFailover HighAvailability__DatabaseFencingMode=External HighAvailability__WitnessConnectionString="$wcs" \
    Security__RequireAuthentication=false Security__RequireHttps=false Security__RequireMfa=false Security__RequireMutationHeader=false \
    Qualification__EnableCrashInjection=true "$DOTNET_BIN" "$SERVER_DLL" >"$log" 2>&1 & echo $!; }
BASE_D="http://127.0.0.1:$APP_DISPATCH_PORT"; BASE_H="http://127.0.0.1:$APP_HQ_PORT"
wait_live(){ local base="$1" pid="$2" log="$3"; for _ in $(seq 1 240); do curl -fsS "$base/health/live" >/dev/null 2>&1 && return 0; kill -0 "$pid" 2>/dev/null || { cat "$log" >&2; return 1; }; sleep .125; done; cat "$log" >&2; return 1; }
is_ready(){ curl -fsS --max-time 1 "$1/health/ready" >/dev/null 2>&1; }
wait_ready(){ local base="$1"; for _ in $(seq 1 240); do is_ready "$base" && return 0; sleep .125; done; return 1; }
wait_zero_ready(){ for _ in $(seq 1 160); do if ! is_ready "$BASE_D" && ! is_ready "$BASE_H"; then return 0; fi; sleep .125; done; return 1; }
assert_exactly(){ local expected="$1"; local d=0 h=0; is_ready "$BASE_D" && d=1 || true; is_ready "$BASE_H" && h=1 || true; [[ $((d+h)) -eq 1 ]] || { echo "expected exactly one writer, dispatch=$d hq=$h" >&2; return 1; }; [[ "$expected" == dispatch && $d -eq 1 || "$expected" == hq && $h -eq 1 ]]; }

create_incident(){ local base="$1" label="$2" address="$3" key="$4" out="$5"; python3 - "$base" "$label" "$address" "$key" "$out" <<'PYINC'
import json,sys,urllib.request
base,label,address,key,out=sys.argv[1:]
def get(p):
  with urllib.request.urlopen(base+p,timeout=10) as r:return json.load(r)
a=get('/api/agencies')[0]; st=get('/api/stations')[0]
p={'agencyId':a['id'],'stationId':st['id'],'nature':label,'priority':'Emergency','address':address,'crossStreets':'','municipality':'Qualification','callerName':'P26','callerPhone':'555-0026','notes':'Pass 26 physical HA qualification','idempotencyKey':key}
r=urllib.request.Request(base+'/api/incidents',data=json.dumps(p).encode(),method='POST',headers={'Content-Type':'application/json','X-DISRoute-Request':'1'})
with urllib.request.urlopen(r,timeout=15) as x: obj=json.load(x)
open(out,'w').write(json.dumps(obj)); print(obj['id'])
PYINC
}
assert_ha_contract(){ local base="$1"; python3 - "$base" <<'PYHA'
import json,sys,urllib.request
with urllib.request.urlopen(sys.argv[1]+'/api/admin/ha',timeout=10) as r:x=json.load(r)
assert x['databaseTopology']=='PhysicalFailover',x
assert x['databaseFencingMode']=='External',x
assert x['externalWitnessConfigured'] is True,x
PYHA
}

restore_archived_timeline_wal(){ local data="$1" timeline="$2" wal; shopt -s nullglob; for wal in "$ARCHIVE/${timeline}"????????????????; do cp -f "$wal" "$data/pg_wal/"; done; shopt -u nullglob; [[ "$(id -u)" == 0 ]] && chown -R "$PG_RUN_USER" "$data/pg_wal" || true; }
cleanup(){ kill_app "$APP_DISPATCH_PID"; kill_app "$APP_HQ_PID"; for p in "$PXY_DDB" "$PXY_HDB" "$PXY_DW" "$PXY_HW"; do stop_proxy_pid "$p"; done; pg_stop "$P_DATA"; pg_stop "$S_DATA"; pg_stop "$W_DATA"; pg_stop "$PITR_DATA"; }
trap cleanup EXIT

rm -rf "$WORK"; mkdir -p "$WORK" "$ARCHIVE"; chmod 777 "$ARCHIVE"
printf '%s\n' '=== Phase 0: initialize physical HA lab ==='
prepare_owned "$P_DATA"; prepare_owned "$W_DATA"
as_pg "$PG_BIN/initdb" -D "$P_DATA" --no-locale --encoding=UTF8 --auth=trust --data-checksums >/dev/null
as_pg "$PG_BIN/initdb" -D "$W_DATA" --no-locale --encoding=UTF8 --auth=trust --data-checksums >/dev/null
cat >>"$P_DATA/postgresql.conf" <<EOFPG
listen_addresses='127.0.0.1'
port=$PRIMARY_PORT
wal_level=replica
max_wal_senders=10
max_replication_slots=10
wal_keep_size='1GB'
max_wal_size='2GB'
archive_mode=on
archive_command='test -f $ARCHIVE/%f || cp %p $ARCHIVE/%f'
EOFPG
cat >>"$W_DATA/postgresql.conf" <<EOFW
listen_addresses='127.0.0.1'
port=$WITNESS_PORT
EOFW
pg_start "$P_DATA" "$PRIMARY_PORT" "$WORK/primary.log"; pg_start "$W_DATA" "$WITNESS_PORT" "$WORK/witness.log"
as_pg "$PG_BIN/createdb" -h 127.0.0.1 -p "$PRIMARY_PORT" -U "$PG_RUN_USER" disroute
start_dispatch_db_proxy; start_dispatch_witness_proxy; start_hq_witness_proxy
APP_DISPATCH_PID="$(start_app dispatch-primary "$APP_DISPATCH_PORT" "$DISPATCH_DB_PROXY" "$DISPATCH_WITNESS_PROXY" Dispatch PrimaryCandidate false "$WORK/app-dispatch.log")"
wait_live "$BASE_D" "$APP_DISPATCH_PID" "$WORK/app-dispatch.log"; wait_ready "$BASE_D"
assert_ha_contract "$BASE_D"
[[ "$(pg_value "$PRIMARY_PORT" disroute 'select count(*) from disroute_schema_migrations')" == 20 ]]

printf '%s\n' '=== Phase 1: create real physical standby; keep one application writer ==='
prepare_owned "$S_DATA"
as_pg "$PG_BIN/pg_basebackup" -h 127.0.0.1 -p "$PRIMARY_PORT" -U "$PG_RUN_USER" -D "$S_DATA" -Fp -Xs --checkpoint=fast -R >/dev/null
chmod 700 "$S_DATA"; [[ "$(id -u)" == 0 ]] && chown -R "$PG_RUN_USER" "$S_DATA"
cat >>"$S_DATA/postgresql.auto.conf" <<EOFS
port=$STANDBY_PORT
hot_standby=on
EOFS
pg_start "$S_DATA" "$STANDBY_PORT" "$WORK/standby.log"; wait_recovery "$STANDBY_PORT" t
start_hq_db_proxy
APP_HQ_PID="$(start_app hq-dr "$APP_HQ_PORT" "$HQ_DB_PROXY" "$HQ_WITNESS_PROXY" HQ GeographicDR true "$WORK/app-hq.log")"
wait_live "$BASE_H" "$APP_HQ_PID" "$WORK/app-hq.log"; sleep 2; assert_exactly dispatch; assert_ha_contract "$BASE_H"
[[ "$(pg_value "$STANDBY_PORT" disroute 'select count(*) from disroute_schema_migrations')" == 20 ]]

printf '%s\n' '=== Phase 2: independent-witness partition fails closed ==='
stop_proxy_pid "$PXY_DW"; PXY_DW=""; wait_zero_ready || { echo 'witness partition did not produce zero writers' >&2; exit 1; }
start_dispatch_witness_proxy; wait_ready "$BASE_D"; assert_exactly dispatch

printf '%s\n' '=== Phase 3: local database partition fails closed ==='
stop_proxy_pid "$PXY_DDB"; PXY_DDB=""; wait_zero_ready || { echo 'database partition did not produce zero writers' >&2; exit 1; }
start_dispatch_db_proxy; wait_ready "$BASE_D"; assert_exactly dispatch

printf '%s\n' '=== Phase 4: external DB fence + physical promotion + Dispatch -> HQ authority ==='
# Synchronize and force an archive boundary before fencing the old primary.
pg_exec "$PRIMARY_PORT" postgres 'select pg_switch_wal()'; sleep .5
FAILOVER_START="$(date +%s%3N)"
pg_stop "$P_DATA"
wait_zero_ready || true
as_pg "$PG_BIN/pg_ctl" -D "$S_DATA" -w promote >/dev/null
wait_recovery "$STANDBY_PORT" f
wait_ready "$BASE_H" || { tail -100 "$WORK/app-hq.log" >&2; exit 1; }
assert_exactly hq; FAILOVER_MS=$(( $(date +%s%3N) - FAILOVER_START ))
HQ_INC="$(create_incident "$BASE_H" 'P26 HQ promoted' '26 Promoted Timeline Way' 'p26:hq-promoted' "$WORK/hq-promoted.json")"

printf '%s\n' '=== Phase 5: unsafe old DB primary return stays application-fenced by witness ==='
pg_start "$P_DATA" "$PRIMARY_PORT" "$WORK/primary-return.log"; wait_recovery "$PRIMARY_PORT" f
sleep 3; assert_exactly hq
# Old timeline must not receive the command committed through HQ.
[[ "$(pg_value "$PRIMARY_PORT" disroute "select count(*) from incidents where id='$HQ_INC'")" == 0 ]]
pg_stop "$P_DATA"

printf '%s\n' '=== Phase 6: rewind former Dispatch primary and return it as standby ==='
restore_archived_timeline_wal "$P_DATA" 00000001
as_pg "$PG_BIN/pg_rewind" --target-pgdata="$P_DATA" --source-server="host=127.0.0.1 port=$STANDBY_PORT user=$PG_RUN_USER dbname=postgres" -R >/dev/null
sed -i '/^port=/d' "$P_DATA/postgresql.auto.conf" || true
echo "port=$PRIMARY_PORT" >>"$P_DATA/postgresql.auto.conf"
[[ "$(id -u)" == 0 ]] && chown -R "$PG_RUN_USER" "$P_DATA"
pg_start "$P_DATA" "$PRIMARY_PORT" "$WORK/primary-rewound.log"; wait_recovery "$PRIMARY_PORT" t
for _ in $(seq 1 160); do [[ "$(pg_value "$PRIMARY_PORT" disroute "select count(*) from incidents where id='$HQ_INC'" 2>/dev/null || echo 0)" == 1 ]] && break; sleep .125; done
[[ "$(pg_value "$PRIMARY_PORT" disroute "select count(*) from incidents where id='$HQ_INC'")" == 1 ]]
assert_exactly hq

printf '%s\n' '=== Phase 7: planned physical failback to Dispatch ==='
# Make the rewound Dispatch standby current, then externally fence HQ before promotion.
for _ in $(seq 1 160); do lag="$(pg_value "$PRIMARY_PORT" postgres "select coalesce(pg_wal_lsn_diff(pg_last_wal_receive_lsn(),pg_last_wal_replay_lsn()),0)::bigint" 2>/dev/null || echo 1)"; [[ "$lag" == 0 ]] && break; sleep .125; done
FAILBACK_START="$(date +%s%3N)"
pg_stop "$S_DATA"
as_pg "$PG_BIN/pg_ctl" -D "$P_DATA" -w promote >/dev/null
wait_recovery "$PRIMARY_PORT" f
wait_ready "$BASE_D" || { tail -100 "$WORK/app-dispatch.log" >&2; exit 1; }
assert_exactly dispatch; FAILBACK_MS=$(( $(date +%s%3N) - FAILBACK_START ))
DISPATCH_INC="$(create_incident "$BASE_D" 'P26 Dispatch failback' '26 Failback Avenue' 'p26:dispatch-failback' "$WORK/dispatch-failback.json")"

printf '%s\n' '=== Phase 8: rewind former HQ primary and restore streaming protection ==='
restore_archived_timeline_wal "$S_DATA" 00000002
as_pg "$PG_BIN/pg_rewind" --target-pgdata="$S_DATA" --source-server="host=127.0.0.1 port=$PRIMARY_PORT user=$PG_RUN_USER dbname=postgres" -R >/dev/null
sed -i '/^port=/d' "$S_DATA/postgresql.auto.conf" || true
echo "port=$STANDBY_PORT" >>"$S_DATA/postgresql.auto.conf"
[[ "$(id -u)" == 0 ]] && chown -R "$PG_RUN_USER" "$S_DATA"
pg_start "$S_DATA" "$STANDBY_PORT" "$WORK/hq-rewound.log"; wait_recovery "$STANDBY_PORT" t
for _ in $(seq 1 160); do [[ "$(pg_value "$STANDBY_PORT" disroute "select count(*) from incidents where id='$DISPATCH_INC'" 2>/dev/null || echo 0)" == 1 ]] && break; sleep .125; done
[[ "$(pg_value "$STANDBY_PORT" disroute "select count(*) from incidents where id='$DISPATCH_INC'")" == 1 ]]
assert_exactly dispatch

printf '%s\n' '=== Phase 9: physical base backup + WAL archive PITR boundary ==='
prepare_owned "$BASE_DATA"
as_pg "$PG_BIN/pg_basebackup" -h 127.0.0.1 -p "$PRIMARY_PORT" -U "$PG_RUN_USER" -D "$BASE_DATA" -Fp -Xs --checkpoint=fast >/dev/null
INC_ADDR="26 PITR Included Street"; EXC_ADDR="26 PITR Excluded Street"
create_incident "$BASE_D" 'P26 PITR included' "$INC_ADDR" 'p26:pitr-included' "$WORK/pitr-included.json" >/dev/null
PITR_TARGET="$(pg_value "$PRIMARY_PORT" postgres 'select clock_timestamp()')"
sleep 1
create_incident "$BASE_D" 'P26 PITR excluded' "$EXC_ADDR" 'p26:pitr-excluded' "$WORK/pitr-excluded.json" >/dev/null
PITR_REQUIRED_WAL="$(pg_value "$PRIMARY_PORT" postgres 'select pg_walfile_name(pg_current_wal_lsn())')"
pg_exec "$PRIMARY_PORT" postgres 'select pg_switch_wal()'
for _ in $(seq 1 320); do [[ -f "$ARCHIVE/$PITR_REQUIRED_WAL" ]] && break; sleep .125; done
[[ -f "$ARCHIVE/$PITR_REQUIRED_WAL" ]] || { echo "required PITR WAL not archived: $PITR_REQUIRED_WAL" >&2; exit 1; }
cp -a "$BASE_DATA" "$PITR_DATA"; chmod 700 "$PITR_DATA"; [[ "$(id -u)" == 0 ]] && chown -R "$PG_RUN_USER" "$PITR_DATA"
rm -f "$PITR_DATA/standby.signal"
touch "$PITR_DATA/recovery.signal"; [[ "$(id -u)" == 0 ]] && chown "$PG_RUN_USER" "$PITR_DATA/recovery.signal"
cat >>"$PITR_DATA/postgresql.conf" <<EOFP
restore_command='cp $ARCHIVE/%f %p'
recovery_target_time='$PITR_TARGET'
recovery_target_action='promote'
archive_mode=off
EOFP
# Base backups inherit node-local auto.conf; neutralize it for the isolated PITR instance.
sed -i '/^port=/d;/^primary_conninfo/d' "$PITR_DATA/postgresql.auto.conf" || true
echo "port=$PITR_PORT" >>"$PITR_DATA/postgresql.auto.conf"
[[ "$(id -u)" == 0 ]] && chown -R "$PG_RUN_USER" "$PITR_DATA"
pg_start "$PITR_DATA" "$PITR_PORT" "$WORK/pitr.log"; wait_recovery "$PITR_PORT" f
[[ "$(pg_value "$PITR_PORT" disroute "select count(*) from incidents where address='$INC_ADDR'")" == 1 ]]
[[ "$(pg_value "$PITR_PORT" disroute "select count(*) from incidents where address='$EXC_ADDR'")" == 0 ]]
[[ "$(pg_value "$PITR_PORT" disroute 'select count(*) from disroute_schema_migrations')" == 20 ]]

END_MS="$(date +%s%3N)"
python3 - "$WORK/evidence.json" "$PITR_TARGET" "$FAILOVER_MS" "$FAILBACK_MS" "$START_MS" "$END_MS" <<'PYEVID'
import json,sys,datetime
out,target,fo,fb,start,end=sys.argv[1:]
x={
 'result':'PASS','qualifiedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
 'databaseTopology':'PhysicalFailover','externalWitnessRequiredAndUsed':True,
 'externalDatabaseFencingRequiredByConfiguration':True,
 'witnessNetworkPartitionFailsClosed':True,'databaseNetworkPartitionFailsClosed':True,
 'physicalStreamingReplicaQualified':True,'physicalReplicaPromotionQualified':True,
 'dispatchToHqApplicationPromotionQualified':True,'oldPrimaryApplicationFencedByIndependentWitness':True,
 'oldPrimaryRewindQualified':True,'physicalFailbackToDispatchQualified':True,'formerHqPrimaryRewindQualified':True,
 'pitrPhysicalBaseBackupWalArchiveQualified':True,'pitrIncludedExcludedBoundaryQualified':True,
 'pitrTargetTime':target,'measuredPhysicalFailoverMilliseconds':int(fo),'measuredPhysicalFailbackMilliseconds':int(fb),
 'elapsedMilliseconds':int(end)-int(start),
 'databaseStonithImplementation':'Qualification harness explicit PostgreSQL process fencing before promotion; production HA manager/STONITH selection remains deployment-specific.'
}
open(out,'w').write(json.dumps(x,indent=2)+'\n'); print(json.dumps(x,indent=2))
PYEVID
printf '%s\n' 'PASS: PostgreSQL physical HA / independent witness / rewind / failback / PITR qualification completed.'
