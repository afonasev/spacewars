#!/usr/bin/env python3
"""Read scale Player frame/stage traces; timings are inclusive, never additive."""
import csv
import json
import math
from pathlib import Path
import re
import statistics
import sys


def distribution(v):
    v=sorted(x for x in v if math.isfinite(x) and x>=0)
    if not v:return None
    return dict(n=len(v),mean=statistics.mean(v),p50=v[math.ceil(.5*len(v))-1],
                p95=v[math.ceil(.95*len(v))-1],p99=v[math.ceil(.99*len(v))-1],max=v[-1])


def measured_video_identity(frames, start, seconds):
    """The startup host precedes deferred display settings; use measured rows."""
    rows = [r for r in frames if start <= r['elapsed'] < start + seconds]
    def values(*keys):
        return sorted({tuple(int(r[k]) for k in keys) for r in rows})
    return dict(dimensions=values('width', 'height'), vsync=values('vsync'),
                target_fps=values('target_fps'), rows=len(rows))


def report(path):
    path=Path(path)
    terminal=json.loads((path/'terminal.json').read_text()) if (path/'terminal.json').exists() else None
    actions=(path/'native-input.txt').read_text() if (path/'native-input.txt').exists() else ''
    match=re.search(r'^([\d.]+) .*split-scale START unitsPerSeat=(\d+) seconds=(\d+)',actions,re.M)
    if not match:return dict(status='NotRun',terminal=terminal)
    start=float(match[1]);seconds=int(match[3])
    with (path/'native-route-frames.csv').open() as f:
        frames=[{k:float(v) for k,v in r.items()} for r in csv.DictReader(l for l in f if not l.startswith('#'))]
    host=json.loads((path/'precise-render-host.json').read_text())
    frequency=host['clockFrequency']
    stamp0=next(r['stopwatch_stamp']-(r['elapsed']-start)*frequency for r in frames if r['stopwatch_stamp']>0)
    logs=max(((path/n).read_text(errors='replace') for n in ('host.log','player.log') if (path/n).exists()),key=lambda x:x.count('PRECISE_CPU'),default='')
    cpu={}
    for m in re.finditer(r'PRECISE_CPU thread=(\d+) stamp=(\d+) frequency=(\d+) stage=(\w+) calls=(\d+) wall_ms=([\d.]+) cpu_ms=([-\d.]+) max_ms=([\d.]+) attempts=(\d+)',logs):
        t,stamp,freq,stage,calls,wall,cost,maximum,attempts=m.groups()
        cpu.setdefault((t,stage),[]).append(dict(time=(int(stamp)-stamp0)/frequency,wall=float(wall),cpu=float(cost),calls=int(calls),max=float(maximum)))
    windows={}
    for low in range(0,seconds,30):
        high=min(seconds,low+30)
        rows=[r for r in frames if low<=r['elapsed']-start<high and r['wall_frame_ms']>0]
        if len(rows)<2:continue
        wall=(rows[-1]['stopwatch_stamp']-rows[0]['stopwatch_stamp'])/frequency
        vals=[r['wall_frame_ms'] for r in rows]
        tick_changes=[r['unity_realtime'] for a,r in zip(rows,rows[1:]) if a['tick']!=r['tick']]
        gpu={r['timing_stamp']:r['gpu_ms'] for r in rows if r['timing_stamp']>0 and r['gpu_ms']>0}
        stages={}
        for (thread,stage),samples in cpu.items():
            # Ten-second cumulative samples; include only intervals fully inside this window.
            pairs=[(a,b) for a,b in zip(samples,samples[1:]) if low<=a['time']<b['time']<=high]
            if not pairs:continue
            dt=sum(b['time']-a['time'] for a,b in pairs)
            stages[f'{thread}:{stage}']=dict(cpu_ms_per_s=sum(b['cpu']-a['cpu'] for a,b in pairs)/dt,
                wall_ms_per_s=sum(b['wall']-a['wall'] for a,b in pairs)/dt,
                calls_per_s=sum(b['calls']-a['calls'] for a,b in pairs)/dt,
                lifetime_max_ms=max(b['max'] for a,b in pairs))
        windows[f'{low}-{high}']=dict(actual_low=rows[0]['elapsed']-start, actual_high=rows[-1]['elapsed']-start, wall_seconds=wall, frames=len(rows),fps=1000/statistics.mean(vals),frame_ms=distribution(vals),
            frames_over_50=sum(v>50 for v in vals),frames_over_100=sum(v>100 for v in vals),
            simulation_wall_ratio=(rows[-1]['tick']-rows[0]['tick'])/30/wall,
            max_visible_tick_gap_ms=max((1000*(b-a) for a,b in zip(tick_changes,tick_changes[1:])),default=None),
            tick_first=rows[0]['tick'],tick_last=rows[-1]['tick'],
            units=[min(r['global_units'] for r in rows),max(r['global_units'] for r in rows)],
            gc0=rows[-1]['gc0_count']-rows[0]['gc0_count'],heap_mib=distribution([r['heap_bytes']/2**20 for r in rows]),
            gpu_ms=distribution(gpu.values()),
            metrics={key:distribution([r[key] for r in rows]) for key in ('main_update_ms','render_ms','hud_ms','maps_ms','route_service_ms','gc_frame_bytes','draw_calls','triangles','pending','ready','retained_bytes','queue_age','cpu_main_ms','cpu_render_ms','present_wait_ms')},
            cumulative_delta={key:rows[-1][key]-rows[0][key] for key in ('barrier_polls','barrier_builds','route_pumps','prepare_elapsed_ms','provider_elapsed_ms','solve_elapsed_ms','projection_elapsed_ms','transport_wait_ms','transport_hold_ms','publish_elapsed_ms','route_worker_allocated_bytes')},
            stages=stages)
    seats=[]
    with (path/'split-scale-seats.csv').open() as f:seats=list(csv.DictReader(f))
    motion={}
    for owner in {r['owner'] for r in seats}:
        rows=[r for r in seats if r['owner']==owner]
        motion[owner]=dict(samples=len(rows),moving_samples=sum(int(r['moving'])>0 for r in rows),
            first_units=rows[0]['units'],last_units=rows[-1]['units'],
            center_distance=sum(math.hypot(float(b['center_x'])-float(a['center_x']),float(b['center_z'])-float(a['center_z'])) for a,b in zip(rows,rows[1:])))
    completed=bool(terminal and terminal['exit_code']==0 and (terminal['completion'] or '').startswith('COMPLETE\nsplit-scale measurement completed'))
    source=json.loads((path/'source.json').read_text())
    workload_note=('Natural eight-bot production at x4 with four observer cameras; no physical-device or Windows acceptance.' if source.get('observer') else 'Diagnostic synthetic controllers; no physical-device or Windows acceptance. Fixed seeded armies isolate scale, not natural production.')
    return dict(status='measured' if completed else 'FAIL' if terminal and (terminal['completion'] or '').startswith('FAIL') else 'incomplete',measurement_complete=completed,host=host,source=source,
        start=start,terminal=terminal,windows=windows,motion=motion,
        measured_video=measured_video_identity(frames,start,seconds),
        notes=['CPU stages inclusive and per-thread; never sum parents and children. CPU intervals sampled every10s; startup excluded.',
               'Use measured_video for comparison identity; host width/height and frame cap precede deferred display settings.',
               'render/hud/maps columns cover first seat; main_update includes children. GPU deduplicated by timing stamp.',
               workload_note])


if __name__=='__main__':
    result=report(sys.argv[1]);print(json.dumps(result,indent=2))
