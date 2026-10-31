#!/usr/bin/env python3
"""Read the ordinary four-human benchmark's existing native timing evidence."""
import argparse
import csv
import json
import math
import re
from pathlib import Path


def distribution(values, milliseconds=True):
    values = sorted(values)
    if not values:
        return None
    def percentile(p):
        return values[max(0, math.ceil(p * len(values)) - 1)]
    result = dict(samples=len(values), median=percentile(.5), p95=percentile(.95), p99=percentile(.99), max=values[-1])
    if milliseconds:
        result["over_33_3"] = sum(v > 33.3 for v in values)
    return result


def report(directory):
    directory = Path(directory)
    action_path=directory/'native-input.txt'
    if not action_path.is_file():
        terminal_path=directory/'terminal.json'
        return {'status':'NotRun','reason':'Player did not reach evidence bootstrap',
                'terminal':json.loads(terminal_path.read_text()) if terminal_path.is_file() else None}
    actions = action_path.read_text()
    match = re.search(r'^([\d.]+) .*split-screen START .*logging=(.*?) syntheticPads=true', actions, re.M)
    if not match:
        return {'status': 'NotRun', 'reason': 'No valid four-seat measurement start'}
    start = float(match[1])
    with (directory/'native-route-frames.csv').open() as source:
        frames = list(csv.DictReader(line for line in source if not line.startswith('#')))
    with (directory/'split-screen-seats.csv').open() as source:
        seats = list(csv.DictReader(source))
    host = json.loads((directory/'precise-render-host.json').read_text())
    frequency = host['clockFrequency']
    windows = {}
    for name, phase, low, high in [('separated', 1, 15, 45), ('overlapping', 2, 45, 75), ('combat', 3, 75, 90)]:
        rows = [r for r in frames if low <= float(r['elapsed'])-start < high]
        owner_rows = [r for r in seats if int(r['phase']) == phase]
        result = {}
        seen_timing = set()
        gpu = []
        for row in rows:
            stamp = int(row['timing_stamp'])
            if stamp > 0 and stamp not in seen_timing:
                seen_timing.add(stamp)
                if float(row['gpu_ms']) > 0:
                    gpu.append(float(row['gpu_ms']))
        for column in ['wall_frame_ms','main_update_ms','render_ms','hud_ms','maps_ms','gpu_ms','cpu_main_ms','cpu_render_ms']:
            result[column] = distribution(gpu if column == 'gpu_ms' else [float(r[column]) for r in rows if float(r[column]) >= 0])
        result['gc_frame_bytes'] = distribution([int(r['gc_frame_bytes']) for r in rows if int(r['gc_frame_bytes']) >= 0], milliseconds=False)
        result['base_200_every_sample'] = bool(owner_rows) and all(int(r['units']) == 50 for r in owner_rows)
        result['owner_counts'] = {owner: [min(int(r['units']) for r in owner_rows if r['owner']==owner), max(int(r['units']) for r in owner_rows if r['owner']==owner)] for owner in {r['owner'] for r in owner_rows}}
        if len(rows)>1:
            duration = (int(rows[-1]['stopwatch_stamp'])-int(rows[0]['stopwatch_stamp']))/frequency
            result['simulation_wall_ratio'] = (int(rows[-1]['tick'])-int(rows[0]['tick']))/30/duration if duration>0 else None
            result['main_allocated_bytes_per_second'] = (int(rows[-1]['main_allocated_bytes'])-int(rows[0]['main_allocated_bytes']))/duration if duration>0 else None
            result['gc0_collections'] = int(rows[-1]['gc0_count'])-int(rows[0]['gc0_count'])
        if rows and all(int(r['main_allocated_bytes']) == 0 for r in rows):
            result['main_allocated_bytes_per_second'] = None
        result['shadow_ms'] = distribution([float(r['shadow_ms']) for r in owner_rows])
        result['shadow_renderers'] = {owner: distribution([int(r['shadow_renderers']) for r in owner_rows if r['owner']==owner], milliseconds=False) for owner in {r['owner'] for r in owner_rows}}
        windows[name] = result
    owners = {}
    for owner in {r['owner'] for r in seats}:
        rows = [r for r in seats if r['owner']==owner]
        owners[owner] = dict(center_displacement=math.hypot(float(rows[-1]['center_x'])-float(rows[0]['center_x']),float(rows[-1]['center_z'])-float(rows[0]['center_z'])),health_first=int(rows[0]['health']),health_last=int(rows[-1]['health']),moving_samples=sum(int(r['moving'])>0 for r in rows))
    return {'status':'measured','measurement_start_native_elapsed':start,'windows':windows,'owners':owners,
            'test_logs':match[2], 'notes':['render/hud/maps_ms are first seat only; main_update_ms includes all children','GPU unavailable values omitted; positive timing stamps deduplicated; Mono constant0 allocation counter is unavailable','Timing includes screenshot tails; nearest-rank percentiles; uncapped pacing asserted separately','Movement centers and health totals require journal review if units disappear; no physical device acceptance inferred']}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    print(json.dumps(report(args.directory), indent=2))
