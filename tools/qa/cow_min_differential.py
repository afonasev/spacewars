#!/usr/bin/env python3
"""Same-C# immutable baseline/candidate COW-min and targeted cold-next checks."""
import argparse, difflib, hashlib, json, os, pathlib, subprocess
W=pathlib.Path(__file__).resolve().parents[2]
PIN='d1edd3d2f8595684fcf95d0e96b35d60073ec0bd'
OWNED={'unity/Assets/Spacewars/Simulation/LayeredNavigationCompileWork.cs','unity/Assets/Spacewars/Simulation/NavigationDemandExact.cs','unity/Assets/Spacewars/Simulation/NavigationExactCost.cs'}
sha=lambda b:hashlib.sha256(b).hexdigest()
def main():
 p=argparse.ArgumentParser();p.add_argument('--out',required=True);a=p.parse_args();out=pathlib.Path(a.out).resolve();out.mkdir(parents=True,exist_ok=False)
 paths=subprocess.check_output(['git','ls-tree','-r','--name-only',PIN,'--','unity/Assets/Spacewars/Runtime','unity/Assets/Spacewars/Simulation'],cwd=W,text=True).splitlines();env=os.environ.copy();env.update(DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_TieredCompilation='0');dotnet=str(pathlib.Path.home()/'.dotnet/dotnet');program=pathlib.Path(__file__).with_suffix('.cs')
 for mode in ['baseline','candidate']:
  folder=out/mode;source=folder/'source';source.mkdir(parents=True);bindings=[];patch=[]
  for path in paths:
   if not path.endswith('.cs'):continue
   old=subprocess.check_output(['git','show',PIN+':'+path],cwd=W);current=(W/path).read_bytes();assert path in OWNED or current==old,path
   raw=old if mode=='baseline' else current;text=raw.decode()
   if path.endswith('/NavigationExactCost.cs'):
    token='public int CompareTo(NavExactQueueEntry other){';assert text.count(token)==1;text=text.replace(token,token+'CowAudit.Comparisons++;')
   compiled=text.encode();(source/path.replace('/','__')).write_bytes(compiled);bindings.append(dict(path=path,baselineSHA256=sha(old),sourceSHA256=sha(raw),compiledSHA256=sha(compiled),instrumented=compiled!=raw))
   if compiled!=old:patch.extend(difflib.unified_diff(old.decode().splitlines(True),text.splitlines(True),fromfile='baseline/'+path,tofile=mode+'/'+path))
  if mode=='candidate':
   changed={row['path'] for row in bindings if row['baselineSHA256']!=row['sourceSHA256']}
   assert changed==OWNED,changed
  (folder/'source-bindings.json').write_text(json.dumps(dict(baseline=PIN,mode=mode,programSHA256=sha(program.read_bytes()),files=bindings),indent=2));(folder/'source.patch').write_text(''.join(patch));(folder/'Program.cs').write_bytes(program.read_bytes())
  define='<DefineConstants>CANDIDATE</DefineConstants>' if mode=='candidate' else ''
  (folder/'checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>disable</Nullable><EnableNETAnalyzers>false</EnableNETAnalyzers><GenerateAssemblyInfo>false</GenerateAssemblyInfo>'+define+'</PropertyGroup></Project>')
  cmd=[dotnet,'build',str(folder/'checks.csproj'),'-c','Release','--nologo','-v','quiet'];(folder/'compile-command.json').write_text(json.dumps(cmd))
  with (folder/'compile.log').open('w')as log:subprocess.run(cmd,env=env,stdout=log,stderr=subprocess.STDOUT,check=True)
  dll=folder/'bin/Release/net8.0/checks.dll';(folder/'compiled-bindings.json').write_text(json.dumps({f.name:sha(f.read_bytes())for f in dll.parent.iterdir()if f.is_file()},indent=2));cmd=[dotnet,str(dll),str(folder)];(folder/'run-command.json').write_text(json.dumps(cmd))
  with(folder/'run.log').open('w')as log:r=subprocess.run(cmd,env=env,stdout=log,stderr=subprocess.STDOUT,timeout=180)
  if r.returncode:raise SystemExit(f'{mode} check failed, see {folder}/result.json')
 reports=[json.loads((out/m/'result.json').read_text())for m in ['baseline','candidate']]
 def normalized(value):
  if isinstance(value,dict):return {k:normalized(v)for k,v in value.items()if k!='Comparisons'}
  if isinstance(value,list):return[normalized(v)for v in value]
  return value
 assert normalized(reports[0])==normalized(reports[1]),'exact baseline/candidate report mismatch'
 for old,new in zip(reports[0]['containers'],reports[1]['containers']):
  if old['success']and old['extracted']['Node']!=0:assert old['Comparisons']>0 and new['Comparisons']==0,'removed comparer search passes'
 for old,new in zip(reports[0]['queries'],reports[1]['queries']):
  assert new['Comparisons']==0
  assert (old['Comparisons']>0)==(old['target'] in ('direct','edges'))
 (out/'result.json').write_text(json.dumps(dict(classification='EXACT_COW_MIN_DIFFERENTIAL_PASS',baseline=PIN,containers=len(reports[0]['containers']),queries=len(reports[0]['queries']),fullReportsEqualExceptComparerCount=True,gameplay='NotRun; existing ArmyFAIL retained',performance='NotRun; comparer calls only'),indent=2));print(out/'result.json')
if __name__=='__main__':main()
