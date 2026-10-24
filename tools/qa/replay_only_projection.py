#!/usr/bin/env python3
"""Same-source Replay-only qualification. No solver/game/Unity/Player build."""
import argparse, hashlib, json, os, pathlib, subprocess, zipfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
sha=lambda b:hashlib.sha256(b).hexdigest()
def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--out',required=True,type=pathlib.Path);ap.add_argument('--boundaries-only',action='store_true');a=ap.parse_args()
    out=a.out.resolve();out.relative_to(ROOT/'.local');out.mkdir(parents=True,exist_ok=False)
    raw=pathlib.Path('/Users/eaafonasev/Projects/spacewars-planning/evidence/native-strategic-ai/frontier-materialization-20261018/member14-causal-raw.zip')
    with zipfile.ZipFile(raw) as z:
        assert z.testzip() is None
        for n in ['inputs.json','result.json','14.typed-legs.bin','donor-sidecar.json']:(out/n).write_bytes(z.read('connected2/'+n))
    declarations=ROOT/'tools/testdata/replay-actual214-declarations.json'
    (out/'declarations.json').write_bytes(declarations.read_bytes())
    (out/'query-reference.json').write_bytes((out/'result.json').read_bytes());(out/'result.json').unlink()
    pin=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    (out/'qa-plan.md').write_text('Baseline main2b92e91c, limited B+Replay port. Neutral declarations exported by exact donor producer582b; candidate constructs fresh owned provider and verifies every recorded binding without decoding donor Work. Diff Replay-only Host projection/prebirth tags, full movement points unchanged. Same C# Runtime+Simulation, actual saved214 legs and source-verified immutable donor geometry/profile, no search rerun. Paid original214 leg qualifications, compact forward traces, original Ready bytes, COW pins/cold-next and new allocation sites/refusal/reset. New search G/C/BK=0; prior query accounting cited separately, not refunded. No full/Unity/game/build/CPU claim.\n')
    source=out/'source';source.mkdir();bindings=[]
    paths=subprocess.check_output(['rg','--files','unity/Assets/Spacewars/Runtime','unity/Assets/Spacewars/Simulation'],cwd=ROOT,text=True).splitlines()
    for name in paths:
        if not name.endswith('.cs'):continue
        b=(ROOT/name).read_bytes();compiled=b
        if name.endswith('/CertifiedRouteKernel.cs'):
            needle='if(actual.TryTrace(state.At,state.Point(state.ReplayCursor),'
            assert b.decode().count(needle)==1
            compiled=b.decode().replace(needle,'if(ReplayFault.TryTrace(actual,state.At,state.Point(state.ReplayCursor),').encode()
        (source/name.replace('/','__')).write_bytes(compiled);bindings.append(dict(path=name,sha256=sha(b),compiledSHA256=sha(compiled),instrumented=compiled!=b,bytes=len(b)))
    helper=(ROOT/'tools/qa/replay_fixture_inputs.cs').read_text()
    (out/'Inputs.cs').write_text(helper);(out/'Program.cs').write_bytes(pathlib.Path(__file__).with_suffix('.cs').read_bytes())
    (out/'source-bindings.json').write_text(json.dumps(dict(pin=pin,files=bindings,helperSHA256=sha(helper.encode()),rawSHA256=sha(raw.read_bytes()),programSHA256=sha((out/'Program.cs').read_bytes()),runnerSHA256=sha(pathlib.Path(__file__).read_bytes())),indent=2))
    (out/'replay.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>disable</Nullable><EnableNETAnalyzers>false</EnableNETAnalyzers><GenerateAssemblyInfo>false</GenerateAssemblyInfo></PropertyGroup></Project>')
    env=dict(os.environ,DOTNET_CLI_TELEMETRY_OPTOUT='1');dotnet=str(pathlib.Path.home()/'.dotnet/dotnet')
    command=[dotnet,'build',str(out/'replay.csproj'),'-c','Release','--nologo','-v','quiet'];(out/'compile-command.json').write_text(json.dumps(command))
    with (out/'compile.log').open('w') as log:r=subprocess.run(command,env=env,stdout=log,stderr=subprocess.STDOUT)
    if r.returncode:print((out/'compile.log').read_text()[-6000:]);raise SystemExit(r.returncode)
    dll=out/'bin/Release/net8.0/replay.dll';(out/'compiled-bindings.json').write_text(json.dumps({p.name:sha(p.read_bytes()) for p in dll.parent.iterdir() if p.is_file()},indent=2))
    command=[dotnet,str(dll),str(out)]+(['boundaries'] if a.boundaries_only else []);(out/'run-command.json').write_text(json.dumps(command))
    with (out/'run.log').open('w') as log:r=subprocess.run(command,env=env,stdout=log,stderr=subprocess.STDOUT,timeout=600)
    for row in bindings:assert sha((ROOT/row['path']).read_bytes())==row['sha256'],row['path']
    print(json.dumps({k:v for k,v in json.loads((out/'result.json').read_text()).items() if k in ('status','stage','ledger','error')}) if (out/'result.json').exists() else (out/'run.log').read_text()[-6000:]);raise SystemExit(r.returncode)
if __name__=='__main__':main()
