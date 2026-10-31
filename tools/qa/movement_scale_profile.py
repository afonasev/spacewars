#!/usr/bin/env python3
"""Actual-source component CPU profile; never rendered FPS or threaded-match proof."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
BASELINE = '9fef5003441cdc32f00bc162c02ad0b4e97edbd4'
ENGINE = Path('/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/Resources')
SCRIPTING = ENGINE / 'Scripting'
OPTIONS=argparse.ArgumentParser(description=__doc__)
OPTIONS.add_argument('--baseline',action='store_true')
OPTIONS.add_argument('--count',type=int,choices=(30,50),default=30)
OPTIONS.add_argument('--phases',action='store_true',help='Temporary-copy stage timing/allocation diagnosis, not an A/B speed result')
ARGS=OPTIONS.parse_args()
OUT = ROOT / '.local/movement-50' / (('step-phases-' if ARGS.phases else 'baseline-' if ARGS.baseline else 'component-')+str(ARGS.count))


def instrument(text, signature, stage):
    """Wrap one method, ignoring C# comments/quoted strings while finding its end."""
    if text.count(signature) != 1:
        raise ValueError('Ambiguous instrumentation anchor: ' + signature)
    start = text.index('{', text.index(signature) + len(signature))
    at, depth, state = start, 0, 'code'
    while at < len(text):
        ch, nxt = text[at], text[at:at+2]
        if state == 'line':
            if ch == '\n': state = 'code'
        elif state == 'block':
            if nxt == '*/': state = 'code'; at += 1
        elif state in ('"', "'"):
            if ch == '\\': at += 1
            elif ch == state: state = 'code'
        elif nxt == '//': state = 'line'; at += 1
        elif nxt == '/*': state = 'block'; at += 1
        elif ch in ('"', "'"): state = ch
        elif ch == '{': depth += 1
        elif ch == '}':
            depth -= 1
            if depth == 0:
                return text[:start+1] + '\nusing(var qaStage=MovementPhase.Begin(' + str(stage) + ')){\n' + text[start+1:at] + '}\n' + text[at:]
        at += 1
    raise ValueError('Unclosed method: ' + signature)


