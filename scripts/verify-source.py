#!/usr/bin/env python3
from pathlib import Path
import json, re, sys, xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
errors=[]

cs_files=[p for p in (root/'src').rglob('*.cs') if not any(part in ('bin','obj') for part in p.parts)]
projects=[p for p in (root/'src').rglob('*.csproj') if not any(part in ('bin','obj') for part in p.parts)]

for p in cs_files:
    s=p.read_text(encoding='utf-8')
    if s.count('{') != s.count('}'): errors.append(f'{p.relative_to(root)}: brace mismatch')
    if s.count('(') != s.count(')'): errors.append(f'{p.relative_to(root)}: parenthesis mismatch')
    for token in ('NotImplementedException','TODO:','FIXME:'):
        if token in s: errors.append(f'{p.relative_to(root)}: unfinished implementation marker {token}')

xml_files=[p for p in (root/'src').rglob('*.axaml') if not any(part in ('bin','obj') for part in p.parts)] + projects + [root/'DISRoute.slnx',root/'Directory.Build.props',root/'Directory.Packages.props']
for p in xml_files:
    try: ET.parse(p)
    except Exception as ex: errors.append(f'{p.relative_to(root)}: invalid XML: {ex}')

for p in projects:
    try: tree=ET.parse(p)
    except Exception: continue
    for node in tree.findall('.//ProjectReference'):
        inc=node.attrib.get('Include')
        if inc:
            target=(p.parent/inc).resolve()
            if not target.exists(): errors.append(f'{p.relative_to(root)}: missing ProjectReference {inc}')

for p in (root/'src'/'DISRoute.Server').glob('appsettings*.json'):
    try: json.loads(p.read_text(encoding='utf-8'))
    except Exception as ex: errors.append(f'{p.relative_to(root)}: invalid JSON: {ex}')

# Route uniqueness across all server endpoint files.
routes={}
route_re=re.compile(r'([A-Za-z_][A-Za-z0-9_]*)\.Map(Get|Post|Put|Delete|Patch)\(\s*"([^"]+)"')
for p in (root/'src'/'DISRoute.Server').rglob('*.cs'):
    if any(part in ('bin','obj') for part in p.parts): continue
    s=p.read_text(encoding='utf-8')
    for m in route_re.finditer(s):
        key=(m.group(1),m.group(2).upper(),m.group(3))
        location=f'{p.relative_to(root)}:{s[:m.start()].count(chr(10))+1}'
        if key in routes: errors.append(f'duplicate route {key[0]}.{key[1]} {key[2]} at {routes[key]} and {location}')
        else: routes[key]=location

# Migration numbering and declared schema version must be continuous.
migrations=sorted((root/'db'/'migrations').glob('*.sql'))
nums=[]
for p in migrations:
    m=re.match(r'(\d+)_',p.name)
    if not m: errors.append(f'{p.relative_to(root)}: migration filename lacks numeric prefix'); continue
    nums.append(int(m.group(1)))
if nums and nums != list(range(1,max(nums)+1)): errors.append(f'db/migrations: non-contiguous sequence {nums}')
state_file=root/'src'/'DISRoute.Server'/'Operational'/'StatePersistenceService.cs'
if state_file.exists() and nums:
    m=re.search(r'CurrentSchemaVersion\s*=\s*(\d+)',state_file.read_text())
    if not m or int(m.group(1)) != max(nums): errors.append(f'CurrentSchemaVersion does not match latest migration {max(nums)}')

# Every published migration must advance schema_info to its own version. Pass 25 seals
# the historical chain with SHA-256 identities, so this also prevents regressions to the
# pre-ledger inconsistent schema_info shapes that made fresh PostgreSQL upgrades ambiguous.
for p, version in zip(migrations, nums):
    sql=p.read_text(encoding='utf-8')
    if version == 1:
        pattern=rf'INSERT\s+INTO\s+schema_info\s*\([^)]*version[^)]*\)\s*VALUES\s*\(\s*true\s*,\s*{version}\b'
    else:
        pattern=rf'UPDATE\s+schema_info\s+SET\s+version\s*=\s*{version}\b'
    if not re.search(pattern, sql, re.IGNORECASE | re.DOTALL):
        errors.append(f'{p.relative_to(root)}: does not advance schema_info to version {version}')
    stripped=sql.strip()
    starts_tx=bool(re.match(r'^BEGIN\s*;', stripped, re.IGNORECASE))
    ends_tx=bool(re.search(r'COMMIT\s*;\s*$', stripped, re.IGNORECASE))
    if starts_tx != ends_tx:
        errors.append(f'{p.relative_to(root)}: incomplete explicit transaction wrapper; Pass 25 atomic migration ledger requires both BEGIN and final COMMIT or neither')

# Pass 26 physical-HA safety artifacts are part of the production contract.
physical_guard=root/'src'/'DISRoute.Server'/'Operational'/'HaDatabaseTopologySafety.cs'
physical_harness=root/'scripts'/'postgresql-physical-ha-qualification.sh'
fault_proxy=root/'scripts'/'tcp-fault-proxy.py'
for required in (physical_guard, physical_harness, fault_proxy):
    if not required.exists(): errors.append(f'{required.relative_to(root)}: required Pass 26 physical-HA artifact missing')
if physical_guard.exists():
    guard=physical_guard.read_text(encoding='utf-8')
    for token in ('PhysicalFailover','WitnessConnectionString','DatabaseFencingMode=External'):
        if token not in guard: errors.append(f'{physical_guard.relative_to(root)}: missing physical-HA safety token {token}')

