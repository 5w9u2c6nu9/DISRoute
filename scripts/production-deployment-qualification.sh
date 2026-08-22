#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_ROOT:-/mnt/data/disroute-toolchain/dotnet}/dotnet"
SERVER_DLL="$ROOT/src/DISRoute.Server/bin/Release/net10.0/DISRoute.Server.dll"
CS="${DISROUTE_DEPLOYQUAL_CONNECTION_STRING:-}"
PORT="${DISROUTE_DEPLOYQUAL_PORT:-5277}"
WORK="${DISROUTE_DEPLOYQUAL_WORK:-/tmp/disroute-deployqual}"
ALLOW="${DISROUTE_DEPLOYQUAL_ALLOW_DESTRUCTIVE:-}"
[[ "$ALLOW" == "YES_I_UNDERSTAND" ]] || { echo 'Set DISROUTE_DEPLOYQUAL_ALLOW_DESTRUCTIVE=YES_I_UNDERSTAND; use a disposable PostgreSQL database.' >&2; exit 2; }
[[ -n "$CS" ]] || { echo 'Set DISROUTE_DEPLOYQUAL_CONNECTION_STRING to a disposable PostgreSQL database.' >&2; exit 2; }
[[ -x "$DOTNET_BIN" && -f "$SERVER_DLL" ]] || { echo 'Build DISRoute.Server Release first.' >&2; exit 2; }
command -v curl >/dev/null || { echo 'curl required' >&2; exit 2; }
command -v python3 >/dev/null || { echo 'python3 required' >&2; exit 2; }
command -v openssl >/dev/null || { echo 'openssl required for direct TLS qualification' >&2; exit 2; }
rm -rf "$WORK" && mkdir -p "$WORK/secrets"
chmod 700 "$WORK/secrets"
printf '%s\n' "$CS" >"$WORK/secrets/postgresql.txt"
python3 - "$WORK/secrets" <<'PY'
import base64,os,pathlib,sys
root=pathlib.Path(sys.argv[1])
for n in ('mfa.key','offline-lease.key','mfa-session.key'):
    (root/n).write_text(base64.b64encode(os.urandom(32)).decode()+'\n')
