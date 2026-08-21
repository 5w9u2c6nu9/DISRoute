#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_ROOT:-/mnt/data/dotnet}/dotnet"
PORT="${DISROUTE_SMOKE_PORT:-5109}"
BASE="http://127.0.0.1:${PORT}"
WORK="${DISROUTE_SMOKE_WORK:-/tmp/disroute-runtime-smoke}"
STATE="$WORK/disroute-state.json"
JOURNAL="$WORK/disroute-command-journal.ndjson"
LOG1="$WORK/server-1.log"
LOG2="$WORK/server-2.log"
LOG3="$WORK/server-3.log"
IDFILE="$WORK/incident-id.txt"
BACKUPFILE="$WORK/backup-file.txt"
TRANSIENTFILE="$WORK/transient-incident-id.txt"
REPLAYFILE="$WORK/replay-at.txt"
RMSFILE="$WORK/rms-report-id.txt"
NERISFILE="$WORK/neris-submission-id.txt"
WORKFORCEFILE="$WORK/workforce-roster.json"
DMSFILE="$WORK/dms-state.json"
CHATFILE="$WORK/chat-state.json"
MOBILEFILE="$WORK/mobile-state.json"
INTEGRATIONFILE="$WORK/integration-state.json"

rm -rf "$WORK"
mkdir -p "$WORK"

export DOTNET_ROOT="$(dirname "$DOTNET_BIN")"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/mnt/data/dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/mnt/data/nuget-packages}"
export NUGET_CERT_REVOCATION_MODE="${NUGET_CERT_REVOCATION_MODE:-offline}"
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="$BASE"
export Persistence__Provider=FileCheckpoint
export Persistence__CheckpointPath="$STATE"
export Persistence__JournalPath="$JOURNAL"
export Persistence__CheckpointSeconds=1
export Integrations__FireReporting__Endpoint="http://127.0.0.1:1/neris"

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
  local server_dll="$ROOT/src/DISRoute.Server/bin/Release/net10.0/DISRoute.Server.dll"
  [[ -f "$server_dll" ]] || { echo "Release server build not found at $server_dll" >&2; exit 1; }
  (cd "$ROOT/src/DISRoute.Server" && "$DOTNET_BIN" "$server_dll" --urls "$BASE") >"$log" 2>&1 &
  PID=$!
  for _ in $(seq 1 80); do
    if curl -fsS "$BASE/health" >/dev/null 2>&1; then
      return 0
    fi
    if ! kill -0 "$PID" 2>/dev/null; then
      echo "DISRoute.Server exited before becoming healthy" >&2
      cat "$log" >&2
      exit 1
    fi
    sleep 0.25
  done
  echo "DISRoute.Server did not become healthy" >&2
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

python3 - "$BASE" "$IDFILE" "$BACKUPFILE" "$REPLAYFILE" "$RMSFILE" "$NERISFILE" "$WORKFORCEFILE" "$DMSFILE" "$CHATFILE" "$MOBILEFILE" "$INTEGRATIONFILE" <<'PY'
import json, sys, urllib.request, urllib.error, urllib.parse
from datetime import datetime, timezone
import websocket
base, idfile, backupfile, replayfile, rmsfile, nerisfile, workforcefile, dmsfile, chatfile, mobilefile, integrationfile = sys.argv[1:]

def call(path, method="GET", body=None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(base + path, data=data, method=method,
                                 headers={"Content-Type":"application/json"} if data else {})
    try:
        with urllib.request.urlopen(req, timeout=10) as r:
            raw = r.read().decode()
            return r.status, json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"{method} {path} -> HTTP {e.code}: {e.read().decode()}") from e

_, agencies = call('/api/agencies')
_, stations = call('/api/stations')
_, units = call('/api/units')
assert agencies and stations and units, "seed agency/station/unit data missing"
agency = agencies[0]
station = stations[0]
unit = units[0]

status, incident = call('/api/incidents', 'POST', {
    'agencyId': agency['id'],
    'stationId': station['id'],
    'nature': 'Runtime Persistence Smoke Test',
    'priority': 'Emergency',
    'address': '100 Runtime Test Way',
    'crossStreets': 'Compile Ave / Restore Blvd',
    'municipality': 'DISRoute Test',
    'callerName': 'Runtime Harness',
    'callerPhone': '555-0109',
    'notes': 'Created by scripts/runtime-smoke.sh',
    'idempotencyKey': 'runtime-smoke-create'
})
assert status == 201 and incident['version'] == 1 and incident['status'] == 'Open'
incident_id = incident['id']

_, incident = call(f'/api/incidents/{incident_id}/assign', 'POST', {
    'unitId': unit['id'],
    'expectedIncidentVersion': incident['version'],
    'allowReassignment': True,
    'idempotencyKey': 'runtime-smoke-assign'
})
assert incident['version'] == 2 and incident['status'] == 'Dispatched'

_, activity = call(f'/api/incidents/{incident_id}/narrative', 'POST', {
    'text': 'Runtime smoke narrative entry.',
    'idempotencyKey': 'runtime-smoke-narrative'
})
assert activity['incidentId'] == incident_id
replay_at = datetime.now(timezone.utc).isoformat().replace('+00:00', 'Z')
_, historical = call(f'/api/replay/incidents/{incident_id}/snapshot?atUtc={urllib.parse.quote(replay_at)}')
assert historical['incident']['status'] == 'Dispatched', historical
assert any(x.get('category') == 'NARRATIVE' and 'Runtime smoke narrative entry.' in x.get('message','') for x in historical['activity']), historical['activity']
with open(replayfile, 'w', encoding='utf-8') as f:
    f.write(replay_at)

_, incident = call(f'/api/incidents/{incident_id}/close', 'POST', {
    'expectedVersion': incident['version'],
    'reason': 'Runtime smoke test complete',
    'idempotencyKey': 'runtime-smoke-close'
})
assert incident['version'] == 3 and incident['status'] == 'Closed' and incident['closedAtUtc']

# Pass 18 regression: create and finalize the canonical RMS report through the HTTP API
# before backup, so restart and staged restore must retain the complete records lifecycle.
_, rms = call(f'/api/rms/incident-reports/from-incident/{incident_id}', 'POST', {
    'agencyId': agency['id'],
    'reportType': 'FireIncident',
    'summary': 'Runtime RMS handoff summary',
    'idempotencyKey': 'runtime-smoke-rms-create'
})
assert rms['incidentId'] == incident_id and rms['status'] == 'Draft' and rms['closedAtUtc'], rms
_, rms_validation = call(f"/api/rms/incident-reports/{rms['id']}/validation")
assert rms_validation['canonicalReady'] is True and rms_validation['providerValidationPending'] is True, rms_validation
_, rms = call(f"/api/rms/incident-reports/{rms['id']}/submit-review", 'POST', {
    'expectedVersion': rms['version'], 'notes': 'Runtime review', 'idempotencyKey': 'runtime-smoke-rms-submit'
})
_, rms = call(f"/api/rms/incident-reports/{rms['id']}/review", 'POST', {
    'expectedVersion': rms['version'], 'decision': 'approve', 'notes': 'Runtime approval', 'idempotencyKey': 'runtime-smoke-rms-finalize'
})
assert rms['status'] == 'Finalized' and rms['finalizedAtUtc'], rms
with open(rmsfile, 'w', encoding='utf-8') as f:
    f.write(rms['id'])