def main():
    if OUT.exists():
        raise RuntimeError('Refusing to overwrite evidence')
    OUT.mkdir(parents=True)
    files = sorted(p for folder in ('Runtime', 'Simulation')
                   for p in (ROOT / 'unity/Assets/Spacewars' / folder).rglob('*.cs'))
    fixture = ROOT / 'unity/Assets/Spacewars/Tests/EditMode/SmallUnitMovementProfileTests.cs'
    files.append(fixture)
    fixture_copy=OUT/'SmallUnitMovementProfileTests.cs'
    fixture_copy.write_text(fixture.read_text().replace('TestContext.WriteLine(', 'Console.WriteLine('))
    compiled = {}
    if ARGS.phases:
        stages = [
            ('Runtime/NavigationSession.cs','public void Step(double dt)',0),
            ('Runtime/NavigationOwnedMovement.cs','private bool? TryPublishOwnedMovement(double dt)',1),
            ('Runtime/NavigationSession.cs','private void StepMovementLocked(double dt,NavMotionWorkspace workspace)',2),
            ('Simulation/NavigationCrowd.cs','private void StepJoint(double dt,ref NavLocalRepairWorkspace repair,NavMotionWorkspace workspace)',3),
            ('Simulation/TerrainLocations.cs','out int blocker,bool certifiedEndpoints=false)',4),
            ('Runtime/NavigationGroupMutation.cs','private void RefreshOwnedGroupMembers()',5),
            ('Simulation/TerrainLocations.cs','private List<double> IndexedCuts(NavPoint a,NavPoint b)',6),
        ]
        for relative, signature, stage in stages:
            path=ROOT/'unity/Assets/Spacewars'/relative
            compiled[path]=instrument(compiled.get(path,path.read_text()),signature,stage)
        for path,text in list(compiled.items()):
            target=OUT/'compiled'/path.relative_to(ROOT);target.parent.mkdir(parents=True,exist_ok=True);target.write_text(text);compiled[path]=target
        fixture_copy.write_text(fixture_copy.read_text().replace('var samples=new double[120]', 'MovementPhase.Reset();var samples=new double[120]').replace('var sorted=samples.OrderBy', 'MovementPhase.Dump();var sorted=samples.OrderBy'))
        helper=OUT/'MovementPhase.cs'
        helper.write_text('''using System;using System.Diagnostics;
internal static class MovementPhase {
 static long[] calls=new long[7],ticks=new long[7],bytes=new long[7];
 internal struct Stamp:IDisposable {int stage;long start,allocation;
  internal Stamp(int s){stage=s;start=Stopwatch.GetTimestamp();allocation=GC.GetAllocatedBytesForCurrentThread();}
  public void Dispose(){calls[stage]++;ticks[stage]+=Stopwatch.GetTimestamp()-start;bytes[stage]+=GC.GetAllocatedBytesForCurrentThread()-allocation;}}
 internal static void Reset(){Array.Clear(calls,0,7);Array.Clear(ticks,0,7);Array.Clear(bytes,0,7);}
 internal static Stamp Begin(int stage)=>new Stamp(stage);
 internal static void Dump(){for(int i=0;i<7;i++){Console.WriteLine("PHASE id="+i+" calls="+calls[i]+" ms="+(ticks[i]*1000d/Stopwatch.Frequency)+" bytes="+bytes[i]);calls[i]=ticks[i]=bytes[i]=0;}}
}''')
        files.append(helper)
    harness = OUT / 'runner.cs'
    harness_text='''using System;
using Spacewars.Tests.EditMode;
public static class MovementScaleProfile {
 public static int Main() {
  try {
   foreach(int count in new[]{COUNT}) {
    var test=new SmallUnitMovementProfileTests();
    try {test.ProfileThreeCrossingsIdleAndMoving(count);Console.WriteLine("COMPONENT_PASS count="+count);}
    finally {test.Reset();}
   }
   return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
}'''
    harness.write_text(harness_text.replace('COUNT',str(ARGS.count)))
    baseline_copy=OUT/'Navigation.cs'
    if ARGS.baseline: baseline_copy.write_bytes(subprocess.check_output(['git','show',BASELINE+':unity/Assets/Spacewars/Simulation/Navigation.cs'],cwd=ROOT))
    refs = SCRIPTING / 'MonoBleedingEdge/lib/mono/unityjit-macos'
    nunit = ENGINE / 'PackageManager/BuiltInPackages/com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll'
    arguments = ['-nostdlib+', '-langversion:9', '-optimize-', '-target:exe',
                 '-define:UNITY_INCLUDE_TESTS,DEVELOPMENT_BUILD', '-out:' + str(OUT / 'runner.exe')]
    arguments += ['-r:' + str(refs / name) for name in
                  ('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Numerics.dll', 'Facades/netstandard.dll')]
    arguments += ['-r:' + str(SCRIPTING / 'Managed/Newtonsoft.Json.dll'), '-r:' + str(nunit), str(harness)]
    arguments += [str(fixture_copy if p==fixture else baseline_copy if ARGS.baseline and p.name=='Navigation.cs' else compiled.get(p,p)) for p in files]
    rsp = OUT / 'sources.rsp'
    rsp.write_text('\n'.join('"' + a + '"' for a in arguments))
    manifest = {'source': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
                'classification': 'component CPU only; unoptimised Unity Mono C#; fixed dt1/30,8warmup,120samples',
                'qaOnlyAdapter': 'TestContext.WriteLine becomes Console.WriteLine in temporary fixture copy; no runtime or assertions changed',
                'fixtureCompiledSha256': hashlib.sha256(fixture_copy.read_bytes()).hexdigest(),
                'baselineRef': BASELINE,
                'compiledNavigationSha256': hashlib.sha256((baseline_copy if ARGS.baseline else ROOT/'unity/Assets/Spacewars/Simulation/Navigation.cs').read_bytes()).hexdigest(),
                'counts': [ARGS.count], 'baselineRuntime': ARGS.baseline, 'timeoutSeconds': 180,
                'phases': ARGS.phases,
                'compiledDeltas': {str(p.relative_to(ROOT)):hashlib.sha256(v.read_bytes()).hexdigest() for p,v in compiled.items()},
                'files': {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}}
    (OUT / 'manifest.json').write_text(json.dumps(manifest, indent=2))
    with (OUT / 'compile.log').open('w') as log:
        subprocess.run([str(SCRIPTING / 'NetCoreRuntime/dotnet'), str(SCRIPTING / 'DotNetSdkRoslyn/csc.dll'), '@' + str(rsp)],
                       cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
    for dependency in (SCRIPTING / 'Managed/Newtonsoft.Json.dll', nunit):
        shutil.copy2(dependency, OUT)
    env = dict(os.environ, UNITY_RUN_MODE='exclusive', SPACEWARS_SMALL_MOVEMENT_OUTPUT=str(OUT), SPACEWARS_PRECISE_PROFILE='0')
    with (OUT / 'run.log').open('w') as log:
        result = subprocess.run([str(ROOT / 'tools/unity.sh'), 'player', str(SCRIPTING / 'MonoBleedingEdge/bin/mono'),
                                 str(OUT / 'runner.exe'), '-noaudio', '-muted'], cwd=ROOT, env=env,
                                stdout=log, stderr=subprocess.STDOUT, timeout=180)
    (OUT / 'exit.json').write_text(json.dumps({'exitCode': result.returncode}))
    result.check_returncode()
    print(OUT)


if __name__ == '__main__':
    main()
