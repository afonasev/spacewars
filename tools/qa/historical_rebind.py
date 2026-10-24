#!/usr/bin/env python3
"""Controlled actual-domain historical rebind causal component probe; no Unity."""
import argparse,pathlib,subprocess,hashlib,json,os,difflib,zipfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
def sha(b):return hashlib.sha256(b).hexdigest()
def main():
 p=argparse.ArgumentParser();p.add_argument('--out',required=True,type=pathlib.Path);p.add_argument('--plain-pumps',type=int);p.add_argument('--actual-refinery',action='store_true');p.add_argument('--provider-prefix',action='store_true');p.add_argument('--actor3-page0',action='store_true');a=p.parse_args();out=a.out.resolve();out.relative_to(ROOT/'.local');out.mkdir(exist_ok=False,parents=True);source=out/'source';source.mkdir();rows=[];patch=[];inverse=[]
 (out/'qa-plan.md').write_text('CONTROLLED_CLASS_REPRO. Baseline '+subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()+'. ThreeCrossingsDefault actual domain, real enemy three-Tank Human AttackMove, normal authority-issued accrued credits and deterministic default-budget Kernel Pump. Stop at two paid assignments and third Pending, publish real Factory foundation site2/slot2 through normal BuildAt and AdvanceFoundations, then first historical drop observation. Only copied source DropHistorical entry receives static observation call; no authority layout/branch/charge/policy change. No Unity/Army/full/Player/build/deploy/performance.\n')
 for name in sorted(subprocess.check_output(['rg','--files','unity/Assets/Spacewars/Runtime','unity/Assets/Spacewars/Simulation'],cwd=ROOT,text=True).splitlines()):
  if not name.endswith('.cs'):continue
  b=(ROOT/name).read_bytes();s=b.decode();t=s
  if name.endswith('/NavigationHistoricalArrival.cs') and not a.plain_pumps and not a.actual_refinery and not a.provider_prefix and not a.actor3_page0:
   t=s.replace('private void DropHistorical()\n        {','private void DropHistorical()\n        {\n            global::HistoricalQa.Observe(this);');assert t!=s
  (source/name.replace('/','__')).write_text(t);rows.append(dict(path=name,gitBlob=subprocess.check_output(['git','rev-parse','HEAD:'+name],cwd=ROOT,text=True).strip(),originalSHA256=sha(b),compiledSHA256=sha(t.encode())))
  patch+=difflib.unified_diff(s.splitlines(True),t.splitlines(True),fromfile=name,tofile=name);inverse+=difflib.unified_diff(t.splitlines(True),s.splitlines(True),fromfile=name,tofile=name)
 (out/'instrumentation.patch').write_text(''.join(patch));(out/'instrumentation.inverse.patch').write_text(''.join(inverse));(out/'Program.cs').write_bytes(pathlib.Path(__file__).with_suffix('.cs').read_bytes());(out/'source-bindings.json').write_text(json.dumps(dict(head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),files=rows,runnerSHA256=sha(pathlib.Path(__file__).read_bytes()),programSHA256=sha((out/'Program.cs').read_bytes())),indent=2))
 (out/'checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>disable</Nullable><EnableNETAnalyzers>false</EnableNETAnalyzers><GenerateAssemblyInfo>false</GenerateAssemblyInfo></PropertyGroup></Project>')
 if a.actual_refinery or a.provider_prefix or a.actor3_page0:
  archive=pathlib.Path('/Users/eaafonasev/Projects/spacewars-planning/evidence/ai-opening-obligation-protection/route-retention-20261017/final/original-08f97d07-01a1497f/first-army-body-capture-582b2705.zip')
  assert sha(archive.read_bytes())=='7899f035c16debd66e930fe48ee7b87141922b63e9153e580ce09166933b2e46'
  with zipfile.ZipFile(archive) as bundle:
   raw=bundle.read('first-army.work.body');assert sha(raw)=='1004067cae0e8eb706da766ecd48a00154c3a4fb7f3eab5281a83437aea3752b'
   (out/'provider-donor.body').write_bytes(raw);(out/'provider-donor-sidecar.json').write_bytes(bundle.read('sidecar.json'))
  (out/'fixed-input-bindings.json').write_text(json.dumps(dict(archive=str(archive),archiveSHA256=sha(archive.read_bytes()),donorSHA256=sha(raw),producer='582b270570de4d17bc428b20494341bb1ffa24e3',purpose='immutable provider declarations only; no old work/prefix/proof reused'),indent=2))
  (out/'qa-plan.md').write_text('FIXED_ACTUAL_REFINERY_PREFLIGHT. Baseline current Git; source315 unmodified. Decode only immutable donor provider declarations using production BODY reader; verify full c5 provider/schema/profile/map/producer and exact bytepolicy. Derive fixed missing old buildings by exact geometry footprint and normal authored home slot/kind mapping; unknown ownership or first mismatch stops NOT_QUALIFIED. No alternate geometry/revision guesses, no old labels/frontier/proofs/Work transfer. Actual exact origins and target from parent. Baseline geometry7/providerc5/sourcea5/profile72 required before paying old solver work; no Unity/Army/full/Player/build/deploy/perf.\n')
 if a.provider_prefix:
  (out/'qa-plan.md').write_text('PROVIDER_ONLY_PREFIX_PROOF. Baseline ef59a08c/runtime4a. Actual315 source files unchanged, no copied instrumentation. Decode SHA1004067 donor with normal BODY reader and EOF/exact reencode; reuse only immutable provider declarations. Reconstruct old provider from current authored map and exact old geometry; require c5. Append exactly actual Refinery16 center(51.25861046343716,42.17939586009892),radius1.35 using current NavGeometry constructor revision8. Require new031f2ea8fd28c0efce2fc254b1840914d257c5ed76bbbd7facc9cd720edceb78, no variants. Compare MonotoneHeaderTo then every MonotonePartTo, record counts/firstfailedoperand. No world/building-kind assertion, solver/Pump, Army/Unity/Player/gamebuild/deploy/performance.\n')
 if a.actor3_page0:
  archive=pathlib.Path('/Users/eaafonasev/Projects/spacewars-planning/evidence/native-strategic-ai/frontier-materialization-20261018/arrival-idle-credit-20261019-raw.zip')
  assert sha(archive.read_bytes())=='075750e79ead454c91b5ca20f9256f01491bd0965ce9404731183437ebe7efb5'
  with zipfile.ZipFile(archive) as bundle:
   raw=bundle.read('ai-arrival-idle-credit-20261019-army600/game/first-army.ndjson');events=[json.loads(line) for line in raw.splitlines()]
  def walk(x):
   if isinstance(x,dict):
    if isinstance(x.get('subscription'),dict) and x['subscription'].get('GroupId')==10:yield x
    for value in x.values():yield from walk(value)
   elif isinstance(x,list):
    for value in x:yield from walk(value)
  reps={}
  for event in events:
   for rep in walk(event):reps.setdefault(rep['subscription']['EntityId'],rep)
  assert set(reps)=={10,12,14}
  snapshots={}
  for event in events:
   for header in event.get('headers',[]):
    for row in header['occupancy']['blocking']:
     if row['ActorId']==3 and (row['x'],row['z']) in [(-21.451473067226104,-47.93712136078625),(-28.708826396124259,-40.029375978959656)]:snapshots.setdefault('old' if row['x']==-21.451473067226104 else 'current',dict(tick=event['ownerTick'],inputKey=header['inputKey'],row=row,header=header))
  assert set(snapshots)=={'old','current'}
  event=next(e for e in events if e['ownerTick']==3434);requests=[x['request'] for x in event['root']['live']['allocating'] if x['request']['Entity'] in [10,12,14]];assert len(requests)==3
  (out/'actor3-inputs.json').write_text(json.dumps(dict(archive=str(archive),archiveSHA256=sha(archive.read_bytes()),rawSHA256=sha(raw),representatives=[reps[x] for x in [10,12,14]],currentRequests=requests,snapshots=snapshots),indent=2))
  (out/'qa-plan.md').write_text('ACTOR3_ONLY_PAGE0_PREDICATE_PROOF. Baseline b60bc107/runtime4a,315actualsource unchanged. Exact current031 provider/sourcea5/profile72/coreclearances ground1/ground/0/.72 from saved raw. Actor3-only preparation carrier contains only exact observed old actor3; absent current fulloccupancy explicitly not fabricated. Production owned PrepareRegions through bounded796Candidate/0Occupancy/0navigation preparation; no query or solver work. Compare private actual TooClose for supplied old/new actor3 positions/radius. Current memberclearances identical; record page0 regions for all3keys. Stop first actual boolean difference with fulloperands. No world/Army/Unity/Player/gamebuild/deploy/performance.\n')
 dotnet=str(pathlib.Path.home()/'.dotnet/dotnet');env=dict(os.environ,DOTNET_CLI_TELEMETRY_OPTOUT='1')
 for label,cmd in [('compile',[dotnet,'build',str(out/'checks.csproj'),'-c','Release','--nologo','-v','quiet']),('run',[dotnet,str(out/'bin/Release/net8.0/checks.dll'),str(out)]+(['actor3-page0'] if a.actor3_page0 else ['provider-prefix'] if a.provider_prefix else ['actual-refinery'] if a.actual_refinery else [str(a.plain_pumps)] if a.plain_pumps else []))]:
  (out/(label+'-command.json')).write_text(json.dumps(cmd))
  with (out/(label+'.log')).open('w') as log:r=subprocess.run(cmd,stdout=log,stderr=subprocess.STDOUT,env=env,timeout=240)
  if label=='compile' and r.returncode:print((out/'compile.log').read_text()[-6000:]);raise SystemExit(r.returncode)
  if label=='compile':(out/'compiled-bindings.json').write_text(json.dumps({p.name:sha(p.read_bytes()) for p in (out/'bin/Release/net8.0').iterdir() if p.is_file()},indent=2))
 for row in rows:assert sha((ROOT/row['path']).read_bytes())==row['originalSHA256']
 print((out/'run.log').read_text()[-3000:]);raise SystemExit(r.returncode)
if __name__=='__main__':main()
