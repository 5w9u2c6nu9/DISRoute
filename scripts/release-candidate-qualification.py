#!/usr/bin/env python3
import argparse,hashlib,json,pathlib,re,subprocess,sys,zipfile,os,tempfile
p=argparse.ArgumentParser(description='DISRoute release-candidate integrity and reproducibility gate.')
p.add_argument('--root',default='.'); p.add_argument('--publish-dir',required=True); p.add_argument('--output-dir',required=True)
p.add_argument('--version',default='1.0.0-rc.1'); p.add_argument('--source-commit',default='qualification'); p.add_argument('--source-tree',default='qualification'); p.add_argument('--source-date-epoch',type=int,required=True)
a=p.parse_args(); root=pathlib.Path(a.root).resolve(); publish=pathlib.Path(a.publish_dir).resolve(); out=pathlib.Path(a.output_dir).resolve(); out.mkdir(parents=True,exist_ok=True)
props=(root/'Directory.Build.props').read_text()
if f'<Version>{a.version}</Version>' not in props: raise SystemExit(f'Directory.Build.props is not version {a.version}')
globalj=json.loads((root/'global.json').read_text())
if globalj.get('sdk',{}).get('rollForward')!='disable': raise SystemExit('global.json must pin SDK with rollForward=disable')
locks=sorted(root.glob('src/*/packages.lock.json'))
projects=sorted(root.glob('src/*/*.csproj'))
if len(locks)!=len(projects): raise SystemExit(f'expected one lock file per project: {len(locks)} locks for {len(projects)} projects')
# Build twice from the same publish payload; byte-identical ZIPs are required.
env=os.environ.copy(); env['SOURCE_DATE_EPOCH']=str(a.source_date_epoch)
zip1=out/f'DISRoute-{a.version}-deployment-a.zip'; zip2=out/f'DISRoute-{a.version}-deployment-b.zip'
for target in (zip1,zip2):
    subprocess.run([sys.executable,str(root/'scripts/package-deployment.py'),str(publish),str(target),'--release-version',a.version,'--source-commit',a.source_commit,'--source-tree',a.source_tree,'--source-date-epoch',str(a.source_date_epoch)],check=True,env=env,stdout=subprocess.DEVNULL)
h1=hashlib.sha256(zip1.read_bytes()).hexdigest(); h2=hashlib.sha256(zip2.read_bytes()).hexdigest()
if h1!=h2: raise SystemExit(f'deployment package is not reproducible: {h1} != {h2}')
final=out/f'DISRoute-{a.version}-deployment.zip'; final.write_bytes(zip1.read_bytes()); zip1.unlink(); zip2.unlink()
manifest=out/f'DISRoute-{a.version}-deployment.manifest.json'
subprocess.run([sys.executable,str(root/'scripts/package-deployment.py'),str(publish),str(final),'--manifest',str(manifest),'--release-version',a.version,'--source-commit',a.source_commit,'--source-tree',a.source_tree,'--source-date-epoch',str(a.source_date_epoch)],check=True,env=env,stdout=subprocess.DEVNULL)
if hashlib.sha256(final.read_bytes()).hexdigest()!=h1: raise SystemExit('final deterministic package changed from reproducibility probe')
sbom=out/f'DISRoute-{a.version}-sbom.cdx.json'
subprocess.run([sys.executable,str(root/'scripts/generate-sbom.py'),str(root),'--output',str(sbom),'--version',a.version,'--source-date-epoch',str(a.source_date_epoch)],check=True,env=env,stdout=subprocess.DEVNULL)
with zipfile.ZipFile(final) as z:
    if z.testzip(): raise SystemExit('final deployment zip failed integrity test')
release={'product':'DISRoute','releaseVersion':a.version,'releaseChannel':'release-candidate','sourceCommit':a.source_commit,'sourceTree':a.source_tree,'sourceDateEpoch':a.source_date_epoch,'deploymentZip':{'file':final.name,'sha256':hashlib.sha256(final.read_bytes()).hexdigest()},'deploymentManifest':{'file':manifest.name,'sha256':hashlib.sha256(manifest.read_bytes()).hexdigest()},'sbom':{'file':sbom.name,'sha256':hashlib.sha256(sbom.read_bytes()).hexdigest()},'projectCount':len(projects),'lockFileCount':len(locks),'status':'QUALIFIED-IN-IMPLEMENTATION-ENVIRONMENT','externalAcceptanceRequired':True}
release_path=out/f'DISRoute-{a.version}-release-manifest.json'; release_path.write_text(json.dumps(release,indent=2,sort_keys=True)+'\n')
print(json.dumps(release,indent=2))
