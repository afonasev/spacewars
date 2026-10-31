import csv
import tempfile
import unittest
from pathlib import Path
from qa.arrival_lifecycle import validate, COLUMNS
import route_performance_qa as driver


class ArrivalLifecycleTests(unittest.TestCase):
    def fixture(self, path, extra=None, dropped=0):
        rows=[]
        for i in range(1,9):
            row={key:0 for key in COLUMNS};row.update(kind='registration', generation=1, sequence=i, reason='registered');rows.append(row)
        if extra:
            row = dict(rows[0]);row.update(kind='advance', group=2, attempt=3, reason='source-owner-refused', state_known=1, proposed_total=16);row.update(extra);rows.append(row)
        with path.open('w') as f:
            writer = csv.DictWriter(f, fieldnames=rows[0]);writer.writeheader();writer.writerows(rows)
            f.write(f'# schema=1,rows={len(rows)},dropped={dropped},expected=8,filtered=0,preselection_unattributed=2,preselection_refused=1,observer_ticks=1,frequency=100,limit=8192\n')

    def test_discarded_proposal_is_valid_data_not_committed_progress(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d)/'a.csv';self.fixture(p, {'committed': 0});self.assertTrue(validate(p))
            self.fixture(p, {'committed': 1});self.assertFalse(validate(p))
            self.fixture(p, {'committed': 1, 'reason': 'committed'});self.assertTrue(validate(p))
            self.fixture(p, {'committed': -1, 'reason': 'exception'});self.assertTrue(validate(p))
            self.fixture(p, {'committed': -1, 'reason': 'scope-refused'});self.assertFalse(validate(p))

    def test_loss_missing_and_foreign_binding_fail_collection(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'a.csv';self.assertFalse(validate(p));self.fixture(p, dropped=1);self.assertFalse(validate(p))
            self.fixture(p, {'sequence': 999});self.assertFalse(validate(p));self.fixture(p);self.assertTrue(validate(p));p.write_text(p.read_text().replace('rows=8','rows=9'));self.assertFalse(validate(p))

    def test_flag_requires_q10_before_process_creation(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(ValueError):driver.run(Path(d)/'run',7,arrival_lifecycle_readouts=True)
            self.assertFalse(driver.evidence_complete(Path(d), 'eight-player-400-moving', arrival_lifecycle_readouts=True))