# Pass 19 regression: build the finalized RMS -> NERIS projection, force one provider
# failure, retry through the simulation provider, and preserve immutable attempt history.
_, neris = call(f"/api/rms/incident-reports/{rms['id']}/neris/build", 'POST', {
    'profileVersion': 'DISRoute-NERIS-Bridge-2026.1',
    'idempotencyKey': 'runtime-smoke-neris-build'
})
assert neris['state'] == 'Ready' and len(neris['payloadSha256']) == 64 and neris['rmsReportVersion'] == rms['version'], neris
_, provider_validation = call(f"/api/rms/neris-submissions/{neris['id']}/provider-validation?providerKey=simulation-fire-reporting", 'POST')
assert provider_validation['valid'] is True, provider_validation
_, neris = call(f"/api/rms/neris-submissions/{neris['id']}/queue", 'POST', {
    'providerKey': 'http-fire-reporting',
    'idempotencyKey': 'runtime-smoke-neris-queue-failing'
})
assert neris['state'] == 'Queued', neris
import time
for _ in range(80):
    _, neris = call(f"/api/rms/neris-submissions/{neris['id']}")
    if neris['state'] == 'Failed':
        break
    time.sleep(0.1)
assert neris['state'] == 'Failed' and neris['attemptCount'] == 1 and neris['lastError'], neris
_, attempts = call(f"/api/rms/neris-submissions/{neris['id']}/attempts")
assert len(attempts) == 1 and attempts[0]['state'] == 'Failed' and attempts[0]['providerKey'] == 'http-fire-reporting', attempts
_, neris = call(f"/api/rms/neris-submissions/{neris['id']}/retry", 'POST', {
    'providerKey': 'simulation-fire-reporting',
    'confirmPossibleDuplicate': False,
    'idempotencyKey': 'runtime-smoke-neris-retry'
})
assert neris['state'] == 'Queued', neris
for _ in range(80):
    _, neris = call(f"/api/rms/neris-submissions/{neris['id']}")
    if neris['state'] in ('Accepted', 'Confirmed'):
        break
    time.sleep(0.1)
assert neris['state'] in ('Accepted', 'Confirmed') and neris['attemptCount'] == 2 and neris['providerReference'], neris
_, rms_validation_after_neris = call(f"/api/rms/incident-reports/{rms['id']}/validation")
assert rms_validation_after_neris['providerValidationPending'] is False, rms_validation_after_neris
_, attempts = call(f"/api/rms/neris-submissions/{neris['id']}/attempts")
assert len(attempts) == 2 and attempts[0]['state'] == 'Failed' and attempts[1]['state'] in ('Accepted', 'Confirmed'), attempts
with open(nerisfile, 'w', encoding='utf-8') as f:
    f.write(neris['id'])

# Pass 20 regression: exercise a real posted roster lifecycle through HTTP before the
# checkpoint.  Staffing deficiencies must be visible and must never auto-fill themselves.
work_date = datetime.now(timezone.utc).date().isoformat()
_, workforce_rule = call('/api/workforce/staffing-rules', 'POST', {
    'agencyId': agency['id'],
    'stationId': station['id'],
    'shiftName': 'Runtime Day',
    'position': 'Runtime Driver',
    'minimumCount': 1,
    'requiredQualification': '',
    'enabled': True
})
_, workforce_assignment = call('/api/workforce/assignments', 'POST', {
    'agencyId': agency['id'],
    'date': work_date,
    'shiftName': 'Runtime Day',
    'stationId': station['id'],
    'unitId': unit['id'],
    'position': 'Runtime Driver',
    'employeeId': 'runtime-roster-1',
    'employeeName': 'Runtime Roster Employee',
    'qualification': '',
    'assignmentType': 'Regular',
    'state': 'Assigned',
    'effectiveAtUtc': None,
    'endsAtUtc': None,
    'source': 'RuntimeSmoke'
})
_, staffing = call(f"/api/workforce/staffing-evaluation?agencyId={agency['id']}&date={work_date}")
assert staffing['requiredPositions'] == 1 and staffing['filledPositions'] == 1 and staffing['meetsMinimums'] is True, staffing
_, workforce_snapshot_1 = call(f"/api/workforce/roster/publish?agencyId={agency['id']}&date={work_date}", 'POST', {
    'notes': 'Runtime posted roster revision 1'
})
assert workforce_snapshot_1['revision'] == 1 and workforce_snapshot_1['state'] == 'Posted' and len(workforce_snapshot_1['contentSha256']) == 64, workforce_snapshot_1
_, called_out = call(f"/api/workforce/assignments/{workforce_assignment['id']}/call-out", 'POST', {
    'notes': 'Runtime call-out vacancy'
})
assert called_out['state'] == 'CallOut', called_out
_, staffing = call(f"/api/workforce/staffing-evaluation?agencyId={agency['id']}&date={work_date}")
assert staffing['vacancies'] == 1 and staffing['meetsMinimums'] is False, staffing
_, current_roster = call(f"/api/workforce/roster/current?agencyId={agency['id']}&date={work_date}")
open_slots = [x for x in current_roster['assignments'] if x['state'] == 'Open' and x['position'] == 'Runtime Driver']
assert len(open_slots) == 1 and not open_slots[0]['employeeName'], current_roster['assignments']
open_slot = open_slots[0]
_, overtime_assignment = call(f"/api/workforce/assignments/{open_slot['id']}", 'PUT', {
    'agencyId': open_slot['agencyId'],
    'date': open_slot['date'],
    'shiftName': open_slot['shiftName'],
    'stationId': open_slot['stationId'],
    'unitId': open_slot['unitId'],
    'position': open_slot['position'],
    'employeeId': 'runtime-overtime-1',
    'employeeName': 'Runtime Overtime Employee',
    'qualification': open_slot['qualification'],
    'assignmentType': 'Overtime',
    'state': 'Overtime',
    'effectiveAtUtc': open_slot['effectiveAtUtc'],
    'endsAtUtc': open_slot['endsAtUtc'],
    'source': 'RuntimeSmokeCoverage'
})
assert overtime_assignment['state'] == 'Overtime' and overtime_assignment['employeeName'] == 'Runtime Overtime Employee', overtime_assignment
_, staffing = call(f"/api/workforce/staffing-evaluation?agencyId={agency['id']}&date={work_date}")
assert staffing['requiredPositions'] == 1 and staffing['filledPositions'] == 1 and staffing['meetsMinimums'] is True, staffing
_, workforce_snapshot_2 = call(f"/api/workforce/roster/publish?agencyId={agency['id']}&date={work_date}", 'POST', {
    'notes': 'Runtime posted roster revision 2'
})
assert workforce_snapshot_2['revision'] == 2 and workforce_snapshot_2['state'] == 'Posted' and workforce_snapshot_2['contentSha256'] != workforce_snapshot_1['contentSha256'], workforce_snapshot_2
_, roster_print = call(f"/api/workforce/roster/print-view?agencyId={agency['id']}&date={work_date}&includeChanges=true")
assert 'SAME-DAY CHANGES' in roster_print['content'] and 'Runtime Overtime Employee' in roster_print['content'] and len(roster_print['contentSha256']) == 64, roster_print
_, roster_print_result = call(f"/api/workforce/roster/print?agencyId={agency['id']}&date={work_date}&includeChanges=true&printerKey=runtime-smoke", 'POST', {})
assert roster_print_result['state'] in ('Printed', 'Spooled', 'Confirmed') and roster_print_result['providerReference'], roster_print_result
with open(workforcefile, 'w', encoding='utf-8') as f:
    json.dump({
        'agencyId': agency['id'],
        'date': work_date,
        'ruleId': workforce_rule['id'],
        'snapshot1Id': workforce_snapshot_1['id'],
        'snapshot1Hash': workforce_snapshot_1['contentSha256'],
        'snapshot2Id': workforce_snapshot_2['id'],
        'snapshot2Hash': workforce_snapshot_2['contentSha256']
    }, f)

