import tempfile
import unittest
from pathlib import Path
import zipfile
from test_timing_report import read_reports, summarize


class TimingReportTests(unittest.TestCase):
    XML = b'''<test-run result="Passed" duration="9"><test-suite duration="9">
      <test-suite duration="9"><test-case fullname="A.One(1.2)" duration="4" result="Passed"/>
      <test-case fullname="B.Two" duration="2" result="Passed"/></test-suite></test-suite></test-run>'''

    def test_counts_only_leaf_cases_and_ranks_fixtures(self):
        report = summarize(self.XML, 'fixture')
        self.assertEqual(2, report['case_count'])
        self.assertEqual(6, report['case_seconds'])
        self.assertEqual(9, report['nunit_seconds'])
        self.assertEqual([{'name': 'A', 'seconds': 4}, {'name': 'B', 'seconds': 2}], report['fixtures'])

    def test_zip_reads_without_extracting_or_launching(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'raw.zip'
            with zipfile.ZipFile(path, 'w') as archive:
                archive.writestr('run/editmode.xml', self.XML)
                archive.writestr('run/editor.log', 'unused')
            self.assertEqual(1, len(read_reports(path)))
            self.assertEqual([path], list(Path(directory).iterdir()))

    def test_rejects_non_nunit_xml(self):
        with self.assertRaises(ValueError):
            summarize(b'<settings/>', 'fixture')
