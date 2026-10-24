#!/usr/bin/env python3
"""Functional old/new exact comparator differential. No query or benchmark."""
import argparse, hashlib, json, os, pathlib, subprocess
ROOT=pathlib.Path(__file__).resolve().parents[2]
PIN='d1edd3d2f8595684fcf95d0e96b35d60073ec0bd'
CHANGED='unity/Assets/Spacewars/Simulation/NavigationExactCost.cs'
OWNED={'unity/Assets/Spacewars/Simulation/LayeredNavigationCompileWork.cs','unity/Assets/Spacewars/Simulation/NavigationDemandExact.cs',CHANGED}
sha=lambda b:hashlib.sha256(b).hexdigest()
def main():
    p=argparse.ArgumentParser();p.add_argument('--out',required=True);args=p.parse_args();out=pathlib.Path(args.out).resolve();out.mkdir(parents=True,exist_ok=False)
    paths=[p for p in subprocess.check_output(['git','ls-tree','-r','--name-only',PIN,'--','unity/Assets/Spacewars/Runtime','unity/Assets/Spacewars/Simulation'],cwd=ROOT,text=True).splitlines() if p.endswith('.cs')]
    changed=[];source=[]
    for name in paths:
        old=subprocess.check_output(['git','show',PIN+':'+name],cwd=ROOT);new=(ROOT/name).read_bytes()
        if old!=new:changed.append(name)
        source.append((name,old,new))
    assert set(changed)==OWNED,changed
    old=next(old for name,old,new in source if name==CHANGED).decode();new=next(new for name,old,new in source if name==CHANGED).decode()
    needle='public int CompareTo(NavExactCost other){for(int n=Limbs-1;n>=0;n--){ulong a=Limb(n),b=other.Limb(n);if(a!=b)return a<b?-1:1;}return 0;}'
    assert old.count(needle)==1
    begin=new.index('public int CompareTo(NavExactCost other)');end=new.index('        public bool Equals(NavExactCost other)',begin)
    assert old.count(needle)==1 and new[begin:end].strip().startswith('public int CompareTo(NavExactCost other)')
    env=dict(os.environ,DOTNET_CLI_TELEMETRY_OPTOUT='1');dotnet=str(pathlib.Path.home()/'.dotnet/dotnet')
    for mode,index in [('baseline',1),('candidate',2)]:
        folder=out/mode;folder.mkdir();src=folder/'source';src.mkdir();bindings=[]
        for row in source:
            name=row[0];raw=row[index];(src/name.replace('/','__')).write_bytes(raw);bindings.append(dict(path=name,sha256=sha(raw)))
        (folder/'Program.cs').write_bytes(pathlib.Path(__file__).with_suffix('.cs').read_bytes())
        (folder/'source-bindings.json').write_text(json.dumps(dict(pin=PIN,mode=mode,files=bindings,programSHA256=sha((folder/'Program.cs').read_bytes()),runnerSHA256=sha(pathlib.Path(__file__).read_bytes())),indent=2))
        define='<DefineConstants>CANDIDATE</DefineConstants>' if mode=='candidate' else ''
        (folder/'compare.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>disable</Nullable><EnableNETAnalyzers>false</EnableNETAnalyzers><GenerateAssemblyInfo>false</GenerateAssemblyInfo>'+define+'</PropertyGroup></Project>')
        command=[dotnet,'build',str(folder/'compare.csproj'),'-c','Release','--nologo','-v','quiet'];(folder/'compile-command.json').write_text(json.dumps(command))
        with (folder/'compile.log').open('w') as log:subprocess.run(command,env=env,stdout=log,stderr=subprocess.STDOUT,check=True)
        dll=folder/'bin/Release/net8.0/compare.dll';(folder/'compiled-bindings.json').write_text(json.dumps({p.name:sha(p.read_bytes()) for p in dll.parent.iterdir() if p.is_file()},indent=2))
        command=[dotnet,str(dll),str(folder)];(folder/'run-command.json').write_text(json.dumps(command))
        with (folder/'run.log').open('w') as log:subprocess.run(command,env=env,stdout=log,stderr=subprocess.STDOUT,check=True,timeout=60)
    for name in ['result.json','frontier-before.bin']+['frontier-after-'+str(i)+'.bin' for i in range(16)]:
        assert (out/'baseline'/name).read_bytes()==(out/'candidate'/name).read_bytes(),name
    for name,old,new in source:assert (ROOT/name).read_bytes()==new,'source changed during QA: '+name
    print((out/'candidate/result.json').read_text());print('Exact reports and17fullBODY bytepairs identical')
if __name__=='__main__':main()
