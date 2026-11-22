import gzip
import json
from pathlib import Path
import sqlite3
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import MagicMock, patch

import overnight_domination as overnight


class OvernightDominationTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.db = sqlite3.connect(':memory:')
        self.addCleanup(self.db.close)
        self.jobs = overnight.matrix()[:2]
        overnight.initialize(self.db, {'Schema': 3}, self.jobs)
        self.budget = overnight.StorageBudget(
            self.root, SimpleNamespace(min_free_gib=0, max_output_gib=1, max_match_mib=32))

    def test_collects_legacy_and_live_gzip_events_without_recompressing(self):
        case = self.root / 'batches/0001/0001'
        session = case / 'TestLogs/session'
        match = session / 'match-1'
        match.mkdir(parents=True)
        for path, row in (
            (case / 'result.json', {'Status': 'horizon', 'Tick': 36000}),
            (match / 'manifest.json', {}),
            (match / 'summary.json', {'Status': 'complete', 'Written': 2, 'Dropped': 0}),
            (session / 'session-end.json', {'RecorderFailure': False}),
        ):
            path.write_text(json.dumps(row))
        raw = match / 'events-0001.jsonl'
        raw.write_bytes(b'{"Type":"ai-receipt"}\n')
        compressed = match / 'events-0002.jsonl.gz'
        compressed.write_bytes(gzip.compress(b'{"Type":"match-end"}\n'))
        expected_raw = raw.read_bytes()
        expected_compressed = compressed.read_bytes()

        overnight.collect(self.db, self.root, case, self.budget)

        self.assertEqual(self.db.execute('SELECT id FROM recorded_matches').fetchall(), [(1,)])
        indexed = dict(self.db.execute('SELECT name, path FROM files'))
        self.assertEqual((self.root / indexed[str(compressed.relative_to(case))]).read_bytes(),
                         expected_compressed)
        self.assertEqual(gzip.decompress((self.root / indexed[str(raw.relative_to(case))]).read_bytes()),
                         expected_raw)
        self.assertFalse(raw.exists())
        self.assertFalse(compressed.exists())
        self.assertTrue((case / 'collected').exists())

    def test_truncated_live_gzip_is_reported(self):
        path = self.root / 'events-0001.jsonl.gz'
        path.write_bytes(gzip.compress(b'{"Type":"match-end"}\n')[:-8])
        record, problem = list(overnight.records([path]))[-1]
        self.assertIsNone(record)
        self.assertIn('end-of-stream', problem)

    def test_drift_blocks_next_match_before_releasing_collector_ack(self):
        process = MagicMock()
        process.returncode = 0
        process.poll.side_effect = [None, 0]
        args = SimpleNamespace(exclusive=False, stall_seconds=90, startup_minutes=40)

        def launch(_, **options):
            batch = Path(options['env']['SPACEWARS_OVERNIGHT_BATCH'])
            case = batch / '0001'
            case.mkdir()
            (case / 'result.json').write_text('{"Status":"horizon","Tick":36000}')
            return process

        acknowledge = overnight.acknowledge_collection

        def verify_stop(db, root, case, key):
            self.assertTrue((case.parent / 'stop').exists())
            acknowledge(db, root, case, key)

        with (self.root / '.lock').open('wb') as lock, \
                patch.object(overnight.subprocess, 'Popen', side_effect=launch), \
                patch.object(overnight, 'source_changed', return_value=True), \
                patch.object(overnight, 'test_passed', return_value=True), \
                patch.object(overnight, 'acknowledge_collection', side_effect=verify_stop), \
                patch.object(overnight, 'ProjectSettingsGuard') as settings:
            settings.return_value.finish.return_value = {'unchanged': True}
            result = overnight.run_batch(self.db, self.root, {'Revision': 'original'}, self.jobs,
                                         args, None, self.budget, lock.fileno(), MagicMock())

        self.assertEqual(result, ('source-drift', False))
        self.assertEqual(self.db.execute('SELECT status FROM matches ORDER BY id').fetchall(),
                         [('horizon',), ('pending',)])
