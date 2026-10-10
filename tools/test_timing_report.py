#!/usr/bin/env python3
"""Rank saved NUnit case durations, including XML inside evidence ZIPs; never launch Unity."""
import argparse
from collections import defaultdict
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile


def summarize(data, source):
    root = ET.fromstring(data)
    if root.tag != 'test-run':
        raise ValueError('Expected NUnit test-run: ' + source)
    cases = []
    fixtures = defaultdict(float)
    for case in root.findall('.//test-case'):
        name = case.get('fullname', case.get('name', ''))
        seconds = float(case.get('duration', '0'))
        # Parameter strings can contain dots; remove them before finding the class.
        fixture = case.get('classname') or name.split('(', 1)[0].rsplit('.', 1)[0]
        fixtures[fixture] += seconds
        cases.append({'name': name, 'seconds': seconds, 'result': case.get('result')})
    duration = float(root.get('duration', '0'))
    return {'source': source, 'result': root.get('result'), 'case_count': len(cases),
            'nunit_seconds': duration, 'case_seconds': sum(c['seconds'] for c in cases),
            'fixtures': [{'name': name, 'seconds': seconds} for name, seconds in
                         sorted(fixtures.items(), key=lambda item: item[1], reverse=True)],
            'cases': sorted(cases, key=lambda case: case['seconds'], reverse=True)}


def read_reports(path):
    if path.suffix == '.zip':
        with zipfile.ZipFile(path) as archive:
            return [summarize(archive.read(name), str(path) + '!' + name)
                    for name in sorted(archive.namelist()) if name.endswith('.xml')]
    return [summarize(path.read_bytes(), str(path))]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('paths', type=Path, nargs='+')
    parser.add_argument('--top', type=int, default=10)
    args = parser.parse_args()
    if args.top < 1:
        parser.error('--top must be positive')
    reports = []
    for path in args.paths:
        for report in read_reports(path):
            report['fixtures'] = report['fixtures'][:args.top]
            report['cases'] = report['cases'][:args.top]
            reports.append(report)
    print(json.dumps({'note': 'NUnit duration excludes host admission and Editor startup/import/shutdown. '
                      'Case sums exclude suite setup/teardown; parallel cases may overlap. '
                      'Historical timings are not a controlled benchmark.', 'runs': reports}, indent=2))


if __name__ == '__main__':
    main()
