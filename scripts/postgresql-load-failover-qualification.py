#!/usr/bin/env python3
"""Pass 29 shared-primary PostgreSQL load + application failover qualification."""
from __future__ import annotations
import argparse, concurrent.futures, json, os, pathlib, signal, socket, subprocess, sys, tempfile, threading, time, urllib.error, urllib.request
from datetime import datetime, timezone

def utc(): return datetime.now(timezone.utc).isoformat().replace('+00:00','Z')
def free_port():
    s=socket.socket(); s.bind(('127.0.0.1',0)); p=s.getsockname()[1]; s.close(); return p

def http(base,path,method='GET',body=None,timeout=8):
    data=None if body is None else json.dumps(body,separators=(',',':')).encode()
    h={'X-DISRoute-Request':'1'}
    if data is not None:h['Content-Type']='application/json'
    req=urllib.request.Request(base+path,data=data,method=method,headers=h)
    try:
        with urllib.request.urlopen(req,timeout=timeout) as r:
            raw=r.read(); return r.status,json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        raw=e.read()
        try:p=json.loads(raw) if raw else None
        except Exception:p=raw.decode(errors='replace')
        return e.code,p

def ready(base):
    try:return http(base,'/health/ready',timeout=.7)[0]==200
    except Exception:return False

def require(c,m):
    if not c: raise AssertionError(m)