# Pass 21 regression: exercise approval-gated DMS publication, required-read tracking,
# entity linkage, and immutable form signature metadata before checkpoint/backup.
_, dms_doc = call('/api/documents/dms', 'POST', {
    'agencyId': agency['id'],
    'title': 'Runtime DMS Policy',
    'classification': 'Internal',
    'content': 'Runtime DMS revision one'
})
_, dms_revisions = call(f"/api/documents/dms/{dms_doc['id']}/revisions")
assert len(dms_revisions) == 1 and dms_revisions[0]['version'] == 1 and dms_revisions[0]['state'] == 'Draft', dms_revisions
revision1 = dms_revisions[0]
_, dms_asset = call(f"/api/documents/dms/{dms_doc['id']}/assets", 'POST', {
    'documentVersion': 1,
    'fileName': 'runtime-policy.pdf',
    'contentType': 'application/pdf',
    'storageReference': 'dms://runtime/policies/runtime-policy-v1',
    'contentSha256': 'a' * 64,
    'classification': 'Internal'
})
assert dms_asset['documentVersion'] == 1 and len(dms_asset['contentSha256']) == 64, dms_asset
_, dms_workflow = call(f"/api/documents/dms/{dms_doc['id']}/revisions/1/approval", 'POST', {
    'name': 'Runtime DMS Approval',
    'mode': 'Sequential',
    'steps': [{'sequence': 1, 'approver': 'Supervisor', 'required': True}]
})
_, dms_steps = call(f"/api/documents/workflows/{dms_workflow['id']}/steps")
assert len(dms_steps) == 1 and dms_steps[0]['state'] == 'Pending', dms_steps
_, _ = call(f"/api/documents/workflow-steps/{dms_steps[0]['id']}/decide", 'POST', {
    'actor': 'ignored-client-actor', 'decision': 'Approve', 'comment': 'Runtime approval'
})
_, revision1 = call(f"/api/documents/dms/{dms_doc['id']}/revisions/1/publish", 'POST', {'requireApprovedWorkflow': True})
assert revision1['state'] == 'Published' and len(revision1['contentSha256']) == 64, revision1
_, distribution = call(f"/api/documents/dms/{dms_doc['id']}/distributions", 'POST', {
    'documentVersion': 1,
    'scopeType': 'User',
    'scopeValue': 'local-development',
    'requiredRead': True,
    'dueAtUtc': None,
    'recipients': ['local-development']
})
_, dms_receipts = call(f"/api/documents/dms/{dms_doc['id']}/read-receipts")
assert len(dms_receipts) == 1 and dms_receipts[0]['state'] == 'Assigned', dms_receipts
_, dms_receipt = call(f"/api/documents/dms/read-receipts/{dms_receipts[0]['id']}/acknowledge", 'POST', {})
assert dms_receipt['state'] == 'Acknowledged' and dms_receipt['acknowledgedAtUtc'], dms_receipt
_, dms_link = call(f"/api/documents/dms/{dms_doc['id']}/links", 'POST', {
    'documentVersion': 1,
    'entityType': 'Incident',
    'entityKey': incident_id,
    'relationship': 'Runtime operational reference'
})
assert dms_link['entityType'] == 'Incident' and dms_link['entityKey'] == incident_id, dms_link
_, revision2 = call(f"/api/documents/dms/{dms_doc['id']}/revisions", 'POST', {'content': 'Runtime DMS revision two'})
assert revision2['version'] == 2 and revision2['state'] == 'Draft', revision2
_, dms_workflow2 = call(f"/api/documents/dms/{dms_doc['id']}/revisions/2/approval", 'POST', {
    'name': 'Runtime DMS Revision 2 Approval',
    'mode': 'Sequential',
    'steps': [{'sequence': 1, 'approver': 'Supervisor', 'required': True}]
})
_, dms_steps2 = call(f"/api/documents/workflows/{dms_workflow2['id']}/steps")
_, _ = call(f"/api/documents/workflow-steps/{dms_steps2[0]['id']}/decide", 'POST', {'actor': 'ignored', 'decision': 'Approve', 'comment': 'Revision 2 approved'})
_, revision2 = call(f"/api/documents/dms/{dms_doc['id']}/revisions/2/publish", 'POST', {'requireApprovedWorkflow': True})
assert revision2['state'] == 'Published' and revision2['contentSha256'] != revision1['contentSha256'], revision2
_, dms_revisions = call(f"/api/documents/dms/{dms_doc['id']}/revisions")
by_version = {x['version']: x for x in dms_revisions}
assert by_version[1]['state'] == 'Superseded' and by_version[1]['content'] == 'Runtime DMS revision one' and by_version[2]['state'] == 'Published', by_version

_, form_template = call('/api/forms/templates', 'POST', {
    'agencyId': agency['id'], 'name': 'Runtime Operational Form', 'schemaJson': '{"fields":[]}', 'createdBy': 'ignored-client-actor'
})
_, form_template = call(f"/api/forms/templates/{form_template['id']}/publish", 'POST', {})
assert form_template['status'] == 'Published', form_template
_, form = call('/api/forms', 'POST', {
    'agencyId': agency['id'], 'templateId': form_template['id'], 'incidentId': incident_id, 'valuesJson': '{"runtime":true}', 'createdBy': 'ignored-client-actor'
})
_, form_signature = call(f"/api/forms/{form['id']}/sign-strong", 'POST', {'meaning': 'Runtime certification'})
assert len(form_signature['valuesSha256']) == 64 and form_signature['templateVersion'] == form_template['version'], form_signature
with open(dmsfile, 'w', encoding='utf-8') as f:
    json.dump({
        'documentId': dms_doc['id'],
        'revision1Hash': revision1['contentSha256'],
        'revision2Hash': revision2['contentSha256'],
        'receiptId': dms_receipt['id'],
        'assetId': dms_asset['id'],
        'linkId': dms_link['id'],
        'formId': form['id'],
        'formSignatureHash': form_signature['valuesSha256']
    }, f)

