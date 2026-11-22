#!/usr/bin/env python3
"""Bounded, silent exclusive four-participant scaling measurements (no build)."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
from route_performance_qa import close_owned_player
from native_plugins import validate_macos_recast

STAGES = ('AirMotion,AirMotionLaw,AirMotionInputs,AirMotionAdoption,CrowdRepair,HudChat,HudResources,HudSpectator,DomainPrelude,DomainMotionExtras,Economy,Queues,Routes,Artillery,SnapshotTrails,WreckGeometry,WreckReconcile,WreckReachability,WreckRebind,Authority,Domain,Ai,AiProjection,AiReview,Movement,Firing,Projectiles,Contacts,Vision,PlayerView,'
          'Snapshot,SnapshotUnits,SnapshotBuildings,SnapshotPublic,SnapshotRequests,SnapshotEnvelope,SnapshotPayload,RouteWorker,PhysicalSolve,ContactDetect,ContactPublish,HostFork,HostSeal,HostPublish,'
          'Presentation,WorldFog,WorldSites,WorldUnits,WorldAnimation,WorldBuildings,WorldWrecks,'
          'WorldTrails,WorldProjectiles,JournalCapture,ParticipantViews,RuntimePublish,'
          'FormationGuide,CrowdPreparation,CrowdExecution,CrowdCertificates,CrowdNeighbors,'
          'CrowdScale,CrowdSelect,CrowdYield,CrowdJoint,PhysicalProjection,PhysicalArrival,'
          'NavMarch,NavTraffic,NavFrameObserve,NavFrameCreate,NavOwnedMotion,NavResultMotion,HumanDestination,'
          'NavMarchComplete,NavAdmissionProjection,NavMotionPrepare,NavMotionValidate,NavMotionRelease')


def run(output, units, seconds, ai=False, width=1920, height=1080, profile=True, player_app=None, fullscreen=False, native_baseline=False, observer=False):
    output = Path(output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    app=Path(player_app).resolve() if player_app else ROOT/'unity/Builds/macOS/SpacewarsPlayable.app'
    executable=app/'Contents/MacOS/Spacewars'
    if not executable.is_file():
        raise FileNotFoundError(executable)
    validate_macos_recast(executable.parents[1]/'Plugins/libspacewars_recast.dylib')
    command = [str(ROOT/'tools/unity.sh'), 'player', str(executable), '-noaudio', '-muted',
               '-matchSeed', '7', '-playableEvidence', str(output), '-routePerformanceEvidence',
               '-splitScreenPerformanceEvidence', '-splitScaleUnits', str(units),
               '-splitScaleSeconds', str(seconds), '-screen-fullscreen', '1' if fullscreen else '0',
               '-screen-width', str(width), '-screen-height', str(height),
               '-qaDeadlineFile', str(output/'qa-deadline.txt'), '-logFile', str(output/'player.log')]
    if observer:
        begin=command.index("-splitScreenPerformanceEvidence");end=command.index("-screen-fullscreen")
        command[begin:end]=["-observerPerformanceEvidence","-observerSeconds",str(seconds)]
    if ai:
        command.append('-splitScaleAi')
    if native_baseline:
        command.append('-splitScaleNativeBaseline')
    env = dict(os.environ, UNITY_RUN_MODE='exclusive', SPACEWARS_PRECISE_PROFILE='1' if profile else '0',
               SPACEWARS_PROFILE_STAGES=STAGES, SPACEWARS_FRAME_TIMING_ONLY='1')
    (output/'command.json').write_text(json.dumps(command, indent=2)+'\n')
    (output/'source.json').write_text(json.dumps(dict(
        commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
        build_source=json.loads(Path(str(executable.parents[2])+'.source.json').read_text()),
        units_per_seat=1 if observer else units, seconds=seconds, ai=True if observer else ai, width=width, height=height,
        cpu_profile=profile, observer=observer, stages=STAGES, runner_mode='exclusive', fullscreen=fullscreen, native_baseline=native_baseline),indent=2)+'\n')
    host = output/'host.log'
    admitted = None
    child = None
    reason = 'autonomous exit'
    with host.open('w') as log, (output/'process.csv').open('w') as process_log:
        process_log.write('wall_seconds,pid,pcpu,rss_kib\n')
        process = subprocess.Popen(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,env=env)
        queued = time.monotonic()
        next_sample = 0
        try:
            while process.poll() is None:
                now = time.monotonic()
                if admitted is None:
                    match = re.search(r'started exclusive; guardian=\d+ pid=(\d+)',host.read_text())
                    if match:
                        child = int(match[1]); admitted = now
                        finish = int(time.time()*1000)+(seconds+120)*1000
                        temporary=output/'qa-deadline.tmp'
                        temporary.write_text(f'{finish}\n{finish+15000}\n')
                        temporary.replace(output/'qa-deadline.txt')
                    elif now-queued>180:
                        reason='NotRun admission timeout';process.terminate();break
                else:
                    if now-admitted>seconds+150:
                        reason='FAIL owned watchdog deadline';close_owned_player(child,executable,output);break
                    if now>=next_sample:
                        next_sample=now+5
                        stats=subprocess.run(['ps','-p',str(child),'-o','pid=,%cpu=,rss='],capture_output=True,text=True).stdout.split()
                        if len(stats)==3:
                            process_log.write(f'{now-admitted:.3f},'+','.join(stats)+'\n');process_log.flush()
                time.sleep(.25)
        finally:
            if process.poll() is None:
                if child is not None:
                    close_owned_player(child,executable,output)
                try:
                    process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    process.terminate();process.wait(timeout=10)
    completion = output/'qa-completion.txt'
    result = dict(exit_code=process.returncode, reason=reason, child=child,
                  completion=completion.read_text() if completion.is_file() else None,
                  admission_seconds=None if admitted is None else admitted-queued)
    (output/'terminal.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result),flush=True)
    return result


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('output',type=Path)
    p.add_argument('--units',type=int,choices=[5,10,20],default=20)
    p.add_argument('--observer',action='store_true',help='Natural eight bots, four observer cameras at x4')
    p.add_argument('--seconds',type=int,default=240)
    p.add_argument('--ai',action='store_true')
    p.add_argument('--player-app',type=Path,help='Frozen diagnostic app with adjacent .source.json manifest')
    p.add_argument('--width',type=int,default=1920)
    p.add_argument('--height',type=int,default=1080)
    p.add_argument('--fullscreen',action='store_true',help='Force fullscreen to match a native-size baseline without window-frame clipping')
    p.add_argument('--native-baseline',action='store_true',help='Use the QA native FullScreenWindow path with pre-START display identity checks')
    p.add_argument('--no-cpu-profile',action='store_true')
    a=p.parse_args()
    if not 60<=a.seconds<=360 or min(a.width,a.height)<480:
        p.error('seconds must be 60..360; dimensions at least 480')
    r=run(a.output,a.units,a.seconds,a.ai,a.width,a.height,not a.no_cpu_profile,a.player_app,a.fullscreen,a.native_baseline,a.observer)
    sys.exit(0 if r['exit_code']==0 and (r['completion'] or '').startswith('COMPLETE\nsplit-scale measurement completed') else 1)