# Pass 27 production deployment/security artifacts are mandatory.
pass27_required=[
    root/'src'/'DISRoute.Server'/'Operational'/'SecretReferenceResolver.cs',
    root/'src'/'DISRoute.Server'/'Operational'/'ProductionDeploymentSafety.cs',
    root/'src'/'DISRoute.Server'/'Operational'/'WindowsServiceHost.cs',
    root/'scripts'/'production-deployment-qualification.sh',
    root/'scripts'/'package-deployment.py',
    root/'deploy'/'linux'/'disroute-server.service',
    root/'deploy'/'linux'/'install.sh',
    root/'deploy'/'windows'/'Install-DISRouteServer.ps1',
    root/'deploy'/'windows'/'Update-DISRouteServer.ps1',
    root/'docs'/'ops'/'PRODUCTION-DEPLOYMENT.md',
    root/'docs'/'ops'/'UPGRADE-ROLLBACK.md',
]
for required in pass27_required:
    if not required.exists(): errors.append(f'{required.relative_to(root)}: required Pass 27 deployment artifact missing')
if (root/'src'/'DISRoute.Server'/'Operational'/'SecretReferenceResolver.cs').exists():
    secret=(root/'src'/'DISRoute.Server'/'Operational'/'SecretReferenceResolver.cs').read_text(encoding='utf-8')
    for token in ('secret://env/','secret://file/','GetUnixFileMode'):
        if token not in secret: errors.append(f'src/DISRoute.Server/Operational/SecretReferenceResolver.cs: missing secret safety token {token}')
if (root/'src'/'DISRoute.Server'/'Operational'/'ProductionDeploymentSafety.cs').exists():
    safety=(root/'src'/'DISRoute.Server'/'Operational'/'ProductionDeploymentSafety.cs').read_text(encoding='utf-8')
    for token in ('RequireMfa','KnownProxies','CertificatePath','PermitLimitPerMinute','PhysicalFailover'):
        if token not in safety: errors.append(f'src/DISRoute.Server/Operational/ProductionDeploymentSafety.cs: missing production safety token {token}')
if (root/'src'/'DISRoute.Server'/'Operational'/'WindowsServiceHost.cs').exists():
    svc=(root/'src'/'DISRoute.Server'/'Operational'/'WindowsServiceHost.cs').read_text(encoding='utf-8')
    for token in ('StartServiceCtrlDispatcher','RegisterServiceCtrlHandlerEx','ServiceControlStop'):
        if token not in svc: errors.append(f'src/DISRoute.Server/Operational/WindowsServiceHost.cs: missing Windows SCM token {token}')


# Pass 30 release-candidate / supply-chain artifacts are mandatory.
pass30_required=[
    root/'global.json',
    root/'.github'/'workflows'/'verify.yml',
    root/'scripts'/'generate-sbom.py',
    root/'scripts'/'compare-publish-trees.py',
    root/'scripts'/'release-candidate-qualification.py',
    root/'scripts'/'verify-published-dependencies.py',
    root/'docs'/'ops'/'RELEASE-CANDIDATE-ACCEPTANCE.md',
    root/'docs'/'ops'/'KNOWN-LIMITATIONS.md',
    root/'docs'/'ops'/'RELEASE-CHECKLIST.md',
    root/'docs'/'ops'/'RELEASE-NOTES-1.0.0-rc.1.md',
    root/'PASS-30-NOTES.md',
]
for required in pass30_required:
    if not required.exists(): errors.append(f'{required.relative_to(root)}: required Pass 30 RC artifact missing')
props=(root/'Directory.Build.props').read_text(encoding='utf-8')
for token in ('<Version>1.0.0-rc.1</Version>','<Deterministic>true</Deterministic>','<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>'):
    if token not in props: errors.append(f'Directory.Build.props: missing RC token {token}')
try:
    global_json=json.loads((root/'global.json').read_text(encoding='utf-8'))
    sdk=global_json.get('sdk',{})
    if sdk.get('version') != '10.0.400' or sdk.get('rollForward') != 'disable': errors.append('global.json: Pass 30 requires exact SDK 10.0.400 with rollForward=disable')
except Exception as ex:
    errors.append(f'global.json: invalid JSON: {ex}')
locks=sorted((root/'src').glob('*/packages.lock.json'))
if len(locks) != len(projects): errors.append(f'Pass 30 requires one packages.lock.json per project: {len(locks)} locks for {len(projects)} projects')
else:
    for lock in locks:
        try:
            data=json.loads(lock.read_text(encoding='utf-8'))
            if data.get('version') not in (1,2): errors.append(f'{lock.relative_to(root)}: unsupported lock-file version')
        except Exception as ex:
            errors.append(f'{lock.relative_to(root)}: invalid lock JSON: {ex}')

# Hard-branding guard. Generic prose use of the word legacy is allowed; historical vendor/product tokens are not.
forbidden=['Emergency'+'Plus','EPL'+'Server','EPL'+'Local']
for p in root.rglob('*'):
    if not p.is_file() or any(x in p.parts for x in ('.git','bin','obj')): continue
    if p.suffix.lower() in {'.zip','.png','.jpg','.jpeg','.pdf'}: continue
    try: text=p.read_text(encoding='utf-8')
    except Exception: continue
    for token in forbidden:
        if token.lower() in text.lower() or token.lower() in p.name.lower(): errors.append(f'{p.relative_to(root)}: forbidden branding token {token}')

if errors:
    print('\n'.join(errors)); sys.exit(1)
print(f'Source structure OK: {len(cs_files)} C# files, {len(projects)} projects, {len(routes)} API routes, {len(migrations)} migrations')
