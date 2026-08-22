#!/usr/bin/env python3
"""DISRoute Pass 29 destructive local stress/resilience/security qualification.

Runs against an isolated Development/FileCheckpoint server. This is a repeatable qualification
harness, not a production capacity benchmark. It deliberately exercises concurrency, duplicate
replay, optimistic conflicts, SIGKILL recovery, malformed/oversize input, rate limiting and
resource growth while preserving deterministic pass/fail evidence.
"""
from __future__ import annotations

import argparse
import concurrent.futures
import json
import os
from pathlib import Path
import signal
import socket
import statistics
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone


def utcnow(): return datetime.now(timezone.utc).isoformat().replace('+00:00', 'Z')

def percentile(values, p):
    if not values: return 0.0
    data = sorted(values)
    k = (len(data)-1) * p
    lo = int(k); hi = min(lo+1, len(data)-1); frac = k-lo
    return data[lo]*(1-frac)+data[hi]*frac

def free_port():
    s=socket.socket(); s.bind(('127.0.0.1',0)); port=s.getsockname()[1]; s.close(); return port

class Server:
    def __init__(self, root: Path, dotnet: Path, work: Path, rate=100000, max_body=1048576):
        self.root=root; self.dotnet=dotnet; self.work=work; self.port=free_port(); self.base=f'http://127.0.0.1:{self.port}'; self.proc=None
        self.rate=rate; self.max_body=max_body; self.generation=0
    def start(self):
        self.generation += 1
        log=self.work/f'server-{self.generation}.log'
        env=os.environ.copy()
        env.update({
            'ASPNETCORE_ENVIRONMENT':'Development','DOTNET_ENVIRONMENT':'Development','ASPNETCORE_URLS':self.base,
            'Persistence__Provider':'FileCheckpoint','Persistence__CheckpointPath':str(self.work/'state.json'),
            'Persistence__JournalPath':str(self.work/'journal.ndjson'),'Persistence__CheckpointSeconds':'1',
            'Security__RequireAuthentication':'false','Security__RequireHttps':'false','Security__RequireMfa':'false',
            'Security__RequireMutationHeader':'false','Security__RateLimit__PermitLimitPerMinute':str(self.rate),
            'Security__RateLimit__QueueLimit':'0','Security__MaxRequestBodyBytes':str(self.max_body),
        })
        dll=self.root/'src/DISRoute.Server/bin/Release/net10.0/DISRoute.Server.dll'
        if not dll.exists(): raise RuntimeError(f'Release server build not found: {dll}')
        fh=open(log,'wb')
        self.proc=subprocess.Popen([str(self.dotnet),str(dll),'--urls',self.base], cwd=self.root/'src/DISRoute.Server', env=env, stdout=fh, stderr=subprocess.STDOUT)
        self._log_handle=fh; self._log=log
        deadline=time.time()+25
        while time.time()<deadline:
            if self.proc.poll() is not None:
                fh.flush(); raise RuntimeError(f'Server exited early ({self.proc.returncode}):\n{log.read_text(errors="replace")[-8000:]}')
            try:
                status,_,_,_=request(self.base,'/health/ready', timeout=1)
                if status==200: return
            except Exception: pass
            time.sleep(.1)
        raise RuntimeError(f'Server not ready:\n{log.read_text(errors="replace")[-8000:]}')
    def stop(self, hard=False):
        if not self.proc: return
        if self.proc.poll() is None:
            if hard: self.proc.kill()
            else: self.proc.send_signal(signal.SIGTERM)
            try: self.proc.wait(timeout=10)
            except subprocess.TimeoutExpired: self.proc.kill(); self.proc.wait(timeout=5)
        self._log_handle.close(); self.proc=None
    def rss_kb(self):
        if not self.proc or self.proc.poll() is not None: return 0
        try:
            txt=Path(f'/proc/{self.proc.pid}/status').read_text()
            return int(next(x.split()[1] for x in txt.splitlines() if x.startswith('VmRSS:')))
        except Exception: return 0
    def fd_count(self):
        if not self.proc or self.proc.poll() is not None: return 0
        try: return len(list(Path(f'/proc/{self.proc.pid}/fd').iterdir()))
        except Exception: return 0


