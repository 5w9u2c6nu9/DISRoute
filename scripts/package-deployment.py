#!/usr/bin/env python3
import argparse,hashlib,json,pathlib,zipfile,datetime,sys
p=argparse.ArgumentParser(description='Create a DISRoute deployment ZIP and integrity manifest from scripts/publish output.')
p.add_argument('publish_dir'); p.add_argument('output_zip'); p.add_argument('--manifest')
a=p.parse_args(); root=pathlib.Path(a.publish_dir).resolve(); out=pathlib.Path(a.output_zip).resolve()
required=['server-linux-x64','server-win-x64','cad-win-x64','station-win-x64','operations-center-win-x64','mobile-field-harness-win-x64','station-display-linux-arm64','legacy-bridge-win-x64','deploy']
missing=[x for x in required if not (root/x).exists()]
if missing: raise SystemExit('missing publish targets: '+', '.join(missing))
files=[f for f in root.rglob('*') if f.is_file()]
rows=[]
for f in sorted(files,key=lambda x:x.relative_to(root).as_posix()):
    h=hashlib.sha256(); size=0
    with f.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024*1024), b''):
            h.update(chunk); size += len(chunk)
    rows.append({'path':f.relative_to(root).as_posix(),'size':size,'sha256':h.hexdigest()})
manifest={'product':'DISRoute','kind':'production-deployment','createdAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'files':rows}
manifest_bytes=(json.dumps(manifest,indent=2)+'\n').encode(); manifest_path=pathlib.Path(a.manifest).resolve() if a.manifest else out.with_suffix('.manifest.json'); manifest_path.write_bytes(manifest_bytes)
out.parent.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED,compresslevel=1) as z:
    for f in sorted(files,key=lambda x:x.relative_to(root).as_posix()): z.write(f,f.relative_to(root).as_posix())
    z.writestr('deployment-manifest.json',manifest_bytes)
with zipfile.ZipFile(out) as z:
    bad=z.testzip()
    if bad: raise SystemExit(f'zip integrity failure: {bad}')
zh=hashlib.sha256()
with out.open('rb') as stream:
    for chunk in iter(lambda: stream.read(1024*1024), b''):
        zh.update(chunk)
print(json.dumps({'zip':str(out),'sha256':zh.hexdigest(),'files':len(files)},indent=2))