# Pass 22.1 regression: prove internal chat is delivered over the real SignalR websocket,
# while the HTTP API still provides durable history/read/search semantics.
_, chat_channel = call('/api/chat/channels', 'POST', {
    'ownerAgencyId': agency['id'], 'type': 'Agency', 'name': 'Runtime Agency Chat',
    'topic': 'Runtime communications durability', 'stationId': None, 'incidentId': None,
    'participatingAgencyIds': None, 'memberUserIds': None, 'allowedRoles': None
})

# The server, not a caller-supplied agency id, owns chat/presence scope.
foreign_agency = '11111111-1111-1111-1111-111111111111'
try:
    urllib.request.urlopen(base + f'/api/chat/channels?agencyId={foreign_agency}', timeout=10)
    raise AssertionError('foreign-agency chat query unexpectedly succeeded')
except urllib.error.HTTPError as e:
    assert e.code in (401, 403), e.code

# Negotiate and join the same SignalR groups used by the desktop client.
req = urllib.request.Request(base + '/hubs/operations/negotiate?negotiateVersion=1', data=b'', method='POST')
with urllib.request.urlopen(req, timeout=10) as r:
    negotiate = json.loads(r.read().decode())
token = negotiate.get('connectionToken') or negotiate.get('connectionId')
assert token, negotiate
ws_base = base.replace('http://', 'ws://', 1).replace('https://', 'wss://', 1)
ws = websocket.create_connection(ws_base + '/hubs/operations?id=' + urllib.parse.quote(token), timeout=10, http_proxy_host=None, http_proxy_port=None)
RS = '\x1e'

def recv_hub_messages(deadline_messages=30):
    out = []
    for _ in range(deadline_messages):
        raw = ws.recv()
        if isinstance(raw, bytes):
            raw = raw.decode('utf-8')
        for frame in raw.split(RS):
            if frame.strip():
                out.append(json.loads(frame))
        if out:
            return out
    raise AssertionError('no SignalR message received')

try:
    ws.send(json.dumps({'protocol': 'json', 'version': 1}, separators=(',', ':')) + RS)
    handshake = recv_hub_messages()
    assert any(m == {} or 'error' not in m for m in handshake), handshake

    invocation = 0
    def invoke(target, arguments):
        global invocation
        invocation += 1
        iid = str(invocation)
        ws.send(json.dumps({'type': 1, 'invocationId': iid, 'target': target, 'arguments': arguments}, separators=(',', ':')) + RS)
        for _ in range(30):
            for msg in recv_hub_messages():
                if msg.get('type') == 3 and msg.get('invocationId') == iid:
                    assert 'error' not in msg, msg
                    return msg
        raise AssertionError(f'no completion for {target}')

    invoke('JoinAgency', [agency['id']])
    invoke('JoinChatChannel', [chat_channel['id']])
    invoke('SetPresence', [agency['id'], None, 'RuntimeSmoke', 'Chat'])

    _, presence = call('/api/presence')
    assert presence and all(x['agencyId'] == agency['id'] for x in presence), presence

    _, chat_message = call(f"/api/chat/channels/{chat_channel['id']}/messages", 'POST', {
        'body': '@local-development runtime communications persistence check',
        'priority': 'Important', 'classification': 'Internal', 'linkedIncidentId': incident_id,
        'mentions': ['local-development'],
        'attachments': [{'id': '00000000-0000-0000-0000-000000000000', 'fileName': 'runtime-chat.txt', 'contentType': 'text/plain', 'storageReference': 'dms://runtime/chat/runtime-chat', 'contentSha256': 'b' * 64, 'classification': 'Internal'}]
    })
    assert chat_message['linkedIncidentId'] == incident_id and len(chat_message['attachments']) == 1 and chat_message['attachments'][0]['id'] != '00000000-0000-0000-0000-000000000000', chat_message

    live = None
    for _ in range(40):
        for msg in recv_hub_messages():
            if msg.get('type') == 1 and msg.get('target') == 'chatMessage' and msg.get('arguments'):
                candidate = msg['arguments'][0]
                if candidate.get('id') == chat_message['id']:
                    live = candidate
                    break
        if live is not None:
            break
    assert live is not None and live['channelId'] == chat_channel['id'], live
finally:
    ws.close()

_, chat_read = call(f"/api/chat/channels/{chat_channel['id']}/read", 'POST', {'throughMessageId': chat_message['id']})
assert chat_read['lastReadMessageId'] == chat_message['id'], chat_read
_, chat_search = call(f"/api/chat/search?agencyId={agency['id']}&q=communications&limit=20")
assert any(x['message']['id'] == chat_message['id'] for x in chat_search), chat_search
with open(chatfile, 'w', encoding='utf-8') as f:
    json.dump({'agencyId': agency['id'], 'channelId': chat_channel['id'], 'messageId': chat_message['id'], 'incidentId': incident_id}, f)

