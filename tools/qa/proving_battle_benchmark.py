"""Run a single muted, exclusive proving-ground sample and summarize its frame trace."""
import argparse
import csv
import json
import math
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
STAGES = ('Authority,Domain,Movement,Firing,Projectiles,Contacts,Vision,PlayerView,'
          'Snapshot,RouteWorker,Presentation,WorldFog,WorldUnits,WorldAnimation,'
          'WorldProjectiles,WorldWrecks,ContactDetect,ContactPublish,PhysicalSolve')


def window(rows):
    if not rows:
        return None
    frames = sorted(r['wall_frame_ms'] for r in rows)
    pct = lambda p: frames[math.ceil(len(frames)*p)-1]
    elapsed = sum(frames)
    # Consecutive wall samples between visible tick changes include host and display latency.
    changes = [r['unity_realtime'] for prev, r in zip(rows, rows[1:]) if prev['tick'] != r['tick']]
    gaps = sorted(1000*(b-a) for a, b in zip(changes, changes[1:]))
    return dict(frames=len(frames), fps=1000*len(frames)/elapsed,
                p50_ms=pct(.50), p95_ms=pct(.95), p99_ms=pct(.99), max_ms=frames[-1],
                over50=sum(v>50 for v in frames), over100=sum(v>100 for v in frames),
                max_visible_tick_gap_ms=max(gaps, default=None),
                ticks_per_second=1000*(rows[-1]['tick']-rows[0]['tick'])/elapsed,
                mean_update_ms=sum(r['main_update_ms'] for r in rows)/len(rows),
                mean_render_ms=sum(r['render_ms'] for r in rows)/len(rows),
                mean_route_service_ms=sum(r['route_service_ms'] for r in rows)/len(rows),
                gc_collections=rows[-1]['gc0_count']-rows[0]['gc0_count'])


def summarize(output):
    battle = json.loads((output/'battle.json').read_text())
    with (output/'native-route-frames.csv').open() as src:
        reader = csv.DictReader(line for line in src if not line.startswith('#'))
        rows = [{k: float(v) for k, v in row.items()} for row in reader
                if battle['startFrame'] < int(row['render_frame']) <= battle['endFrame']
                and int(row['generation']) == battle['Generation']]
    if not rows or any(r['wall_frame_ms'] <= 0 for r in rows):
        raise ValueError('Missing wall-frame samples')
    if len(rows) != battle['endFrame']-battle['startFrame']:
        raise ValueError('Incomplete battle frame trace')
    start = battle.get('startRealtime', rows[0]['unity_realtime']-rows[0]['wall_frame_ms']/1000)
    return dict(battle=battle, full=window(rows),
                after_opening=window([r for r in rows if r['unity_realtime'] > start+2]),
                opening_5s=window([r for r in rows if r['unity_realtime'] <= start+5]),
                dense=window([r for r in rows if r['global_units'] >= 1.5*battle['count']]))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('lineup', choices=['mixed10','mixed','Tank','Explorer','Hawk','Shkval'])
    parser.add_argument('output', type=Path)
    parser.add_argument('--seconds', type=int, default=30, help='Measured wall time (1–120 s)')
    parser.add_argument('--width', type=int, default=1280)
    parser.add_argument('--height', type=int, default=720)
    parser.add_argument('--stock-probe', type=Path, help='Executable of a release copy with the QA observer registered')
    parser.add_argument('--count', type=int, choices=[10, 20, 40], help='Units per side (defaults to original lineup)')
    parser.add_argument('--no-wrecks', action='store_true', help='Benchmark without retained wrecks')
    parser.add_argument('--tick-timing', action='store_true', help='Write phase times for each completed simulation tick')
    parser.add_argument('--salvo-timing', action='store_true', help='Log per-tick rocket and wreck costs (ms) as SALVO rows in a Development Player')
    parser.add_argument('--summary-only', action='store_true')
    args = parser.parse_args()
    output = args.output.resolve()
    if not args.summary_only:
        output.mkdir(parents=True, exist_ok=False)
        app = ROOT/'unity/Builds/macOS/SpacewarsPlayable.app/Contents/MacOS'
        if args.stock_probe:
            exe = args.stock_probe.resolve(strict=True)
        else:
            exe, = (p for p in app.iterdir() if p.is_file() and os.access(p,os.X_OK))
        env = dict(os.environ, UNITY_RUN_MODE='exclusive', SPACEWARS_PRECISE_PROFILE='1',
                   SPACEWARS_PROFILE_STAGES=STAGES)
        if args.tick_timing:
            env['SPACEWARS_BATTLE_TICK_TIMING'] = '1'
        if args.salvo_timing or args.tick_timing:
            env['SPACEWARS_SALVO_TIMING'] = '1'
        recast = ROOT/'unity/Assets/Spacewars/Plugins/macOS'
        subprocess.run([ROOT/'tools/unity.sh','player','/usr/bin/env',
                        'DYLD_LIBRARY_PATH='+str(recast),exe,'-noaudio','-muted',
                        '-screen-fullscreen','0','-screen-width',str(args.width),'-screen-height',str(args.height),
                        *(['-lobbyManual','-lobbyEvidence',str(output)] if args.stock_probe else []),
                        '-playableEvidence',output,'-battleBenchmark',args.lineup,'-battleSeconds',str(args.seconds),
                        *(['-battleCount',str(args.count)] if args.count else []),
                        *(['-battleNoWrecks'] if args.no_wrecks else []),
                        '-logFile',output/'player.log'], cwd=ROOT, env=env, check=True)
    summary = summarize(output)
    if not args.summary_only:
        battle = summary['battle']
        expected = args.count or (10 if args.lineup == 'mixed10' else 20)
        if battle['count'] != expected or battle['lineup'] != args.lineup or bool(battle.get('noWrecks')) != args.no_wrecks:
            raise ValueError('Player did not run the requested battle configuration')
        if args.no_wrecks and battle['peakVisibleWrecks'] != 0:
            raise ValueError('Unexpected wreck in the no-wreck control')
    (output/'summary.json').write_text(json.dumps(summary, indent=2)+'\n')
    print(json.dumps(summary['full']))


if __name__ == '__main__':
    main()
