#!/usr/bin/env python3
import argparse,hashlib,json,pathlib,zipfile,datetime,os
p=argparse.ArgumentParser(description='Create a deterministic DISRoute deployment ZIP and integrity manifest from scripts/publish output.')
p.add_argument('publish_dir'); p.add_argument('output_zip'); p.add_argument('--manifest')
p.add_argument('--release-version', default=os.environ.get('DISROUTE_RELEASE_VERSION','1.0.0-rc.1'))
p.add_argument('--source-commit', default=os.environ.get('DISROUTE_SOURCE_COMMIT','unknown'))
p.add_argument('--source-tree', default=os.environ.get('DISROUTE_SOURCE_TREE','unknown'))
p.add_argument('--source-date-epoch', type=int, default=int(os.environ.get('SOURCE_DATE_EPOCH','0') or 0))
a=p.parse_args(); root=pathlib.Path(a.publish_dir).resolve(); out=pathlib.Path(a.output_zip).resolve()
required=['server-linux-x64','server-win-x64','cad-win-x64','station-win-x64','operations-center-win-x64','mobile-field-harness-win-x64','station-display-linux-arm64','legacy-bridge-win-x64','deploy']
missing=[x for x in required if not (root/x).exists()]
if missing: raise SystemExit('missing publish targets: '+', '.join(missing))
if a.source_date_epoch <= 0: raise SystemExit('SOURCE_DATE_EPOCH or --source-date-epoch is required for reproducible release packaging')
release_time=datetime.datetime.fromtimestamp(a.source_date_epoch,datetime.timezone.utc).replace(microsecond=0)
# ZIP cannot represent dates before 1980 and uses 2-second DOS granularity.
zip_time=max(release_time,datetime.datetime(1980,1,1,tzinfo=datetime.timezone.utc))
zip_dt=(zip_time.year,zip_time.month,zip_time.day,zip_time.hour,zip_time.minute,zip_time.second-(zip_time.second%2))
files=[f for f in root.rglob('*') if f.is_file()]
rows=[]
for f in sorted(files,key=lambda x:x.relative_to(root).as_posix()):
    h=hashlib.sha256(); size=0
    with f.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024*1024), b''):
            h.update(chunk); size += len(chunk)
    rows.append({'path':f.relative_to(root).as_posix(),'size':size,'sha256':h.hexdigest()})
manifest={'manifestVersion':1,'product':'DISRoute','releaseVersion':a.release_version,'kind':'production-deployment','sourceCommit':a.source_commit,'sourceTree':a.source_tree,'sourceDateEpoch':a.source_date_epoch,'createdAtUtc':release_time.isoformat().replace('+00:00','Z'),'files':rows}
manifest_bytes=(json.dumps(manifest,indent=2,sort_keys=True)+'\n').encode(); manifest_path=pathlib.Path(a.manifest).resolve() if a.manifest else out.with_suffix('.manifest.json'); manifest_path.parent.mkdir(parents=True,exist_ok=True); manifest_path.write_bytes(manifest_bytes)
out.parent.mkdir(parents=True,exist_ok=True)
def add_bytes(z,name,data,mode=0o644):
    zi=zipfile.ZipInfo(name,zip_dt); zi.compress_type=zipfile.ZIP_DEFLATED; zi.external_attr=(mode & 0xffff)<<16; zi.create_system=3
    z.writestr(zi,data,compress_type=zipfile.ZIP_DEFLATED,compresslevel=1)
with zipfile.ZipFile(out,'w') as z:
    for f in sorted(files,key=lambda x:x.relative_to(root).as_posix()):
        name=f.relative_to(root).as_posix(); mode=0o755 if os.access(f,os.X_OK) else 0o644
        add_bytes(z,name,f.read_bytes(),mode)
    add_bytes(z,'deployment-manifest.json',manifest_bytes)
with zipfile.ZipFile(out) as z:
    bad=z.testzip()
    if bad: raise SystemExit(f'zip integrity failure: {bad}')
zh=hashlib.sha256(out.read_bytes()).hexdigest()
print(json.dumps({'zip':str(out),'sha256':zh,'files':len(files),'releaseVersion':a.release_version,'sourceDateEpoch':a.source_date_epoch},indent=2))
