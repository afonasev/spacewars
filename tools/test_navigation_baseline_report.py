import json
import tempfile
import unittest
from pathlib import Path
from navigation_baseline_report import EXPECTED, summarize

class BaselineMatrixTests(unittest.TestCase):
    def test_missing_observation_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaisesRegex(ValueError, 'Missing'):
                summarize(root)

    def test_complete_observations_do_not_imply_future_acceptance(self):
        with tempfile.TemporaryDirectory() as root:
            for s, v, n, repetition in EXPECTED:
                row = dict(scenario=s, variant=v, count=n, repetition=repetition, tickHz=30, seed=19092026, ticks=1, pending=0,
                           arrived=0, blocked=0, unreachable=0, moving=0, rejected=1)
                for k in ('routeMs','routeCallP95Ms','routeCallMaxMs','tickP95Ms','tickP99Ms','tickMaxMs','processCpuMs','repairs','peakQueue','flowBuilds','flowHits','flowCells','peakFlowCacheFields','peakFlowCacheCells','retainedManagedBytes'):
                    row[k] = 0
                Path(root, f'{s}-{v}-{n}-{repetition}.json').write_text(json.dumps(row))
            self.assertFalse(summarize(root)['future_requirements_passed'])
            path = next(Path(root).glob('S*.json'))
            row = json.loads(path.read_text());row['tickHz'] = 60;path.write_text(json.dumps(row))
            with self.assertRaisesRegex(ValueError, 'tick/seed'):
                summarize(root)