# Pass 23 regression: exercise the real assignment/incident field package and the signed
# offline authorization lease over HTTP before checkpoint, then verify it after restart/restore.
_, _ = call('/api/security/users/local-development', 'PUT', {
    'roles': ['Dispatcher', 'Supervisor', 'Administrator'],
    'totpEnrolled': False, 'passkeyEnrolled': False, 'breakGlass': True, 'state': 'Active'
})
mobile_device_id = 'runtime-mobile-device'
_, mobile_device = call('/api/security/devices', 'POST', {
    'agencyId': agency['id'], 'deviceId': mobile_device_id, 'name': 'Runtime Mobile Device',
    'role': 'Mobile', 'platform': 'Android', 'certificateThumbprint': None
})
mobile_unit = units[1] if len(units) > 1 else units[0]
_, mobile_incident = call('/api/incidents', 'POST', {
    'agencyId': agency['id'], 'stationId': station['id'], 'nature': 'Runtime Mobile Field Test',
    'priority': 'Emergency', 'address': '200 Mobile Test Way', 'crossStreets': '',
    'municipality': 'DISRoute Test', 'callerName': 'Mobile Harness', 'callerPhone': '555-0123',
    'notes': 'Pass 23 mobile/offline persistence test', 'idempotencyKey': 'runtime-smoke-mobile-create'
})
_, mobile_incident = call(f"/api/incidents/{mobile_incident['id']}/assign", 'POST', {
    'unitId': mobile_unit['id'], 'expectedIncidentVersion': mobile_incident['version'],
    'allowReassignment': True, 'idempotencyKey': 'runtime-smoke-mobile-assign'
})
_, mobile_sync = call('/api/mobile/sync', 'POST', {
    'agencyId': agency['id'], 'deviceId': mobile_device_id, 'sinceUtc': None,
    'queuedOperations': [], 'unitId': mobile_unit['id'], 'offlineAuthorizationLease': None
})
package = mobile_sync['operationalPackage']
assert package['selectedUnitId'] == mobile_unit['id'] and package['currentIncident']['id'] == mobile_incident['id'], package
assert mobile_sync['connectivity']['state'] == 'Online', mobile_sync['connectivity']
_, lease = call('/api/me/offline-lease', 'POST', {
    'agencyId': agency['id'], 'userId': 'local-development', 'deviceId': mobile_device_id,
    'permissions': ['unit.change-status'], 'validMinutes': 30
})
assert lease['token'] and lease['deviceId'] == mobile_device_id, lease
observed = package['selectedUnit']
target_status = 'EnRoute' if observed['status'] == 'OnScene' else 'OnScene'
now_iso = datetime.now(timezone.utc).isoformat().replace('+00:00', 'Z')
mobile_ops = [
    {'operationId': '23000000-0000-0000-0000-000000000001', 'kind': 'Narrative', 'incidentId': mobile_incident['id'],
     'payloadJson': json.dumps({'text':'Runtime mobile offline narrative'}), 'occurredAtUtc': now_iso},
    {'operationId': '23000000-0000-0000-0000-000000000002', 'kind': 'ChatMessage', 'incidentId': mobile_incident['id'],
     'payloadJson': json.dumps({'channelId':chat_channel['id'], 'text':'Runtime mobile offline chat'}), 'occurredAtUtc': now_iso},
    {'operationId': '23000000-0000-0000-0000-000000000003', 'kind': 'UnitStatus', 'incidentId': mobile_incident['id'],
     'payloadJson': json.dumps({'unitId':mobile_unit['id'], 'fromStatus':observed['status'], 'toStatus':target_status, 'observedUnitUpdatedAtUtc':observed['updatedAtUtc']}), 'occurredAtUtc': now_iso}
]
_, mobile_reconciled = call('/api/mobile/sync', 'POST', {
    'agencyId': agency['id'], 'deviceId': mobile_device_id, 'sinceUtc': None,
    'queuedOperations': mobile_ops, 'unitId': mobile_unit['id'], 'offlineAuthorizationLease': lease['token']
})
assert all(x['state'] == 'Accepted' for x in mobile_reconciled['operationResults']), mobile_reconciled['operationResults']
assert mobile_reconciled['operationalPackage']['selectedUnit']['status'] == target_status, mobile_reconciled['operationalPackage']['selectedUnit']
# Explicit duplicate replay is safe and must not reapply commands.
_, mobile_duplicate = call('/api/mobile/sync', 'POST', {
    'agencyId': agency['id'], 'deviceId': mobile_device_id, 'sinceUtc': None,
    'queuedOperations': mobile_ops, 'unitId': mobile_unit['id'], 'offlineAuthorizationLease': lease['token']
})
assert all(x['state'] == 'DuplicateIgnored' for x in mobile_duplicate['operationResults']), mobile_duplicate['operationResults']
with open(mobilefile, 'w', encoding='utf-8') as f:
    json.dump({'agencyId': agency['id'], 'deviceId': mobile_device_id, 'unitId': mobile_unit['id'],
               'incidentId': mobile_incident['id'], 'targetStatus': target_status, 'chatChannelId': chat_channel['id']}, f)

# Pass 24 regression: normalize gateway input and execute provider-backed operations through
# the same durable operation/attempt boundary used by production adapters.
gateway_time = datetime.now(timezone.utc).isoformat().replace('+00:00', 'Z')
_, gateway_911 = call('/api/integrations/911/events', 'POST', {
    'agencyId': agency['id'], 'providerKey': 'simulation-911', 'providerCallId': 'RUNTIME-CALL-24',
    'eventState': 'Open', 'callerName': 'Runtime Gateway Caller', 'callbackNumber': '555-0240',
    'suppliedLocation': '240 Gateway Runtime Way', 'nature': 'Automatic alarm', 'sourceTimestamp': gateway_time
})
_, gateway_911_dup = call('/api/integrations/911/events', 'POST', {
    'agencyId': agency['id'], 'providerKey': 'simulation-911', 'providerCallId': 'RUNTIME-CALL-24',
    'eventState': 'Open', 'callerName': 'Runtime Gateway Caller', 'callbackNumber': '555-0240',
    'suppliedLocation': '240 Gateway Runtime Way', 'nature': 'Automatic alarm', 'sourceTimestamp': gateway_time
})
assert gateway_911_dup['id'] == gateway_911['id'], (gateway_911, gateway_911_dup)
_, gateway_ais = call('/api/integrations/ais/events', 'POST', {
    'agencyId': agency['id'], 'providerKey': 'simulation-ais', 'nativeEventId': 'RUNTIME-AIS-24',
    'mmsi': '367240024', 'name': 'RUNTIME VESSEL', 'latitude': 40.721, 'longitude': -74.021,
    'speedKnots': 6.5, 'courseDegrees': 182.0, 'sourceTimestamp': gateway_time
})
_, route = call('/api/integrations/routing/route', 'POST', {
    'agencyId': agency['id'], 'fromLatitude': 40.721, 'fromLongitude': -74.021,
    'toLatitude': 40.731, 'toLongitude': -74.011, 'unitId': None, 'incidentId': incident_id,
    'avoidRestrictions': True, 'vehicleProfile': 'Emergency', 'providerKey': 'simulation-routing'
})
assert route['operationId'] and route['providerKey'] == 'simulation-routing', route
_, recordings = call('/api/integrations/recordings/query', 'POST', {
    'agencyId': agency['id'], 'incidentId': incident_id, 'channel': 'Dispatch 1', 'telephoneNumber': None,
    'fromUtc': gateway_time, 'toUtc': datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace('+00:00','Z'),
    'providerKey': 'simulation-recording'
})
assert recordings and recordings[0]['providerReference'], recordings
_, discovered = call('/api/integrations/cameras/discover', 'POST', {
    'agencyId': agency['id'], 'stationId': station['id'], 'providerKey': 'simulation-onvif'
})
assert discovered, discovered
_, imported_camera = call('/api/integrations/cameras/import', 'POST', {
    'agencyId': agency['id'], 'stationId': station['id'], 'premiseId': None, 'candidate': discovered[0]
})
_, radio_clear = call('/api/integrations/radio/clear-emergency', 'POST', {
    'agencyId': agency['id'], 'incidentId': incident_id, 'unitId': unit['id'], 'nativeRadioId': 'RUNTIME-RADIO-24',
    'providerKey': 'simulation-radio', 'idempotencyKey': 'runtime-radio-clear-24'
})
assert radio_clear['state'] == 'Confirmed' and radio_clear['providerReference'], radio_clear
_, integration_dashboard = call('/api/integrations/dashboard')
assert any(x['id'] == gateway_911['id'] for x in integration_dashboard['recentEvents']), integration_dashboard['recentEvents']
assert any(x['id'] == radio_clear['id'] for x in integration_dashboard['operations']), integration_dashboard['operations']
with open(integrationfile, 'w', encoding='utf-8') as f:
    json.dump({'agencyId': agency['id'], 'gateway911Id': gateway_911['id'], 'aisEventId': gateway_ais['id'],
               'routeOperationId': route['operationId'], 'radioOperationId': radio_clear['id'],
               'recordingReference': recordings[0]['providerReference'], 'cameraId': imported_camera['id'], 'mmsi': '367240024'}, f)

