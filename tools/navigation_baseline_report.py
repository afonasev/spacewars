#!/usr/bin/env python3
"""Validate the exact A0 observation matrix; future gates remain separate."""
import argparse
import json
from pathlib import Path

VARIANTS = [('S01', v, 1) for v in ('direct', 'bent', 'unreachable')] + [('S02', 'mixed', n) for n in (2, 10, 25, 50)] + [('S04', v, 50) for v in ('narrow-flat', 'wide-flat', 'authored-bridge')] + [('S08', 'occupied', 100), ('S18', 'replace-restart', 50)]
EXPECTED = {(s, v, n, r) for s, v, n in VARIANTS for r in range(3)}

def summarize(directory):
    rows = [json.loads(p.read_text()) for p in sorted(Path(directory).glob('S*.json'))]
    identities = [(r['scenario'], r['variant'], r['count'], r['repetition']) for r in rows]
    if len(identities) != len(set(identities)) or set(identities) != EXPECTED:
        raise ValueError('Missing, duplicate or unexpected baseline observations')
    for row in rows:
        if row['tickHz'] != 30 or row['seed'] != 19092026 or row['ticks'] <= 0 or row['pending'] != 0:
            raise ValueError('Invalid baseline tick/seed or incomplete initial service')
        if row['scenario'] == 'S18' and (row['moving'] or row['rejected'] <= 0):
            raise ValueError('Cancellation observation failed')
        for field in ('routeMs', 'routeCallP95Ms', 'routeCallMaxMs', 'tickP95Ms', 'tickP99Ms', 'tickMaxMs', 'processCpuMs'):
            if not isinstance(row[field], (int, float)) or not 0 <= row[field] < float('inf'):
                raise ValueError('Invalid metric ' + field)
    result = []
    for scenario, variant, count in VARIANTS:
        group = [r for r in rows if (r['scenario'], r['variant'], r['count']) == (scenario, variant, count)]
        result.append({'scenario': scenario, 'variant': variant, 'count': count,
                       'outcomes': [{k: r[k] for k in ('repetition', 'ticks', 'arrived', 'blocked', 'unreachable', 'moving', 'rejected')} for r in group],
                       'worst': {k: max(r[k] for r in group) for k in ('routeMs', 'routeCallP95Ms', 'routeCallMaxMs', 'tickP95Ms', 'tickP99Ms', 'tickMaxMs', 'processCpuMs', 'repairs', 'peakQueue', 'flowBuilds', 'flowHits', 'flowCells', 'peakFlowCacheFields', 'peakFlowCacheCells', 'retainedManagedBytes')}})
    return {'matrix': '36/36 observations', 'future_requirements_passed': False, 'scenarios': result}

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    print(json.dumps(summarize(args.directory), indent=2))
