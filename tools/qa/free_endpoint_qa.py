#!/usr/bin/env python3
"""Focused actual-source NUnit checks or exclusive eight-player CPU measurements.

Compilation uses Unity's bundled Roslyn, Mono/BCL and NUnit, without importing
Unity assets. Baseline is explicitly pinned. Benchmark-only pacing/stop
edits affect temporary copies; no decision, proof, route or physics cadence edits.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess

ROOT=Path(__file__).resolve().parents[2]
ENGINE=Path('/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents')
SCRIPTING=ENGINE/'Resources/Scripting'
BASELINE='ad18ef14c660ee1c7ac8966c91dd48445a9f9d60'
OUT=ROOT/'.local/free-endpoints'
PATCHES={
 'Runtime/PlayableRuntimePacing.cs':[('return next-now;','return 0; // QA only: uncapped wall pacing, fixed simulation dt unchanged.')],
 'Runtime/PlayableRuntime.cs':[('while(Volatile.Read(ref stopping)==0)','while(Volatile.Read(ref stopping)==0&&domain.Tick<FreeEndpointQa.TargetTicks)')]
}

def build(baseline=False,checks=False,frozen=False,scenario_ref=BASELINE,cpu=False):
    label='cpu' if cpu else 'frozen' if frozen else 'checks' if checks else 'baseline' if baseline else 'candidate'
    build_dir=OUT/'compile'/(label+'-'+os.environ.get('ENDPOINT_QA_BUILD_ID','default'));build_dir.mkdir(parents=True,exist_ok=False)
    files=[];manifest=[]
    for folder in (() if cpu else ('Runtime','Simulation')):
        for source in sorted((ROOT/'unity/Assets/Spacewars'/folder).rglob('*.cs')):
            path=source.relative_to(ROOT).as_posix();relative=source.relative_to(ROOT/'unity/Assets/Spacewars').as_posix()
            original=subprocess.check_output(['git','show',f'{BASELINE}:{path}'],cwd=ROOT) if baseline else source.read_bytes()
            text=original.decode()
            patches=[] if checks or frozen else PATCHES.get(relative,[])
            if frozen and relative=='Simulation/NavigationRouting.cs' and 'field.FreeRoots=true;' in text:
                patches=[('field.FreeRoots=true;','field.FreeRoots=FreeEndpointFrozen.Optimized;')]
            for before,after in patches:
                if text.count(before)!=1:raise ValueError('QA patch anchor differs: '+path)
                text=text.replace(before,after)
            target=build_dir/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_text(text);files.append(str(target))
            manifest.append({'path':path,'sourceSha256':hashlib.sha256(original).hexdigest(),'compiledSha256':hashlib.sha256(text.encode()).hexdigest()})
    refs=SCRIPTING/'MonoBleedingEdge/lib/mono/unityjit-macos'
    arguments=['-nostdlib+','-langversion:9','-optimize+','-target:exe',f'-out:{build_dir}/runner.exe']
    arguments += [f'-r:{refs/name}' for name in ('mscorlib.dll','System.dll','System.Core.dll','System.Numerics.dll','Facades/netstandard.dll')]
    arguments += [f'-r:{SCRIPTING}/Managed/Newtonsoft.Json.dll',str(ROOT/'tools/qa/free_endpoint_qa.cs'),str(ROOT/'tools/qa/route_cpu.cs')]+files
    if checks:
        nunit=ENGINE/'Resources/PackageManager/BuiltInPackages/com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll'
        arguments += ['-define:UNITY_INCLUDE_TESTS,FREE_ENDPOINT_CHECKS',f'-r:{nunit}',str(ROOT/'unity/Assets/Spacewars/Tests/EditMode/ObserverGeometryTileTests.cs'),str(ROOT/'unity/Assets/Spacewars/Tests/EditMode/SharedFlowRouterReference.cs')]
        shutil.copy2(nunit,build_dir)
        old=subprocess.check_output(['git','show',BASELINE+':unity/Assets/Spacewars/Simulation/NavigationRouting.cs'],cwd=ROOT,text=True).replace('SharedFlowRouter','BaselineSharedFlowRouter')
        (build_dir/'baseline-router.cs').write_text(old)
        arguments += [str(build_dir/'baseline-router.cs'),str(ROOT/'tools/qa/free_endpoint_checks.cs')]
        shutil.copy2(ROOT/'tools/qa/free_endpoint_checks.cs',build_dir)
    if frozen:
        shutil.copy2(ROOT/'tools/qa/free_endpoint_frozen.cs',build_dir/'frozen-harness.cs')
        arguments += ['-define:FREE_ENDPOINT_FROZEN',str(build_dir/'frozen-harness.cs')]
    if cpu:arguments += ['-define:CPU_CLOCK_ONLY']
    native=subprocess.run(['clang','-dynamiclib','-O2','-arch','arm64','-arch','x86_64',str(ROOT/'tools/qa/route_cpu.c'),'-o',str(build_dir/'libroute_cpu.dylib')],capture_output=True,text=True)
    (build_dir/'native-compile.log').write_text(native.stdout+native.stderr);native.check_returncode()
    for name in ('route_cpu.c','route_cpu.cs','free_endpoint_qa.cs'):shutil.copy2(ROOT/'tools/qa'/name,build_dir)
    rsp=build_dir/'sources.rsp';rsp.write_text('\n'.join('"'+a+'"' for a in arguments))
    result=subprocess.run([str(SCRIPTING/'NetCoreRuntime/dotnet'),str(SCRIPTING/'DotNetSdkRoslyn/csc.dll'),'@'+str(rsp)],cwd=ROOT,capture_output=True,text=True)
    (build_dir/'compile.log').write_text(result.stdout+result.stderr)
    if result.returncode:print(result.stdout+result.stderr);result.check_returncode()
    shutil.copy2(SCRIPTING/'Managed/Newtonsoft.Json.dll',build_dir)
    scenario_ref=subprocess.check_output(['git','rev-parse',scenario_ref+'^{commit}'],cwd=ROOT,text=True).strip()
    scenario_files=[]
    for source in ('PlayableProfile.json','CrimsonGroveProfile.json'):
        path='unity/Assets/Spacewars/Content/Resources/'+source
        if (ROOT/path).read_bytes()!=subprocess.check_output(['git','show',f'{scenario_ref}:{path}'],cwd=ROOT):raise ValueError('Scenario input changed: '+path)
        scenario_files.append({'path':path,'sha256':hashlib.sha256((ROOT/path).read_bytes()).hexdigest()})
    (build_dir/'manifest.json').write_text(json.dumps({'baseline':BASELINE,'scenarioRef':scenario_ref,'scenarioFiles':scenario_files,'candidateHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'mode':label,'platform':platform.platform(),'patches':'Frozen copy selects only certified endpoint reuse; all accepted optimizations remain enabled in both arms' if frozen else {} if checks else PATCHES,'sources':manifest,'cpuSourceHashes':{name:hashlib.sha256((ROOT/'tools/qa'/name).read_bytes()).hexdigest() for name in ('route_cpu.c','route_cpu.cs')},'nativeSha256':hashlib.sha256((build_dir/'libroute_cpu.dylib').read_bytes()).hexdigest(),'harnessSha256':hashlib.sha256((ROOT/'tools/qa/free_endpoint_qa.cs').read_bytes()).hexdigest(), 'buildFiles':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in build_dir.iterdir() if p.is_file()},'extraHarnesses':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in ([ROOT/'tools/qa/free_endpoint_frozen.cs'] if frozen else [ROOT/'unity/Assets/Spacewars/Tests/EditMode/ObserverGeometryTileTests.cs',ROOT/'unity/Assets/Spacewars/Tests/EditMode/SharedFlowRouterReference.cs'] if checks else [])}},indent=2))
    return build_dir

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--cpu-check',action='store_true');parser.add_argument('--calibrate',action='store_true');parser.add_argument('--baseline',action='store_true');parser.add_argument('--checks',action='store_true');parser.add_argument('--frozen',action='store_true');parser.add_argument('--run');parser.add_argument('--seed',type=int,default=23102026);parser.add_argument('--scenario-ref',default=BASELINE,help='Exact committed scenario inputs; default retains original experiment binding');args=parser.parse_args();args.frozen=args.frozen or args.calibrate;args.baseline=args.baseline or args.calibrate
    if args.cpu_check and (args.baseline or args.checks or args.frozen):parser.error('CPU probe is separate')
    if args.baseline and args.frozen and not args.calibrate:parser.error('Baseline/frozen combination is calibration only')
    if args.checks and (args.baseline or args.frozen):parser.error('Select at most one special mode')
    if shutil.disk_usage(ROOT).free<1500*1024*1024:raise RuntimeError('Insufficient free disk headroom; no run started')
    os.environ['ENDPOINT_QA_BUILD_ID']=args.run or ('frozen' if args.frozen else 'checks' if args.checks else 'build')
    build_dir=build(args.baseline,args.checks,args.frozen,args.scenario_ref,args.cpu_check)
    if args.run or args.checks or args.frozen:
        label=args.run or ('frozen-proofs' if args.frozen else 'focused-checks');output=OUT/label
        if output.parent!=OUT or output.exists():raise ValueError('Refusing output overwrite/nonlocal label')
        output.mkdir();shutil.copy2(build_dir/'manifest.json',output/'source-manifest.json');shutil.copy2(build_dir/'compile.log',output/'compile.log')
        env=dict(os.environ,SPACEWARS_PRECISE_PROFILE='0',DYLD_LIBRARY_PATH=str(build_dir),UNITY_RUN_MODE='shared' if args.checks or args.cpu_check else 'exclusive')
        with (OUT/(label+'.log')).open('w') as log:
            result=subprocess.run([str(ROOT/'tools/unity.sh'),'player',str(SCRIPTING/'MonoBleedingEdge/bin/mono'),str(build_dir/'runner.exe'),str(output),str(args.seed),'-noaudio','-muted']+(['--calibrate'] if args.calibrate else []),cwd=ROOT,env=env,stdout=log,stderr=subprocess.STDOUT)
        print(label,'exit',result.returncode);result.check_returncode()
