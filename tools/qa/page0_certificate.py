#!/usr/bin/env python3
"""Reuse archived page0 affected C# contracts against current source; no Unity."""
import argparse,pathlib,json,hashlib,zipfile,subprocess,os,re
ROOT=pathlib.Path(__file__).resolve().parents[2]
ARCHIVE=pathlib.Path('/Users/eaafonasev/Projects/spacewars-planning/evidence/ai-opening-obligation-protection/route-retention-20261017/final/original-08f97d07-01a1497f/page0-occupancy-fa111f9f.zip')
def sha(b):return hashlib.sha256(b).hexdigest()
def main():
 p=argparse.ArgumentParser();p.add_argument('--out',type=pathlib.Path,required=True);p.add_argument('--mode',choices=['semantic','more','final','last','wrapper','legacy','guards'],required=True);p.add_argument('--source-pin');a=p.parse_args();out=a.out.resolve();out.relative_to(ROOT/'.local');out.mkdir(parents=True,exist_ok=False);(out/'source').mkdir()
 assert sha(ARCHIVE.read_bytes())=='4a01a015686edbfd17c34f5284e18a022d95dc506770df53fd422b3a0e59277d'
 with zipfile.ZipFile(ARCHIVE) as z:
  fixtures=z.read('wrapper-drop/Fixtures.cs').decode();program=z.read('LegacyProgram.cs' if a.mode=='legacy' else 'wrapper-drop/Program.cs').decode();(out/'nunit.framework.dll').write_bytes(z.read('nunit.framework.dll'))
  (out/'archived-Program.cs.txt').write_text(program);(out/'archived-Fixtures.cs.txt').write_text(fixtures)
 paths=subprocess.check_output(['git','ls-tree','-r','--name-only',a.source_pin,'--','unity/Assets/Spacewars/Runtime','unity/Assets/Spacewars/Simulation'] if a.source_pin else ['rg','--files','unity/Assets/Spacewars/Runtime','unity/Assets/Spacewars/Simulation'],cwd=ROOT,text=True).splitlines();rows=[]
 for name in sorted(x for x in paths if x.endswith('.cs')):
  raw=subprocess.check_output(['git','show',a.source_pin+':'+name],cwd=ROOT) if a.source_pin else (ROOT/name).read_bytes();(out/'source'/name.replace('/','__')).write_bytes(raw);rows.append(dict(path=name,originalSHA256=sha(raw),compiledSHA256=sha(raw)))
 index=next((out/'source').glob('*NavigationPhysicalIndex.cs')).read_text();new='Page0OccupancyArrivalPolicyV17' in index
 # Update only explicit schema/policy version selection in the archived builders.
 prior='PrefixState(w).Page0Occupancy=version==20;PrefixState(w).Lazy=version==20||version>=13;'
 flags=('PrefixState(w).Page0Occupancy=version==21;' if new else '')+'PrefixState(w).TerminalCost=version>=20;PrefixState(w).OwnedArrivalAnchor=version>=19;PrefixState(w).OriginShadow=version>=18;PrefixState(w).TerminalCorner=version>=17;PrefixState(w).TerminalRectangle=version>=16;PrefixState(w).RectangleOrigins=version>=15;PrefixState(w).Certified=version>=14;PrefixState(w).Lazy=version>=13;'
 assert prior in fixtures;fixtures=fixtures.replace(prior,flags)
 if a.mode=='legacy':
  program=program.replace('Enumerable.Range(2,12)','Enumerable.Range(2,19)')
  program=program.replace('("Lazy",version>=13),','("TerminalCost",version>=20),("OwnedArrivalAnchor",version>=19),("OriginShadow",version>=18),("TerminalCorner",version>=17),("TerminalRectangle",version>=16),("RectangleOrigins",version>=15),("Certified",version>=14),("Lazy",version>=13),')
 else:
  program=program.replace('Page0OccupancyArrivalPolicyV16','Page0OccupancyArrivalPolicyV17').replace('==86,"value86"','==91,"value91"')
  program=program.replace('==312,"actual layout312"','<=512,"actual layout fits fixed charge"').replace('layout=312','layout=NavStorageLayout.ObjectBytesBound(typeof(NavHistoricalArrivalState))')
  # Old wrapper20 overlays lazy13. New wrapper21 overlays current TerminalCost20.
  program=re.sub(r',20(?=[,)])',',21',program);program=re.sub(r',13(?=[,)])',',20',program)
  program=program.replace('==20,"fresh20"','==21,"fresh21"').replace('==13&&!H(c).Page0Occupancy','==20&&!H(c).Page0Occupancy').replace('legacy13.body','legacy20.body')
  program=program.replace('Enumerable.Range(14,6)','Enumerable.Range(22,6)').replace('Enumerable.Range(73,20)','Enumerable.Range(92,13)')
 if a.mode=='guards':
  extra=r"""
 static void GuardCases(){
  var b=new NavigationRetentionBudget();NavigationArrivalWork old=null,w=null;
  try{
   old=Birth(b,F.RemovalInput("old",100),20,ready:true);w=Birth(b,F.RemovalInput("radius",200),21,old);long epoch=0;
   while(H(w).Oracle!=null&&epoch<1000){var n=Next(b,w,epoch++,1);w.ReleaseOwner();w=n;}
   Check(epoch<1000&&H(w).Oracle==null&&w.Proposals.Count==0&&w.MemberCursor==0&&w.WorkPhase=="PrepareRegions","changed current region whole fallback");report.Add(new{kind="current-region-changed",passed=true,epochs=epoch,w.AllocationWorkConsumed});
  }finally{w?.ReleaseOwner();old?.ReleaseOwner();Zero(b);}
  b=new NavigationRetentionBudget();old=w=null;
  try{
   old=Birth(b,F.RemovalInput("old",100),20,ready:true);w=Birth(b,F.RemovalInput("after",200),21,old);
   var hidden=BindingFlags.Instance|BindingFlags.NonPublic;var region=(NavTerminalRegion)typeof(NavigationArrivalWork).GetMethod("RemovalRegion",hidden).Invoke(w,new object[]{old,0});
   var wrong=new NavTerminalRegion(region.Id+"-wrong",region.Revision,region.Requested,region.Candidates);var recipe=typeof(NavigationArrivalWork).GetMethod("RemovalOldRecipe",hidden);
   Check((bool)recipe.Invoke(w,new object[]{0,region})&&!(bool)recipe.Invoke(w,new object[]{0,wrong}),"actual old physical recipe guard");
   bool emptyRejected=false;try{new NavTerminalRegion("invalid-empty",1,region.Requested,Array.Empty<NavLocation>());}catch(ArgumentException){emptyRejected=true;}Check(emptyRejected,"nonnull empty region impossible");report.Add(new{kind="old-physical-recipe-and-empty-region-contract",actualRecipeAccepted=true,differentRecipeRejected=true,emptyRejected});
  }finally{w?.ReleaseOwner();old?.ReleaseOwner();Zero(b);}
  b=new NavigationRetentionBudget();old=w=null;
  try{
   var raw=F.RemovalInput("old",100);var blockers=raw.Source.Candidates.Select((at,i)=>Actor(200+i,at)).ToArray();old=Birth(b,Change(raw,blockers),20,ready:true);
   Check(old.MemberOutcomes.Count>0&&old.Save().PageIndex==0,"real page0 terminal outcomes");w=Birth(b,Change(F.RemovalInput("after",200),blockers),21,old);long epoch=0;
   while(H(w).Oracle!=null&&epoch<1000){var n=Next(b,w,epoch++,1);w.ReleaseOwner();w=n;}
   Check(epoch<1000&&H(w).Oracle==null&&w.Proposals.Count==0&&w.MemberCursor==0&&w.WorkPhase=="PrepareRegions","old outcomes whole fallback");report.Add(new{kind="real-old-terminal-outcome",outcomes=old.MemberOutcomes.Count,passed=true,epochs=epoch});
  }finally{w?.ReleaseOwner();old?.ReleaseOwner();Zero(b);}
 }
"""
  marker=' static void Main(string[] args)'
  assert marker in program;program=program.replace(marker,extra+marker,1)
  program=program.replace('folder=args[0];try{','folder=args[0];try{if(args.Length>1&&args[1]=="guards"){GuardCases();File.WriteAllText(Path.Combine(folder,"result.json"),JsonSerializer.Serialize(new{classification="PASS",report},new JsonSerializerOptions{WriteIndented=true}));return;}',1)
 (out/'Fixtures.cs').write_text(fixtures);(out/'Program.cs').write_text(program)
 (out/'qa-plan.md').write_text('Baseline main6ba6e39c. Reuse archived actual C# affected page0 contracts; minimal current policy adaptation wrapper21/current20 and physical91, valid current1..20 remain interpreted as originally; unsupported future checks move beyond21/91. Original assertions, seeds, grant sizes, epoch bounds and exact byte/counter checks retained; measure actual owner layout <=fixed512 rather than historical312. Selected mode '+a.mode+'. No Unity/Army/full/Player/gamebuild/deploy.\n')
 bindings=dict(head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),sourcePin=a.source_pin,archiveSHA256=sha(ARCHIVE.read_bytes()),files=rows,programSHA256=sha(program.encode()),fixturesSHA256=sha(fixtures.encode()),runnerSHA256=sha(pathlib.Path(__file__).read_bytes()));(out/'source-bindings.json').write_text(json.dumps(bindings,indent=2))
 (out/'checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>disable</Nullable><EnableNETAnalyzers>false</EnableNETAnalyzers><GenerateAssemblyInfo>false</GenerateAssemblyInfo></PropertyGroup><ItemGroup><Reference Include="nunit.framework"><HintPath>'+str(out/'nunit.framework.dll')+'</HintPath></Reference></ItemGroup></Project>')
 env=dict(os.environ,DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_TieredCompilation='0');dotnet=str(pathlib.Path.home()/'.dotnet/dotnet')
 for label,cmd in [('compile',[dotnet,'build',str(out/'checks.csproj'),'-c','Release','--nologo','-v','quiet']),('run',[dotnet,str(out/'bin/Release/net8.0/checks.dll'),str(out)]+([] if a.mode in ['semantic','legacy'] else [a.mode]))]:
  (out/(label+'-command.json')).write_text(json.dumps(cmd))
  with (out/(label+'.log')).open('w') as log:r=subprocess.run(cmd,env=env,stdout=log,stderr=subprocess.STDOUT,timeout=240)
  if label=='compile':
   if r.returncode:print((out/'compile.log').read_text()[-5000:]);raise SystemExit(r.returncode)
   (out/'compiled-bindings.json').write_text(json.dumps({f.name:sha(f.read_bytes()) for f in (out/'bin/Release/net8.0').iterdir() if f.is_file()},indent=2))
 if not a.source_pin:
  for row in rows:assert sha((ROOT/row['path']).read_bytes())==row['originalSHA256']
 print((out/('legacy-result.json' if a.mode=='legacy' else 'result.json')).read_text()[-7000:]);raise SystemExit(r.returncode)
if __name__=='__main__':main()
