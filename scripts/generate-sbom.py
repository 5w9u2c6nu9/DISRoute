#!/usr/bin/env python3
import argparse,json,pathlib,xml.etree.ElementTree as ET,hashlib,datetime,os
p=argparse.ArgumentParser(description='Generate deterministic CycloneDX SBOM from DISRoute NuGet lock files.')
p.add_argument('root', nargs='?', default='.'); p.add_argument('--output', required=True)
p.add_argument('--version', default='1.0.0-rc.1'); p.add_argument('--source-date-epoch', type=int, default=int(os.environ.get('SOURCE_DATE_EPOCH','0') or 0))
a=p.parse_args(); root=pathlib.Path(a.root).resolve()
if a.source_date_epoch<=0: raise SystemExit('SOURCE_DATE_EPOCH or --source-date-epoch is required')
locks=sorted(root.glob('src/*/packages.lock.json'))
if not locks: raise SystemExit('no packages.lock.json files found; restore with lock-file generation first')
packages={}
for lock in locks:
    data=json.loads(lock.read_text())
    for tfm,deps in data.get('dependencies',{}).items():
        for name,meta in deps.items():
            ver=meta.get('resolved')
            if not ver: continue
            packages[(name.lower(),ver)]={'name':name,'version':ver}
components=[]
for (_,ver),pkg in sorted(packages.items()):
    name=pkg['name']; comp={'type':'library','name':name,'version':ver,'bom-ref':f'pkg:nuget/{name}@{ver}','purl':f'pkg:nuget/{name}@{ver}'}
    nupkg=None
    for candidate in [pathlib.Path.home()/'.nuget/packages'/name.lower()/ver/f'{name.lower()}.{ver}.nupkg', pathlib.Path('/mnt/data/disroute-toolchain/feed')/f'{name.lower()}.{ver}.nupkg']:
        if candidate.exists(): nupkg=candidate; break
    if nupkg:
        comp['hashes']=[{'alg':'SHA-256','content':hashlib.sha256(nupkg.read_bytes()).hexdigest()}]
    nuspec=pathlib.Path.home()/'.nuget/packages'/name.lower()/ver/f'{name.lower()}.nuspec'
    if nuspec.exists():
        try:
            x=ET.parse(nuspec).getroot(); lic=x.find('.//{*}license')
            if lic is not None and (lic.text or '').strip(): comp['licenses']=[{'license':{'id':lic.text.strip()}}]
        except Exception: pass
    components.append(comp)
ts=datetime.datetime.fromtimestamp(a.source_date_epoch,datetime.timezone.utc).replace(microsecond=0).isoformat().replace('+00:00','Z')
bom={'bomFormat':'CycloneDX','specVersion':'1.6','serialNumber':f'urn:uuid:00000000-0000-4000-8000-{hashlib.sha256((a.version+str(a.source_date_epoch)).encode()).hexdigest()[:12]}','version':1,'metadata':{'timestamp':ts,'component':{'type':'application','name':'DISRoute','version':a.version}},'components':components}
out=pathlib.Path(a.output); out.parent.mkdir(parents=True,exist_ok=True); out.write_text(json.dumps(bom,indent=2,sort_keys=True)+'\n')
print(json.dumps({'output':str(out),'components':len(components),'lockFiles':len(locks)},indent=2))
