#!/usr/bin/env python3
"""Exact-source two-assembly ABBA control, warmup excluded, native CPU/allocation."""
import os, shutil, subprocess, json, hashlib
import field_key_qa as qa
root=qa.ROOT; out=qa.OUT
contract=out/'contract';contract.mkdir(exist_ok=False)
engine=qa.SCRIPTING;refs=engine/'MonoBleedingEdge/lib/mono/unityjit-macos'
base=['-nostdlib+','-langversion:9','-optimize+']+[f'-r:{refs/n}' for n in ('mscorlib.dll','System.dll','System.Core.dll','System.Numerics.dll','Facades/netstandard.dll')]
def compile(args,where):
    rsp=where/'sources.rsp';rsp.write_text('\n'.join('"'+str(a)+'"' for a in args));r=subprocess.run([str(engine/'NetCoreRuntime/dotnet'),str(engine/'DotNetSdkRoslyn/csc.dll'),'@'+str(rsp)],cwd=root,capture_output=True,text=True);(where/'compile.log').write_text(r.stdout+r.stderr)
    if r.returncode: print(r.stdout+r.stderr);r.check_returncode()
compile(base+['-target:library','-out:'+str(contract/'benchmark-contract.dll'),str(root/'tools/qa/route_packet_contract.cs')],contract)
os.environ['KEY_QA_BUILD_ID']='control'
b=qa.build(baseline=True,library=True);a=qa.build(library=True)
runner=out/'control-runner';runner.mkdir(exist_ok=False)
compile(base+['-target:exe','-out:'+str(runner/'runner.exe'),'-r:'+str(contract/'benchmark-contract.dll'),'-r:'+str(engine/'Managed/Newtonsoft.Json.dll'),str(root/'tools/qa/route_cpu.cs'),str(root/'tools/qa/field_key_control.cs')],runner)
for p in (contract/'benchmark-contract.dll',engine/'Managed/Newtonsoft.Json.dll',a/'libroute_cpu.dylib'):shutil.copy2(p,runner)
for src in ('field_key_control.cs','field_key_packet.cs','route_packet_contract.cs','route_cpu.cs','route_cpu.c','field_key_control.py'):shutil.copy2(root/'tools/qa'/src,runner)
output=out/'control';output.mkdir(exist_ok=False)
(output/'manifest.json').write_text(json.dumps({'baseline':qa.BASELINE,'candidate':subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),'files':{str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for d in (runner,contract) for p in d.iterdir() if p.is_file()},'moduleManifests':[str(b/'manifest.json'),str(a/'manifest.json')]},indent=2))
env=dict(os.environ,SPACEWARS_PRECISE_PROFILE='0',DYLD_LIBRARY_PATH=str(runner),UNITY_RUN_MODE='exclusive')
with (out/'control.log').open('w') as log:r=subprocess.run([str(root/'tools/unity.sh'),'player',str(engine/'MonoBleedingEdge/bin/mono'),str(runner/'runner.exe'),str(output),'23102026',str(b/'baseline-packet.dll'),str(a/'candidate-packet.dll'),'-noaudio','-muted'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
print('control exit',r.returncode);r.check_returncode()