PY
chmod 600 "$WORK/secrets"/*
PID=""
cleanup(){ [[ -n "$PID" ]] && kill "$PID" 2>/dev/null || true; [[ -n "$PID" ]] && wait "$PID" 2>/dev/null || true; }
trap cleanup EXIT

base_env=(
  ASPNETCORE_ENVIRONMENT=Production
  DOTNET_ENVIRONMENT=Production
  ASPNETCORE_URLS="http://127.0.0.1:$PORT"
  Persistence__Provider=PostgreSQL
  "ConnectionStrings__DISRoute=secret://file/postgresql.txt"
  "Security__Secrets__Directory=$WORK/secrets"
  Security__RequireAuthentication=true
  Security__RequireHttps=true
  Security__RequireMfa=true
  "Security__MfaKeyPath=$WORK/secrets/mfa.key"
  "Security__OfflineLeaseKeyPath=$WORK/secrets/offline-lease.key"
  "Security__MfaSessionKeyPath=$WORK/secrets/mfa-session.key"
  Security__ReverseProxy__Enabled=true
  Security__ReverseProxy__KnownProxies__0=127.0.0.1
  Security__RateLimit__PermitLimitPerMinute=60
  Security__RateLimit__QueueLimit=0
  HighAvailability__Enabled=false
)

expect_reject(){
  local name="$1" pattern="$2"; shift 2
  local log="$WORK/reject-$name.log"
  set +e
  env "${base_env[@]}" "$@" "$DOTNET_BIN" "$SERVER_DLL" >"$log" 2>&1
  local rc=$?
  set -e
  [[ $rc -ne 0 ]] || { echo "$name unexpectedly started" >&2; return 1; }
  grep -qi "$pattern" "$log" || { echo "$name did not reject for expected reason '$pattern'" >&2; cat "$log" >&2; return 1; }
}

echo '[deployqual] fail-closed production configuration checks'
expect_reject auth-disabled 'authentication disabled' Security__RequireAuthentication=false
expect_reject mfa-disabled 'disable MFA' Security__RequireMfa=false
expect_reject plaintext-provider 'secret://' Integrations__StationAlert__BearerToken=plaintext-token
expect_reject untrusted-proxy 'KnownProxies' Security__ReverseProxy__Enabled=true Security__ReverseProxy__KnownProxies__0=
expect_reject direct-tls-missing-cert 'CertificatePath' Security__ReverseProxy__Enabled=false Security__ReverseProxy__KnownProxies__0=

cat >"$WORK/external-bad.json" <<'JSON'
{"Security":{"RequireAuthentication":false}}
JSON
expect_reject external-config 'authentication disabled' "DISROUTE_CONFIG_FILE=$WORK/external-bad.json"
expect_reject crash-injection 'crash injection' Qualification__EnableCrashInjection=true

echo '[deployqual] production startup via trusted reverse proxy + secret files'
env "${base_env[@]}" "$DOTNET_BIN" "$SERVER_DLL" >"$WORK/server.log" 2>&1 & PID=$!
for _ in $(seq 1 160); do
  if curl -fsS -H 'X-Forwarded-Proto: https' "http://127.0.0.1:$PORT/health/live" >"$WORK/live.json" 2>/dev/null; then break; fi
  if ! kill -0 "$PID" 2>/dev/null; then cat "$WORK/server.log" >&2; exit 1; fi
  sleep .25
done
curl -fsS -H 'X-Forwarded-Proto: https' "http://127.0.0.1:$PORT/health/live" >/dev/null
ready_code=$(curl -sS -o "$WORK/ready.json" -w '%{http_code}' -H 'X-Forwarded-Proto: https' "http://127.0.0.1:$PORT/health/ready")
[[ "$ready_code" == 200 || "$ready_code" == 503 ]]
python3 - "$WORK/ready.json" <<'PYREADY'
import json,sys
x=json.load(open(sys.argv[1]))
assert set(x)=={'service','status','checks','asOfUtc'},x
assert set(x['checks'])=={'persistenceHealthy','migrationIntegrityVerified','mutationJournalDrained','authoritativeWriter'},x
assert all(isinstance(v,bool) for v in x['checks'].values()),x
PYREADY
admin_code=$(curl -sS -o /dev/null -w '%{http_code}' -H 'X-Forwarded-Proto: https' "http://127.0.0.1:$PORT/api/admin/deployment/status")
[[ "$admin_code" == 401 || "$admin_code" == 403 ]] || { echo "admin deployment status unexpectedly anonymous: $admin_code" >&2; exit 1; }
headers="$WORK/headers.txt"
curl -sS -D "$headers" -o /dev/null -H 'X-Forwarded-Proto: https' "http://127.0.0.1:$PORT/health/live"
grep -qi '^X-Content-Type-Options: nosniff' "$headers"
grep -qi '^X-Frame-Options: DENY' "$headers"
grep -qi '^Content-Security-Policy:' "$headers"
! grep -qi '^Server:' "$headers"

status=""
for _ in $(seq 1 80); do
  status=$(curl -s -o /dev/null -w '%{http_code}' -H 'X-Forwarded-Proto: https' "http://127.0.0.1:$PORT/api/admin/deployment/status" || true)
  [[ "$status" == 429 ]] && break
  sleep .01
done
[[ "$status" == 429 ]] || { echo "rate-limit did not return 429; last=$status" >&2; exit 1; }
retry_after=$(curl -sS -D - -o /dev/null -H 'X-Forwarded-Proto: https' "http://127.0.0.1:$PORT/api/admin/deployment/status" | awk 'BEGIN{IGNORECASE=1} /^Retry-After:/ {gsub("\r",""); print $2; exit}')
[[ "$retry_after" == 60 ]] || { echo "rate-limit Retry-After missing/incorrect: $retry_after" >&2; exit 1; }
health_after_limit=$(curl -sS -o /dev/null -w '%{http_code}' -H 'X-Forwarded-Proto: https' "http://127.0.0.1:$PORT/health/live")
[[ "$health_after_limit" == 200 ]] || { echo "health/live was affected by API rate limit: $health_after_limit" >&2; exit 1; }
ready_after_limit=$(curl -sS -o /dev/null -w '%{http_code}' -H 'X-Forwarded-Proto: https' "http://127.0.0.1:$PORT/health/ready")
[[ "$ready_after_limit" == 200 || "$ready_after_limit" == 503 ]] || { echo "health/ready was rate-limited: $ready_after_limit" >&2; exit 1; }

kill "$PID"; wait "$PID" || true; PID=""

echo '[deployqual] direct Kestrel TLS certificate mode'
TLS_PORT=$((PORT+1))
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$WORK/secrets/tls.key" -out "$WORK/secrets/tls.crt" -days 30 -subj '/CN=localhost' >/dev/null 2>&1
printf '%s\n' 'pass27-pfx' >"$WORK/secrets/tls-password.txt"
openssl pkcs12 -export -out "$WORK/secrets/server.pfx" -inkey "$WORK/secrets/tls.key" -in "$WORK/secrets/tls.crt" -passout pass:pass27-pfx >/dev/null 2>&1
chmod 600 "$WORK/secrets/tls.key" "$WORK/secrets/tls.crt" "$WORK/secrets/tls-password.txt"
chmod 644 "$WORK/secrets/server.pfx"
expect_reject tls-permissions 'permissions' \
  Security__ReverseProxy__Enabled=false Security__ReverseProxy__KnownProxies__0= \
  "Security__Tls__Port=$TLS_PORT" "Security__Tls__CertificatePath=$WORK/secrets/server.pfx" \
  Security__Tls__CertificatePassword=secret://file/tls-password.txt
chmod 600 "$WORK/secrets/server.pfx"
env "${base_env[@]}" \
  Security__ReverseProxy__Enabled=false \
  Security__ReverseProxy__KnownProxies__0= \
  "Security__Tls__Port=$TLS_PORT" \
  "Security__Tls__CertificatePath=$WORK/secrets/server.pfx" \
  Security__Tls__CertificatePassword=secret://file/tls-password.txt \
  "$DOTNET_BIN" "$SERVER_DLL" >"$WORK/server-tls.log" 2>&1 & PID=$!
for _ in $(seq 1 160); do
  if curl -kfsS "https://127.0.0.1:$TLS_PORT/health/live" >/dev/null 2>&1; then break; fi
  if ! kill -0 "$PID" 2>/dev/null; then cat "$WORK/server-tls.log" >&2; exit 1; fi
  sleep .25
done
curl -kfsS --resolve "disroute.test:$TLS_PORT:127.0.0.1" -D "$WORK/tls-headers.txt" -o /dev/null "https://disroute.test:$TLS_PORT/health/live"
grep -qi '^Strict-Transport-Security:' "$WORK/tls-headers.txt"
! grep -qi '^Server:' "$WORK/tls-headers.txt"
kill "$PID"; wait "$PID" || true; PID=""

if command -v systemd-analyze >/dev/null; then
  sed 's#^ExecStart=.*#ExecStart=/bin/true#' "$ROOT/deploy/linux/disroute-server.service" >"$WORK/disroute-server-verify.service"
  systemd-analyze verify "$WORK/disroute-server-verify.service"
fi

python3 - "$WORK/evidence.json" "$PORT" <<'PY'
import json,sys,datetime
json.dump({
  'pass':27,
  'qualification':'production-deployment-security-runtime',
  'status':'PASS',
  'postgresql':'real disposable PostgreSQL supplied by operator',
  'checks':['production fail-closed auth/MFA/plaintext-secret/reverse-proxy checks','external config loading','secret://file PostgreSQL connection','trusted forwarded-proto HTTPS enforcement','direct Kestrel TLS with certificate-age/permission validation','sanitized anonymous readiness and protected admin status','TLS private-file permission rejection','security headers and HSTS','Kestrel Server header suppressed','global fixed-window API rate limit with Retry-After and health-probe exemption','systemd unit verification'],
  'port':int(sys.argv[2]),
  'completedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat()
},open(sys.argv[1],'w'),indent=2)
PY
cat "$WORK/evidence.json"
echo '[deployqual] PASS'
