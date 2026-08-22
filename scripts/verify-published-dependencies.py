#!/usr/bin/env python3
import argparse,json,pathlib,sys
p=argparse.ArgumentParser(description='Verify published NuGet package versions are contained in the frozen DISRoute lock graph.')
p.add_argument('root'); p.add_argument('publish_dir'); a=p.parse_args(); root=pathlib.Path(a.root).resolve(); pub=pathlib.Path(a.publish_dir).resolve()
allowed=set()
for lock in root.glob('src/*/packages.lock.json'):
    data=json.loads(lock.read_text())
    for deps in data.get('dependencies',{}).values():
        for name,meta in deps.items():
            if meta.get('resolved'): allowed.add((name.lower(),meta['resolved']))
seen=set(); unexpected=[]
for deps_file in pub.rglob('*.deps.json'):
    data=json.loads(deps_file.read_text())
    for lib,meta in data.get('libraries',{}).items():
        if meta.get('type')!='package' or '/' not in lib: continue
        name,version=lib.rsplit('/',1); key=(name.lower(),version); seen.add(key)
        if key not in allowed: unexpected.append((deps_file.relative_to(pub).as_posix(),name,version))
if unexpected:
    for row in unexpected: print(f'unlocked published package: {row[1]} {row[2]} in {row[0]}',file=sys.stderr)
    raise SystemExit(1)
print(json.dumps({'publishedPackageVersions':len(seen),'lockedPackageVersions':len(allowed),'unexpected':0},indent=2))