class Node:
    def __init__(self,name,port,root,dotnet,cs,work): self.name=name;self.port=port;self.base=f'http://127.0.0.1:{port}';self.root=root;self.dotnet=dotnet;self.cs=cs;self.work=work;self.proc=None
    def start(self):
        log=self.work/f'{self.name}.log'; f=open(log,'wb'); self.log_handle=f; self.log=log
        env=os.environ.copy();env.update({'ASPNETCORE_ENVIRONMENT':'Development','DOTNET_ENVIRONMENT':'Development','ASPNETCORE_URLS':self.base,'Persistence__Provider':'PostgreSQL','ConnectionStrings__DISRoute':self.cs,'Persistence__CheckpointSeconds':'1','HighAvailability__Enabled':'true','HighAvailability__NodeId':self.name,'HighAvailability__Site':'Dispatch','HighAvailability__ConfiguredRole':'LocalStandby','HighAvailability__HeartbeatSeconds':'1','HighAvailability__StaleNodeSeconds':'4','Security__RequireAuthentication':'false','Security__RequireHttps':'false','Security__RequireMfa':'false','Security__RequireMutationHeader':'false','Security__RateLimit__PermitLimitPerMinute':'100000'})
        dll=self.root/'src/DISRoute.Server/bin/Release/net10.0/DISRoute.Server.dll'; self.proc=subprocess.Popen([str(self.dotnet),str(dll),'--urls',self.base],cwd=self.root/'src/DISRoute.Server',env=env,stdout=f,stderr=subprocess.STDOUT)
    def stop(self,hard=False):
        if self.proc and self.proc.poll() is None:
            self.proc.kill() if hard else self.proc.send_signal(signal.SIGTERM)
            try:self.proc.wait(timeout=8)
            except subprocess.TimeoutExpired:self.proc.kill();self.proc.wait(timeout=3)
        if getattr(self,'log_handle',None): self.log_handle.close()

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--root',required=True);ap.add_argument('--dotnet',required=True);ap.add_argument('--connection-string',required=True);ap.add_argument('--evidence',required=True);a=ap.parse_args()
    root=pathlib.Path(a.root).resolve(); dotnet=pathlib.Path(a.dotnet).resolve(); evidence=pathlib.Path(a.evidence).resolve(); evidence.parent.mkdir(parents=True,exist_ok=True); work=pathlib.Path(tempfile.mkdtemp(prefix='disroute-p29-pgload-'))
    nodes=[Node('p29-a',free_port(),root,dotnet,a.connection_string,work),Node('p29-b',free_port(),root,dotnet,a.connection_string,work)]
    out={'pass':29,'startedAtUtc':utc(),'kind':'shared-primary PostgreSQL active-load application failover','productionCapacityBenchmark':False}
    try:
        for n in nodes:n.start()
        deadline=time.time()+35; active=None
        while time.time()<deadline:
            rs=[ready(n.base) for n in nodes]
            if sum(rs)==1: active=nodes[rs.index(True)];break
            if any(n.proc.poll() is not None for n in nodes): raise RuntimeError('node exited during startup')
            time.sleep(.1)
        require(active is not None,'exactly one initial writer not established'); standby=nodes[1] if active is nodes[0] else nodes[0]
        _,ags=http(active.base,'/api/agencies');_,sts=http(active.base,'/api/stations'); agency=ags[0]['id'];station=sts[0]['id']
        total=80; results={}; lock=threading.Lock(); killed={'at':None}; promoted={'at':None}
        def choose_ready():
            for n in nodes:
                if n.proc.poll() is None and ready(n.base): return n
            return None
        def submit(i):
            body={'agencyId':agency,'stationId':station,'nature':'Pass29 PostgreSQL Load','priority':'Routine','address':f'{39000+i} Failover Load Way','crossStreets':'','municipality':'Qualification','callerName':f'PGLoad{i}','callerPhone':f'555-{3900+i:04d}','notes':'retry-safe active-load failover','idempotencyKey':f'pass29:pgload:{i}'}
            attempts=0; last=None; deadline=time.time()+20
            while time.time()<deadline:
                attempts+=1; n=choose_ready()
                if n is None: time.sleep(.08);continue
                try:
                    code,p=http(n.base,'/api/incidents','POST',body,timeout=4); last=(code,p)
                    if code==201:return i,p['id'],attempts
                    if code in (429,503):time.sleep(.05);continue
                    raise RuntimeError(f'command {i} unexpected HTTP {code}: {p}')
                except (OSError,TimeoutError,urllib.error.URLError): time.sleep(.05)
            raise RuntimeError(f'command {i} failed before failover retry deadline; attempts={attempts}; last={last}')
        def killer():
            time.sleep(.45); killed['at']=time.perf_counter(); active.stop(hard=True)
            deadline=time.time()+20
            while time.time()<deadline:
                if ready(standby.base): promoted['at']=time.perf_counter(); return
                time.sleep(.05)
        t=threading.Thread(target=killer,daemon=True);t.start()
        with concurrent.futures.ThreadPoolExecutor(max_workers=16) as ex:
            for i,inc,attempts in ex.map(submit,range(total)):
                with lock:results[i]=(inc,attempts)
        t.join(timeout=22)
        require(len(results)==total,'not all commands completed')
        require(promoted['at'] is not None and ready(standby.base),'standby did not become authoritative')
        failover_ms=round((promoted['at']-killed['at'])*1000,1) if killed['at'] else None
        code,incs=http(standby.base,'/api/incidents?query=Pass29%20PostgreSQL%20Load');require(code==200,'incident query failed')
        matched=[x for x in incs if x.get('nature')=='Pass29 PostgreSQL Load']
        require(len(matched)==total,f'expected {total} durable load incidents, found {len(matched)}')
        addresses=[x['address'] for x in matched];require(len(set(addresses))==total,'duplicate durable commands detected after failover retries')
        _,journal=http(standby.base,'/api/persistence/journal-status');require(journal['queued']==journal['persisted'] and journal['failed']==0 and journal['pendingAuthoritativeBatches']==0,f'journal not drained: {journal}')
        _,audit=http(standby.base,'/api/audit/verify');require(audit['valid'] is True,f'audit integrity failed: {audit}')
        retries=[v[1] for v in results.values()]
        out.update({'status':'PASS','completedAtUtc':utc(),'initialWriter':active.name,'survivorWriter':standby.name,'commands':total,'commandsRetried':sum(1 for x in retries if x>1),'maxAttempts':max(retries),'measuredFailoverMilliseconds':failover_ms,'measurementsAreProductionSla':False,'journalDrained':True,'auditValid':True})
        evidence.write_text(json.dumps(out,indent=2)+'\n');print(json.dumps(out,indent=2));return 0
    except Exception as e:
        out.update({'status':'FAIL','completedAtUtc':utc(),'failure':f'{type(e).__name__}: {e}','workDirectory':str(work)});evidence.write_text(json.dumps(out,indent=2)+'\n');print(f'FAIL: {e}',file=sys.stderr);return 1
    finally:
        for n in nodes:
            try:n.stop()
            except Exception:pass
if __name__=='__main__': raise SystemExit(main())