def request(base, path, method='GET', body=None, raw=None, headers=None, timeout=15):
    data=raw
    hdr={'X-DISRoute-Request':'1'}
    if body is not None:
        data=json.dumps(body,separators=(',',':')).encode(); hdr['Content-Type']='application/json'
    elif raw is not None:
        hdr['Content-Type']='application/json'
    if headers: hdr.update(headers)
    req=urllib.request.Request(base+path,data=data,method=method,headers=hdr)
    started=time.perf_counter()
    try:
        with urllib.request.urlopen(req,timeout=timeout) as r:
            b=r.read(); elapsed=(time.perf_counter()-started)*1000
            try: payload=json.loads(b) if b else None
            except Exception: payload=b.decode(errors='replace')
            return r.status,payload,elapsed,dict(r.headers)
    except urllib.error.HTTPError as e:
        b=e.read(); elapsed=(time.perf_counter()-started)*1000
        try: payload=json.loads(b) if b else None
        except Exception: payload=b.decode(errors='replace')
        return e.code,payload,elapsed,dict(e.headers)


def require(cond,msg):
    if not cond: raise AssertionError(msg)


def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--root',default=str(Path(__file__).resolve().parents[1])); ap.add_argument('--dotnet',required=True); ap.add_argument('--evidence',required=True); ap.add_argument('--keep-work',action='store_true')
    args=ap.parse_args(); root=Path(args.root).resolve(); dotnet=Path(args.dotnet).resolve(); evidence=Path(args.evidence).resolve(); evidence.parent.mkdir(parents=True,exist_ok=True)
    work=Path(tempfile.mkdtemp(prefix='disroute-pass29-'))
    out={'pass':29,'startedAtUtc':utcnow(),'qualificationEnvironment':{'kind':'isolated Development/FileCheckpoint stress lab','productionCapacityBenchmark':False},'phases':[]}
    server=None
    try:
        # Phase 1: sustained mixed concurrency and idempotency/conflict semantics.
        server=Server(root,dotnet,work,rate=100000,max_body=1024*1024); server.start()
        st,agencies,_,_=request(server.base,'/api/agencies'); st2,stations,_,_=request(server.base,'/api/stations')
        require(st==200 and st2==200 and agencies and stations,'seed topology unavailable')
        agency=agencies[0]['id']; station=stations[0]['id']
        # warm up
        for _ in range(12): request(server.base,'/api/incidents?openOnly=true')
        rss0,fd0=server.rss_kb(),server.fd_count()
        create_lat=[]; errors=[]; ids=[]
        def create_one(i):
            status,payload,ms,_=request(server.base,'/api/incidents','POST',{
                'agencyId':agency,'stationId':station,'nature':'Pass29 Load','priority':'Routine','address':f'{29000+i} Stress Avenue',
                'crossStreets':'Load St / Resilience Rd','municipality':'DISRoute Test','callerName':f'Caller {i}','callerPhone':f'555-{2900+i:04d}',
                'notes':'Pass 29 concurrent qualification','idempotencyKey':f'pass29:load:{i}'})
            return status,payload,ms
        with concurrent.futures.ThreadPoolExecutor(max_workers=16) as ex:
            for status,payload,ms in ex.map(create_one,range(96)):
                create_lat.append(ms)
                if status!=201: errors.append({'status':status,'payload':payload})
                else: ids.append(payload['id'])
        require(not errors,f'concurrent create failures: {errors[:3]}')
        require(len(set(ids))==96,'unique concurrent creates did not produce 96 unique incidents')
        # Read fan-out while stores contain the new load.
        read_lat=[]
        def read_one(i): return request(server.base, f'/api/incidents/{ids[i%len(ids)]}' if i%2 else '/api/incidents?openOnly=true')[2]
        with concurrent.futures.ThreadPoolExecutor(max_workers=32) as ex: read_lat=list(ex.map(read_one,range(256)))
        # Concurrent same-key replay must collapse to one durable result.
        replay_body={'agencyId':agency,'stationId':station,'nature':'Replay Storm','priority':'Emergency','address':'29 Replay Plaza','crossStreets':'','municipality':'DISRoute Test','callerName':'Replay','callerPhone':'555-2929','notes':'same-key storm','idempotencyKey':'pass29:replay:one'}
        def replay(_): return request(server.base,'/api/incidents','POST',replay_body)
        with concurrent.futures.ThreadPoolExecutor(max_workers=32) as ex: replays=list(ex.map(replay,range(32)))
        require(all(x[0]==201 for x in replays),'same-key replay returned non-201')
        replay_ids={x[1]['id'] for x in replays}; require(len(replay_ids)==1,f'same-key replay created multiple incidents: {replay_ids}')
        # Exactly one competing optimistic update may win.
        _,target,_,_=request(server.base,f'/api/incidents/{ids[0]}')
        update_base={'nature':'Pass29 Contended Update','priority':target['priority'],'address':target['address'],'crossStreets':target['crossStreets'],'municipality':target['municipality'],'callerName':target['callerName'],'callerPhone':target['callerPhone'],'notes':'contention','expectedVersion':target['version']}
        def contend(i):
            b=dict(update_base); b['idempotencyKey']=f'pass29:contend:{i}'; return request(server.base,f"/api/incidents/{target['id']}",'PUT',b)
        with concurrent.futures.ThreadPoolExecutor(max_workers=16) as ex: contended=list(ex.map(contend,range(16)))
        codes=[x[0] for x in contended]
        require(codes.count(200)==1 and codes.count(409)==15,f'optimistic concurrency result mismatch: {codes}')
        rss1,fd1=server.rss_kb(),server.fd_count()
        require(rss1-rss0 < 262144,f'RSS grew more than 256 MiB ({rss0}->{rss1} KiB)')
        require(fd1-fd0 < 128,f'file descriptors grew excessively ({fd0}->{fd1})')
        _,journal,_,_=request(server.base,'/api/persistence/journal-status')
        require(journal['queued']==journal['persisted'] and journal['failed']==0 and journal['pendingAuthoritativeBatches']==0,f'journal not drained: {journal}')
        out['phases'].append({'name':'concurrent-load-and-command-semantics','status':'PASS','creates':96,'readRequests':256,'sameKeyReplays':32,'optimisticContenders':16,'createLatencyMs':{'p50':round(percentile(create_lat,.50),2),'p95':round(percentile(create_lat,.95),2),'p99':round(percentile(create_lat,.99),2),'max':round(max(create_lat),2)},'readLatencyMs':{'p50':round(percentile(read_lat,.50),2),'p95':round(percentile(read_lat,.95),2),'p99':round(percentile(read_lat,.99),2),'max':round(max(read_lat),2)},'rssKiB':{'before':rss0,'after':rss1,'delta':rss1-rss0},'fdCount':{'before':fd0,'after':fd1,'delta':fd1-fd0}})

        # Phase 2: successful acknowledgement survives immediate SIGKILL and reload.
        st,active,_,_=request(server.base,'/api/incidents','POST',{'agencyId':agency,'stationId':station,'nature':'Pass29 Crash Continuity','priority':'Emergency','address':'29 Durable Lane','crossStreets':'','municipality':'DISRoute Test','callerName':'Durability','callerPhone':'555-2999','notes':'must survive SIGKILL','idempotencyKey':'pass29:crash:create'})
        require(st==201,'crash-continuity incident create failed')
        st,narr,_,_=request(server.base,f"/api/incidents/{active['id']}/narrative",'POST',{'text':'Acknowledged before immediate SIGKILL.','idempotencyKey':'pass29:crash:narrative'})
        require(st==200,'pre-crash narrative failed')
        server.stop(hard=True); server.start()
        st,reloaded,_,_=request(server.base,f"/api/incidents/{active['id']}"); st2,activity,_,_=request(server.base,f"/api/incidents/{active['id']}/activity")
        require(st==200 and reloaded['id']==active['id'],'acknowledged incident lost after SIGKILL')
        require(st2==200 and any(x.get('text')=='Acknowledged before immediate SIGKILL.' or 'Acknowledged before immediate SIGKILL.' in json.dumps(x) for x in activity),'acknowledged narrative lost after SIGKILL')
        out['phases'].append({'name':'sigkill-after-ack-recovery','status':'PASS','incidentId':active['id'],'activityEntries':len(activity)})
        server.stop()

        # Phase 3: hostile/malformed input and body limits fail safely without killing health.
        server=Server(root,dotnet,work/'security-body',rate=100000,max_body=1024); server.work.mkdir(parents=True,exist_ok=True); server.start()
        bad_status,bad_payload,_,_=request(server.base,'/api/incidents','POST',raw=b'{"broken":')
        require(400 <= bad_status < 500,f'malformed JSON should be 4xx, got {bad_status}: {bad_payload}')
        huge={'agencyId':agency,'stationId':station,'nature':'Oversize','priority':'Routine','address':'29 Large Body','crossStreets':'','municipality':'Test','callerName':'x','callerPhone':'x','notes':'A'*4096,'idempotencyKey':'pass29:oversize'}
        over_status,_,_,_=request(server.base,'/api/incidents','POST',huge)
        require(over_status==413,f'oversize body should be 413, got {over_status}')
        longkey={'agencyId':agency,'stationId':station,'nature':'Long Key','priority':'Routine','address':'29 Key Road','crossStreets':'','municipality':'Test','callerName':'x','callerPhone':'x','notes':'x','idempotencyKey':'k'*300}
        # This request is below 1 KiB except for the key and should be rejected by command validation.
        lk_status,_,_,_=request(server.base,'/api/incidents','POST',longkey)
        require(lk_status in (400,413),f'overlong idempotency key should fail closed, got {lk_status}')
        h,_,_,_=request(server.base,'/health/ready'); require(h==200,'server unhealthy after hostile input')
        out['phases'].append({'name':'hostile-input-and-request-limits','status':'PASS','malformedStatus':bad_status,'oversizeStatus':over_status,'overlongKeyStatus':lk_status})
        server.stop()

        # Phase 4: rate limiter rejects API abuse with backoff metadata while health remains exempt.
        server=Server(root,dotnet,work/'rate',rate=5,max_body=1024*1024); server.work.mkdir(parents=True,exist_ok=True); server.start()
        statuses=[]; retry_headers=[]
        for _ in range(12):
            status,_,_,headers=request(server.base,'/api/incidents?openOnly=true'); statuses.append(status)
            if status==429: retry_headers.append(headers.get('Retry-After'))
        require(429 in statuses,f'rate limiter did not reject burst: {statuses}')
        require(retry_headers and all(x=='60' for x in retry_headers),f'429 responses missing Retry-After: {retry_headers}')
        health_codes=[request(server.base,'/health/ready')[0] for _ in range(8)]
        require(all(x==200 for x in health_codes),f'health probes were rate limited/degraded: {health_codes}')
        out['phases'].append({'name':'rate-limit-and-health-isolation','status':'PASS','apiStatuses':statuses,'healthStatuses':health_codes,'retryAfter':'60'})
        server.stop()

        out['status']='PASS'; out['completedAtUtc']=utcnow(); out['workDirectory']=str(work) if args.keep_work else None
        evidence.write_text(json.dumps(out,indent=2)+'\n')
        print('PASS: Pass 29 stress/resilience/security qualification')
        for p in out['phases']: print(f"  PASS: {p['name']}")
        return 0
    except Exception as exc:
        out['status']='FAIL'; out['completedAtUtc']=utcnow(); out['failure']=f'{type(exc).__name__}: {exc}'; out['workDirectory']=str(work)
        evidence.write_text(json.dumps(out,indent=2)+'\n')
        print(f'FAIL: {exc}',file=sys.stderr)
        return 1
    finally:
        if server:
            try: server.stop()
            except Exception: pass
        if not args.keep_work and out.get('status')=='PASS':
            import shutil; shutil.rmtree(work,ignore_errors=True)

if __name__=='__main__': raise SystemExit(main())