_, persistence = call('/api/persistence/flush', 'POST')
assert persistence['state'] == 'Healthy'
_, journal = call('/api/persistence/journal-status')
assert journal['provider'] == 'FileJournal'
assert journal['queued'] == journal['persisted'], journal
_, ready = call('/health/ready')
assert ready['status'] == 'ready' and ready['checks']['mutationJournalDrained'] and ready['checks']['authoritativeWriter'], ready

_, backup = call('/api/persistence/backups', 'POST', {
    'actor': 'runtime-smoke',
    'note': 'Automated runtime restore verification'
})
assert backup['verified'] is True and backup['stateSchemaVersion'] >= 1, backup
_, verified = call(f"/api/persistence/backups/{backup['fileName']}/verify")
assert verified['valid'] is True, verified

with open(idfile, 'w', encoding='utf-8') as f:
    f.write(incident_id)
with open(backupfile, 'w', encoding='utf-8') as f:
    f.write(backup['fileName'])
print(f"mutation/backup phase PASS — incident={incident_id}, backup={backup['fileName']}")
PY

[[ -s "$STATE" ]] || { echo "checkpoint file was not written" >&2; exit 1; }
[[ -s "$JOURNAL" ]] || { echo "mutation journal was not written" >&2; exit 1; }

stop_server
start_server "$LOG2"

python3 - "$BASE" "$IDFILE" "$BACKUPFILE" "$TRANSIENTFILE" "$REPLAYFILE" "$RMSFILE" "$NERISFILE" "$WORKFORCEFILE" "$DMSFILE" "$CHATFILE" "$MOBILEFILE" "$INTEGRATIONFILE" <<'PY'
import json, sys, urllib.request, urllib.error, urllib.parse
base, idfile, backupfile, transientfile, replayfile, rmsfile, nerisfile, workforcefile, dmsfile, chatfile, mobilefile, integrationfile = sys.argv[1:]
incident_id = open(idfile, encoding='utf-8').read().strip()
backup_name = open(backupfile, encoding='utf-8').read().strip()
replay_at = open(replayfile, encoding='utf-8').read().strip()
rms_id = open(rmsfile, encoding='utf-8').read().strip()
neris_id = open(nerisfile, encoding='utf-8').read().strip()
workforce = json.load(open(workforcefile, encoding='utf-8'))
dms = json.load(open(dmsfile, encoding='utf-8'))
chat = json.load(open(chatfile, encoding='utf-8'))
mobile = json.load(open(mobilefile, encoding='utf-8'))
integration = json.load(open(integrationfile, encoding='utf-8'))

