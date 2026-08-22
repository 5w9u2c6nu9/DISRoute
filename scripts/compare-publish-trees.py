#!/usr/bin/env python3
import argparse,hashlib,json,pathlib
p=argparse.ArgumentParser(description='Compare two DISRoute publish trees byte-for-byte.')
p.add_argument('left'); p.add_argument('right'); a=p.parse_args()
def scan(root):
    root=pathlib.Path(root).resolve(); out={}
    for f in sorted(x for x in root.rglob('*') if x.is_file()):
        rel=f.relative_to(root).as_posix(); out[rel]=(f.stat().st_size,hashlib.sha256(f.read_bytes()).hexdigest())
    return out
l=scan(a.left); r=scan(a.right)
missing=sorted(set(l)-set(r)); extra=sorted(set(r)-set(l)); changed=sorted(k for k in set(l)&set(r) if l[k]!=r[k])
result={'leftFiles':len(l),'rightFiles':len(r),'missing':missing,'extra':extra,'changed':changed,'identical':not(missing or extra or changed)}
print(json.dumps(result,indent=2))
if not result['identical']: raise SystemExit(1)
