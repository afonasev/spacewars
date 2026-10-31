#!/usr/bin/env python3
"""Focused actual-source NUnit checks or exclusive eight-player CPU measurements.

Compilation uses Unity's bundled Roslyn, Mono/BCL and NUnit, without importing
Unity assets. Baseline is explicitly pinned. Benchmark-only pacing/stop/flush
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
BASELINE='91648d8a02eb2a4ec9b737ac93f38405eb9e2b1c'
OUT=ROOT/'.local/route-sparse-readiness'
PATCHES={
 'Runtime/PlayableRuntimePacing.cs':[('return next-now;','return 0; // QA only: uncapped wall pacing, fixed simulation dt unchanged.')],
 'Runtime/PlayableRuntime.cs':[('while(Volatile.Read(ref stopping)==0)','while(Volatile.Read(ref stopping)==0&&domain.Tick<RouteReuseQa.TargetTicks)'),('finally{domain.FlushMovementDelayEvidence();','finally{PerformanceProfile.Flush();domain.FlushMovementDelayEvidence();')]
}

def build(baseline=False,checks=False,frozen=False,scenario_ref=BASELINE):
    label='frozen' if frozen else 'checks' if checks else 'baseline' if baseline else 'candidate'
    build_dir=OUT/'compile'/label;build_dir.mkdir(parents=True,exist_ok=True)
    files=[];manifest=[]
    for folder in ('Runtime','Simulation'):
        for source in sorted((ROOT/'unity/Assets/Spacewars'/folder).rglob('*.cs')):
            path=source.relative_to(ROOT).as_posix();relative=source.relative_to(ROOT/'unity/Assets/Spacewars').as_posix()
            original=subprocess.check_output(['git','show',f'{BASELINE}:{path}'],cwd=ROOT) if baseline else source.read_bytes()
            text=original.decode()
            patches=[] if checks or frozen else PATCHES.get(relative,[])
            if frozen and relative=='Simulation/NavigationRouting.cs':
                old_source=subprocess.check_output(['git','show',f'{BASELINE}:{path}'],cwd=ROOT,text=True)
                start=text.index('            // Distances only grow during this invocation.')
                end=text.index('            if(field.Frontier.Count==0)closedReachability=field.Distance;',start)
                old_start=old_source.index('            bool Ready()')
                old_end=old_source.index('            if(field.Frontier.Count==0)closedReachability=field.Distance;',old_start)
                patches=[(text[start:end],'            if(RouteReuseFrozen.Memoize){\n'+text[start:end]+'            }else{\n'+old_source[old_start:old_end]+'            }\n')]
            for before,after in patches:
                if text.count(before)!=1:raise ValueError('QA patch anchor differs: '+path)
                text=text.replace(before,after)
            target=build_dir/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_text(text);files.append(str(target))
            manifest.append({'path':path,'sourceSha256':hashlib.sha256(original).hexdigest(),'compiledSha256':hashlib.sha256(text.encode()).hexdigest()})
    harness=build_dir/'harness.cs'
    harness.write_text((ROOT/'tools/qa/route_reuse_qa.cs').read_text().replace('Spacewars.Tests.EditMode.ObserverGeometryTileTests.RunSelected()', 'SparseReadinessChecks.RunSelected()'))
    refs=SCRIPTING/'MonoBleedingEdge/lib/mono/unityjit-macos'
    arguments=['-nostdlib+','-langversion:9','-optimize+','-target:exe',f'-out:{build_dir}/runner.exe']
    arguments += [f'-r:{refs/name}' for name in ('mscorlib.dll','System.dll','System.Core.dll','System.Numerics.dll','Facades/netstandard.dll')]
    arguments += [f'-r:{SCRIPTING}/Managed/Newtonsoft.Json.dll',str(harness)]+files
    if checks:
        nunit=ENGINE/'Resources/PackageManager/BuiltInPackages/com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll'
        arguments += ['-define:UNITY_INCLUDE_TESTS,ROUTE_REUSE_TESTS',f'-r:{nunit}',str(ROOT/'unity/Assets/Spacewars/Tests/EditMode/ObserverGeometryTileTests.cs'),str(ROOT/'unity/Assets/Spacewars/Tests/EditMode/SharedFlowRouterReference.cs')]
        baseline_source=subprocess.check_output(['git','show',f'{BASELINE}:unity/Assets/Spacewars/Simulation/NavigationRouting.cs'],cwd=ROOT,text=True)
        reference=build_dir/'BaselineSharedFlowRouter.cs';reference.write_text(baseline_source.replace('SharedFlowRouter','BaselineSharedFlowRouter'))
        arguments += [str(reference),str(ROOT/'tools/qa/sparse_readiness_checks.cs')]
        shutil.copy2(nunit,build_dir)
    if frozen:arguments += ['-define:ROUTE_REUSE_FROZEN',str(ROOT/'tools/qa/sparse_readiness_frozen.cs')]
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
    (build_dir/'manifest.json').write_text(json.dumps({'baseline':BASELINE,'scenarioRef':scenario_ref,'scenarioFiles':scenario_files,'candidateHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'mode':label,'platform':platform.platform(),'patches':'Frozen source copy selects only sparse readiness block; geometry memoization on both arms; no authority ticks advance' if frozen else {} if checks else PATCHES,'sources':manifest,'harnessSha256':hashlib.sha256(harness.read_bytes()).hexdigest(), 'extraHarnesses':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in ([ROOT/'tools/qa/sparse_readiness_checks.cs',build_dir/'BaselineSharedFlowRouter.cs'] if checks else [ROOT/'tools/qa/sparse_readiness_frozen.cs'] if frozen else [])}},indent=2))
    return build_dir

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--baseline',action='store_true');parser.add_argument('--checks',action='store_true');parser.add_argument('--frozen',action='store_true');parser.add_argument('--run');parser.add_argument('--ticks',type=int,default=5400);parser.add_argument('--seed',type=int,default=23102026);parser.add_argument('--scenario-ref',default=BASELINE,help='Exact committed scenario inputs; default retains original experiment binding');args=parser.parse_args()
    if sum((args.checks,args.baseline,args.frozen))>1:parser.error('Select at most one special mode')
    if shutil.disk_usage(ROOT).free<1500*1024*1024:raise RuntimeError('Insufficient free disk headroom; no run started')
    build_dir=build(args.baseline,args.checks,args.frozen,args.scenario_ref)
    if args.run or args.checks or args.frozen:
        label=args.run or ('frozen-proofs' if args.frozen else 'focused-checks');output=OUT/label
        if output.parent!=OUT or output.exists():raise ValueError('Refusing output overwrite/nonlocal label')
        output.mkdir();shutil.copy2(build_dir/'manifest.json',output/'source-manifest.json');shutil.copy2(build_dir/'compile.log',output/'compile.log')
        env=dict(os.environ,SPACEWARS_PRECISE_PROFILE='0' if args.checks or args.frozen else '1',ROUTE_REUSE_TICKS=str(args.ticks),UNITY_RUN_MODE='shared' if args.checks else 'exclusive')
        with (OUT/(label+'.log')).open('w') as log:
            result=subprocess.run([str(ROOT/'tools/unity.sh'),'player',str(SCRIPTING/'MonoBleedingEdge/bin/mono'),str(build_dir/'runner.exe'),str(output),str(args.seed),'-noaudio','-muted'],cwd=ROOT,env=env,stdout=log,stderr=subprocess.STDOUT)
        print(label,'exit',result.returncode);result.check_returncode()