def call(path, method="GET", body=None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(base + path, data=data, method=method,
                                 headers={"Content-Type":"application/json"} if data else {})
    with urllib.request.urlopen(req, timeout=10) as r:
        raw = r.read().decode()
        return r.status, json.loads(raw) if raw else None

_, ready = call('/health/ready')
assert ready['status'] == 'ready' and ready['checks']['mutationJournalDrained'] and ready['checks']['authoritativeWriter'], ready
_, incident = call(f'/api/incidents/{incident_id}')
assert incident['status'] == 'Closed' and incident['version'] == 3 and incident['closedAtUtc'], incident
_, activity = call(f'/api/incidents/{incident_id}/activity')
assert any(x.get('category') == 'NARRATIVE' and 'Runtime smoke narrative entry.' in x.get('message','') for x in activity), activity
_, historical = call(f'/api/replay/incidents/{incident_id}/snapshot?atUtc={urllib.parse.quote(replay_at)}')
assert historical['incident']['status'] == 'Dispatched', historical
assert historical['incident']['closedAtUtc'] is None, historical['incident']
assert any(x.get('category') == 'NARRATIVE' and 'Runtime smoke narrative entry.' in x.get('message','') for x in historical['activity']), historical['activity']
_, rms = call(f'/api/rms/incident-reports/{rms_id}')
assert rms['status'] == 'Finalized' and rms['incidentId'] == incident_id and rms['finalizedAtUtc'], rms
_, rms_versions = call(f'/api/rms/incident-reports/{rms_id}/versions')
assert len(rms_versions) >= 3, rms_versions
_, neris = call(f'/api/rms/neris-submissions/{neris_id}')
assert neris['state'] in ('Accepted', 'Confirmed') and neris['attemptCount'] == 2 and neris['providerReference'], neris
_, neris_attempts = call(f'/api/rms/neris-submissions/{neris_id}/attempts')
assert len(neris_attempts) == 2 and neris_attempts[0]['state'] == 'Failed' and neris_attempts[1]['state'] in ('Accepted', 'Confirmed'), neris_attempts
_, workforce_roster = call(f"/api/workforce/roster/current?agencyId={workforce['agencyId']}&date={workforce['date']}")
assert workforce_roster['staffing']['meetsMinimums'] is True and workforce_roster['postedRoster']['revision'] == 2, workforce_roster
assert any(x['state'] == 'Overtime' and x['employeeName'] == 'Runtime Overtime Employee' for x in workforce_roster['assignments']), workforce_roster['assignments']
assert any(x['kind'] == 'CallOut' for x in workforce_roster['changes']), workforce_roster['changes']
_, workforce_snapshots = call(f"/api/workforce/roster/snapshots?agencyId={workforce['agencyId']}&date={workforce['date']}")
by_id = {x['id']: x for x in workforce_snapshots}
assert workforce['snapshot1Id'] in by_id and workforce['snapshot2Id'] in by_id, workforce_snapshots
assert by_id[workforce['snapshot1Id']]['contentSha256'] == workforce['snapshot1Hash'], by_id[workforce['snapshot1Id']]
assert by_id[workforce['snapshot2Id']]['contentSha256'] == workforce['snapshot2Hash'], by_id[workforce['snapshot2Id']]
_, dms_revisions = call(f"/api/documents/dms/{dms['documentId']}/revisions")
dms_by_version = {x['version']: x for x in dms_revisions}
assert dms_by_version[1]['state'] == 'Superseded' and dms_by_version[1]['contentSha256'] == dms['revision1Hash'], dms_by_version
assert dms_by_version[2]['state'] == 'Published' and dms_by_version[2]['contentSha256'] == dms['revision2Hash'], dms_by_version
_, dms_receipts = call(f"/api/documents/dms/{dms['documentId']}/read-receipts")
assert any(x['id'] == dms['receiptId'] and x['state'] == 'Acknowledged' for x in dms_receipts), dms_receipts
_, dms_assets = call(f"/api/documents/dms/{dms['documentId']}/assets")
assert any(x['id'] == dms['assetId'] and x['contentSha256'] == 'a' * 64 for x in dms_assets), dms_assets
_, dms_links = call(f"/api/documents/dms/{dms['documentId']}/links")
assert any(x['id'] == dms['linkId'] and x['entityKey'] == incident_id for x in dms_links), dms_links
_, form_signatures = call(f"/api/forms/{dms['formId']}/signatures")
assert len(form_signatures) == 1 and form_signatures[0]['valuesSha256'] == dms['formSignatureHash'], form_signatures
_, chat_channels = call(f"/api/chat/channels?agencyId={chat['agencyId']}&includeArchived=true")
assert any(x['channel']['id'] == chat['channelId'] for x in chat_channels), chat_channels
_, chat_messages = call(f"/api/chat/channels/{chat['channelId']}/messages?limit=20")
assert any(x['id'] == chat['messageId'] and x['linkedIncidentId'] == chat['incidentId'] and len(x['attachments']) == 1 for x in chat_messages), chat_messages
_, mobile_sync = call('/api/mobile/sync', 'POST', {
    'agencyId': mobile['agencyId'], 'deviceId': mobile['deviceId'], 'sinceUtc': None,
    'queuedOperations': [], 'unitId': mobile['unitId'], 'offlineAuthorizationLease': None
})
assert mobile_sync['operationalPackage']['currentIncident']['id'] == mobile['incidentId'], mobile_sync['operationalPackage']
assert mobile_sync['operationalPackage']['selectedUnit']['status'] == mobile['targetStatus'], mobile_sync['operationalPackage']['selectedUnit']
_, mobile_activity = call(f"/api/incidents/{mobile['incidentId']}/activity")
assert any('Runtime mobile offline narrative' in x.get('message','') for x in mobile_activity), mobile_activity
_, mobile_chat_history = call(f"/api/chat/channels/{mobile['chatChannelId']}/messages?limit=50")
assert any(x.get('body') == 'Runtime mobile offline chat' for x in mobile_chat_history), mobile_chat_history
_, integration_dashboard = call('/api/integrations/dashboard')
assert any(x['id'] == integration['gateway911Id'] for x in integration_dashboard['recentEvents']), integration_dashboard['recentEvents']
assert any(x['id'] == integration['routeOperationId'] and x['state'] == 'Confirmed' for x in integration_dashboard['operations']), integration_dashboard['operations']
assert any(x['id'] == integration['radioOperationId'] and x['state'] == 'Confirmed' for x in integration_dashboard['operations']), integration_dashboard['operations']
assert any(x['providerReference'] == integration['recordingReference'] for x in integration_dashboard['recordings']), integration_dashboard['recordings']
assert any(x['id'] == integration['cameraId'] for x in integration_dashboard['cameras']), integration_dashboard['cameras']
assert any(x['mmsi'] == integration['mmsi'] for x in integration_dashboard['vessels']), integration_dashboard['vessels']

# Pass 10 regression: retries after a real server restart must replay the durable receipt
# rather than duplicate the original authoritative mutation or journal/outbox work.
_, before_retry_journal = call('/api/persistence/journal-status')
_, before_retry_incidents = call('/api/incidents')
_, agencies = call('/api/agencies')
_, stations = call('/api/stations')
status, replayed_create = call('/api/incidents', 'POST', {
    'agencyId': agencies[0]['id'],
    'stationId': stations[0]['id'],
    'nature': 'Runtime Persistence Smoke Test',
    'priority': 'Emergency',
    'address': '100 Runtime Test Way',
    'crossStreets': 'Compile Ave / Restore Blvd',
    'municipality': 'DISRoute Test',
    'callerName': 'Runtime Harness',
    'callerPhone': '555-0109',
    'notes': 'Created by scripts/runtime-smoke.sh',
    'idempotencyKey': 'runtime-smoke-create'
})
assert status == 201 and replayed_create['id'] == incident_id and replayed_create['version'] == 1, replayed_create
_, replayed_close = call(f'/api/incidents/{incident_id}/close', 'POST', {
    'expectedVersion': 2,
    'reason': 'Runtime smoke test complete',
    'idempotencyKey': 'runtime-smoke-close'
})
assert replayed_close['id'] == incident_id and replayed_close['version'] == 3 and replayed_close['status'] == 'Closed', replayed_close
_, after_retry_incidents = call('/api/incidents')
_, after_retry_journal = call('/api/persistence/journal-status')
assert len(after_retry_incidents) == len(before_retry_incidents), (len(before_retry_incidents), len(after_retry_incidents))
assert after_retry_journal['queued'] == before_retry_journal['queued'], (before_retry_journal, after_retry_journal)

status, transient = call('/api/incidents', 'POST', {
    'agencyId': agencies[0]['id'],
    'stationId': stations[0]['id'],
    'nature': 'Transient Restore Test',
    'priority': 'Routine',
    'address': '999 Must Disappear Road',
    'crossStreets': '',
    'municipality': 'DISRoute Test',
    'callerName': 'Restore Harness',
    'callerPhone': '555-0110',
    'notes': 'This incident must disappear after staged restore.',
    'idempotencyKey': 'runtime-smoke-transient'
})
assert status == 201, transient
with open(transientfile, 'w', encoding='utf-8') as f:
    f.write(transient['id'])

_, staged = call(f'/api/persistence/backups/{backup_name}/stage-restore', 'POST', {
    'actor': 'runtime-smoke',
    'confirmation': 'RESTORE ON RESTART'
})
assert staged['staged'] is True and staged['restartRequired'] is True, staged
print(f"restart/stage phase PASS — original={incident_id}, transient={transient['id']}, backup={backup_name}")
PY

stop_server
start_server "$LOG3"

python3 - "$BASE" "$IDFILE" "$TRANSIENTFILE" "$REPLAYFILE" "$RMSFILE" "$NERISFILE" "$WORKFORCEFILE" "$DMSFILE" "$CHATFILE" "$MOBILEFILE" "$INTEGRATIONFILE" <<'PY'
import json, sys, urllib.request, urllib.error, urllib.parse
base, idfile, transientfile, replayfile, rmsfile, nerisfile, workforcefile, dmsfile, chatfile, mobilefile, integrationfile = sys.argv[1:]
incident_id = open(idfile, encoding='utf-8').read().strip()
transient_id = open(transientfile, encoding='utf-8').read().strip()
replay_at = open(replayfile, encoding='utf-8').read().strip()
rms_id = open(rmsfile, encoding='utf-8').read().strip()
neris_id = open(nerisfile, encoding='utf-8').read().strip()
workforce = json.load(open(workforcefile, encoding='utf-8'))
dms = json.load(open(dmsfile, encoding='utf-8'))
chat = json.load(open(chatfile, encoding='utf-8'))
mobile = json.load(open(mobilefile, encoding='utf-8'))
integration = json.load(open(integrationfile, encoding='utf-8'))
with urllib.request.urlopen(base + '/health/ready', timeout=10) as r:
    ready = json.loads(r.read().decode())
assert ready['status'] == 'ready' and ready['checks']['mutationJournalDrained'] and ready['checks']['authoritativeWriter'], ready
with urllib.request.urlopen(base + f'/api/incidents/{incident_id}', timeout=10) as r:
    incident = json.loads(r.read().decode())
assert incident['status'] == 'Closed' and incident['version'] == 3, incident
with urllib.request.urlopen(base + f'/api/replay/incidents/{incident_id}/snapshot?atUtc={urllib.parse.quote(replay_at)}', timeout=10) as r:
    historical = json.loads(r.read().decode())
assert historical['incident']['status'] == 'Dispatched', historical
assert historical['incident']['closedAtUtc'] is None, historical['incident']
with urllib.request.urlopen(base + f'/api/rms/incident-reports/{rms_id}', timeout=10) as r:
    rms = json.loads(r.read().decode())
assert rms['status'] == 'Finalized' and rms['incidentId'] == incident_id, rms
with urllib.request.urlopen(base + f'/api/rms/neris-submissions/{neris_id}', timeout=10) as r:
    neris = json.loads(r.read().decode())
assert neris['state'] in ('Accepted', 'Confirmed') and neris['attemptCount'] == 2, neris
with urllib.request.urlopen(base + f'/api/rms/neris-submissions/{neris_id}/attempts', timeout=10) as r:
    neris_attempts = json.loads(r.read().decode())
assert len(neris_attempts) == 2, neris_attempts
with urllib.request.urlopen(base + f"/api/workforce/roster/current?agencyId={workforce['agencyId']}&date={workforce['date']}", timeout=10) as r:
    workforce_roster = json.loads(r.read().decode())
assert workforce_roster['staffing']['meetsMinimums'] is True and workforce_roster['postedRoster']['revision'] == 2, workforce_roster
assert any(x['state'] == 'Overtime' and x['employeeName'] == 'Runtime Overtime Employee' for x in workforce_roster['assignments']), workforce_roster['assignments']
assert any(x['kind'] == 'CallOut' for x in workforce_roster['changes']), workforce_roster['changes']
with urllib.request.urlopen(base + f"/api/workforce/roster/snapshots?agencyId={workforce['agencyId']}&date={workforce['date']}", timeout=10) as r:
    workforce_snapshots = json.loads(r.read().decode())
by_id = {x['id']: x for x in workforce_snapshots}
assert workforce['snapshot1Id'] in by_id and workforce['snapshot2Id'] in by_id, workforce_snapshots
assert by_id[workforce['snapshot1Id']]['contentSha256'] == workforce['snapshot1Hash'], by_id[workforce['snapshot1Id']]
assert by_id[workforce['snapshot2Id']]['contentSha256'] == workforce['snapshot2Hash'], by_id[workforce['snapshot2Id']]
with urllib.request.urlopen(base + f"/api/documents/dms/{dms['documentId']}/revisions", timeout=10) as r:
    dms_revisions = json.loads(r.read().decode())
dms_by_version = {x['version']: x for x in dms_revisions}
assert dms_by_version[1]['state'] == 'Superseded' and dms_by_version[1]['contentSha256'] == dms['revision1Hash'], dms_by_version
assert dms_by_version[2]['state'] == 'Published' and dms_by_version[2]['contentSha256'] == dms['revision2Hash'], dms_by_version
with urllib.request.urlopen(base + f"/api/documents/dms/{dms['documentId']}/read-receipts", timeout=10) as r:
    dms_receipts = json.loads(r.read().decode())
assert any(x['id'] == dms['receiptId'] and x['state'] == 'Acknowledged' for x in dms_receipts), dms_receipts
with urllib.request.urlopen(base + f"/api/documents/dms/{dms['documentId']}/assets", timeout=10) as r:
    dms_assets = json.loads(r.read().decode())
assert any(x['id'] == dms['assetId'] and x['contentSha256'] == 'a' * 64 for x in dms_assets), dms_assets
with urllib.request.urlopen(base + f"/api/documents/dms/{dms['documentId']}/links", timeout=10) as r:
    dms_links = json.loads(r.read().decode())
assert any(x['id'] == dms['linkId'] and x['entityKey'] == incident_id for x in dms_links), dms_links
with urllib.request.urlopen(base + f"/api/forms/{dms['formId']}/signatures", timeout=10) as r:
    form_signatures = json.loads(r.read().decode())
assert len(form_signatures) == 1 and form_signatures[0]['valuesSha256'] == dms['formSignatureHash'], form_signatures
with urllib.request.urlopen(base + f"/api/chat/channels?agencyId={chat['agencyId']}&includeArchived=true", timeout=10) as r:
    chat_channels = json.loads(r.read().decode())
assert any(x['channel']['id'] == chat['channelId'] for x in chat_channels), chat_channels
with urllib.request.urlopen(base + f"/api/chat/channels/{chat['channelId']}/messages?limit=20", timeout=10) as r:
    chat_messages = json.loads(r.read().decode())
assert any(x['id'] == chat['messageId'] and x['linkedIncidentId'] == chat['incidentId'] and len(x['attachments']) == 1 for x in chat_messages), chat_messages
mobile_body = json.dumps({'agencyId': mobile['agencyId'], 'deviceId': mobile['deviceId'], 'sinceUtc': None,
                          'queuedOperations': [], 'unitId': mobile['unitId'], 'offlineAuthorizationLease': None}).encode()
req = urllib.request.Request(base + '/api/mobile/sync', data=mobile_body, method='POST', headers={'Content-Type':'application/json'})
with urllib.request.urlopen(req, timeout=10) as r:
    mobile_sync = json.loads(r.read().decode())
assert mobile_sync['operationalPackage']['currentIncident']['id'] == mobile['incidentId'], mobile_sync['operationalPackage']
assert mobile_sync['operationalPackage']['selectedUnit']['status'] == mobile['targetStatus'], mobile_sync['operationalPackage']['selectedUnit']
with urllib.request.urlopen(base + f"/api/incidents/{mobile['incidentId']}/activity", timeout=10) as r:
    mobile_activity = json.loads(r.read().decode())
assert any('Runtime mobile offline narrative' in x.get('message','') for x in mobile_activity), mobile_activity
with urllib.request.urlopen(base + '/api/integrations/dashboard', timeout=10) as r:
    integration_dashboard = json.loads(r.read().decode())
assert any(x['id'] == integration['gateway911Id'] for x in integration_dashboard['recentEvents']), integration_dashboard['recentEvents']
assert any(x['id'] == integration['radioOperationId'] and x['state'] == 'Confirmed' for x in integration_dashboard['operations']), integration_dashboard['operations']
assert any(x['providerReference'] == integration['recordingReference'] for x in integration_dashboard['recordings']), integration_dashboard['recordings']
assert any(x['id'] == integration['cameraId'] for x in integration_dashboard['cameras']), integration_dashboard['cameras']
assert any(x['mmsi'] == integration['mmsi'] for x in integration_dashboard['vessels']), integration_dashboard['vessels']
try:
    urllib.request.urlopen(base + f'/api/incidents/{transient_id}', timeout=10)
    raise AssertionError('transient incident survived restore unexpectedly')
except urllib.error.HTTPError as e:
    assert e.code == 404, e.code
print(f"restore-on-restart phase PASS — restored={incident_id}, removed-transient={transient_id}")
PY

compgen -G "$STATE.pre-restore-*.bak" >/dev/null || { echo "pre-restore rollback checkpoint was not created" >&2; exit 1; }
printf 'DISRoute runtime smoke PASS — checkpoint=%s journal=%s backup/restore=verified\n' "$STATE" "$JOURNAL"
