#!/usr/bin/env python3
"""Explicit native QA routes. Default UI; full is explicit; see .agents/references/qa-scope.md."""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tempfile
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
# Expected case multiplicities are a gate contract, not inferred from a possibly
# broken discovery result. Update only after reviewing a deliberate test change.
UI_METHODS = {
    'ExistingActionsAreGroupedAndFocusable': 1,
    'ModalReflectsExistingSnapshotState': 3,
    'TerminalFocusCyclesOnlyBetweenRestartAndExit': 2,
    'TerminalKeyEventsStayInModalAndSubmitRestart': 2,
    'TerminalExitButtonRequestsNormalPlayerExit': 1,
    'EscapeFromPauseResumesAndReturnsFieldFocus': 1,
    'ResumeClosesModalAndReturnsFocusToField': 1,
    'ResponsiveLayoutKeepsTheSameActionsAndFocus': 2,
    'HudFocusCanBeEnteredAndLeftWithoutChangingGameActions': 1,
    'NarrowViewportKeepsVisibleActionBoundsInsidePanel': 1,
    'PopulatedResearchStaysBesideMapWithoutCoveringHeaderAt800': 1,
    'RestartedSnapshotReleasesTerminalScopeBeforeWorldEscape': 1,
    'TerminalResultsFitAndKeepFocusAtCheckedViewports': 1,
}
# Only reviewed presentation fixtures may join the UI gate. Keep multiplicities
# explicit so an accidentally narrowed discovery cannot make this route green.
UI_AFFECTED_METHODS = {
    'PlayableBattleHudTests': {
        'ArmyPopoverUsesWholeOwnLivingArmyAndFocus': 1,
        'RingKeepsRepeatWhenPurchaseUnavailableAndBlocksStaleAction': 1,
        'CircularActionsStayInsideCompactViewport': 1,
        'PaintedSectorsHaveExclusivePointerTargets': 1,
        'PrecisionPanelsRenderPopulatedQueuesAtBothViewports': 1,
        'ApprovedIconsKeepSemanticMappingsAndQueueStates': 1,
        'ApprovedIconsRenderAtHudSizes': 1,
        'StartingCameraAndZoomKeepUnitsClose': 1,
    },
    'PlayableProductionHudTests': {
        'ShkvalQueueShowsActualKindAndPause': 1,
        'ExplorerQueueShowsKindProgressAndPausedControl': 1,
        'SixControlsKeepIdentityWhileOrdersShiftAndPauseDisablesActions': 1,
    },
    'PlayableScienceHudTests': {
        'BlockedActionRemainsFocusableAndExplainsRequirementWithoutCommand': 1,
        'ProgressIsCancellableButPauseSaleAndEnemyAreNotInteractive': 1,
        'CompletedRefinerySwapsOnlyModelAndUsesAuthoredTurbineAxis': 1,
        'ScienceCenterShowsSixResearchSlotsWithActiveAndWaitingOrders': 1,
    },
    'PlayableBuildingLifecycleHudTests': {
        'ConfirmationResetsOnSelectionAvailabilityFocusAndPause': 1,
        'WaitingRepairRemainsCancellableAndSaleShowsConsequences': 1,
    },
}


def validate_results(path, fixtures=(), ui=False):
    root = ET.parse(path).getroot()
    cases = root.findall('.//test-case')
    if root.tag != 'test-run' or root.get('result') != 'Passed' or not cases:
        raise ValueError('Requires a nonempty Passed NUnit test-run')
    names = [case.get('fullname', '') for case in cases]
    if len(set(names)) != len(names) or any(not name for name in names):
        raise ValueError('Missing or duplicate test identity')
    if any(case.get('result') != 'Passed' for case in cases):
        raise ValueError('Failed/skipped/inconclusive cases cannot pass a gate')
    for key in ('failed', 'skipped', 'inconclusive'):
        if int(root.get(key, '0')) != 0:
            raise ValueError('Nonzero ' + key)
    if int(root.get('total', '-1')) != len(cases) or int(root.get('passed', '-1')) != len(cases):
        raise ValueError('NUnit totals do not match executed cases')
    methods = [name.split('(', 1)[0] for name in names]
    for fixture in fixtures:
        if not any(name.rsplit('.', 1)[0] == fixture for name in methods):
            raise ValueError('Missing fixture: ' + fixture)
    if fixtures and any(name.rsplit('.', 1)[0] not in fixtures for name in methods):
        raise ValueError('Unexpected fixture in focused result')
    if ui:
        contracts = {'PlayableUiShellTests': UI_METHODS, **UI_AFFECTED_METHODS}
        selected = fixtures or ('PlayableUiShellTests',)
        if 'PlayableUiShellTests' not in selected or any(f not in contracts for f in selected):
            raise ValueError('UI requires shell and approved affected HUD fixtures')
        expected = Counter({fixture + '.' + method: count
                            for fixture in selected for method, count in contracts[fixture].items()})
        if Counter(methods) != expected:
            raise ValueError('Incomplete or changed UI test contract')
    return {'passed': len(cases), 'test_duration_seconds': float(root.get('duration', '0')), 'tests': names}


def plan(scope, platform=None, fixtures=(), affected=()):
    if affected and scope != 'ui':
        raise ValueError('--affected-fixture is only allowed with --scope ui')
    if scope == 'ui':
        if len(set(affected)) != len(affected) or any(f not in UI_AFFECTED_METHODS for f in affected):
            raise ValueError('Select distinct exact approved affected HUD fixtures')
        return [('PlayMode', ('PlayableUiShellTests', *affected))]
    if scope == 'focused':
        if platform not in ('EditMode', 'PlayMode') or not fixtures:
            raise ValueError('Focused requires --platform and at least one --fixture (fully qualified class)')
        if any(not re.fullmatch(r'[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*', f) for f in fixtures):
            raise ValueError('Use exact fixture names, not regex or method filters')
        if len(set(fixtures)) != len(fixtures):
            raise ValueError('Duplicate fixture')
        return [(platform, tuple(fixtures))]
    if scope == 'full':
        return [('EditMode', ()), ('PlayMode', ())]
    raise ValueError('Unknown scope')


def provenance():
    def git(*args):
        return subprocess.check_output(['git', '-C', str(ROOT), *args], text=True).strip()
    sources = {}
    for name in ('unity/Assets/Spacewars/Content/Resources/PlayableProfile.json',
                 'unity/Assets/Spacewars/Content/Resources/ThreeCrossingsProfile.json',
                 'unity/Assets/Spacewars/Content/Resources/ThreeCrossingsSurfaceProfile.json'):
        path = ROOT / name
        if path.is_file():
            data = json.loads(path.read_text())
            sources[name] = {'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                             'identity': {k: data[k] for k in ('releaseRef', 'id', 'profileId', 'revision',
                                          'sourceProfileId', 'sourceProfileRevision') if k in data}}
    return {'revision': git('rev-parse', 'HEAD'), 'dirty': git('status', '--short'),
            'diff_sha256': hashlib.sha256(subprocess.check_output(['git', '-C', str(ROOT), 'diff', 'HEAD'])).hexdigest(),
            'sources': sources}


def run():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--scope', choices=('ui', 'focused', 'full'), default='ui')
    parser.add_argument('--platform', choices=('EditMode', 'PlayMode'))
    parser.add_argument('--fixture', action='append', default=[])
    parser.add_argument('--affected-fixture', action='append', default=[],
                        help='UI only: complete battle, production, science or building lifecycle HUD fixture (repeatable)')
    parser.add_argument('--exclusive', action='store_true',
                        help='Exclusive unity-run admission for performance measurements or UI/focus QA')
    parser.add_argument('--plan', action='store_true')
    args = parser.parse_args()
    try:
        if args.scope != 'focused' and (args.platform or args.fixture):
            raise ValueError('Filters are only allowed with --scope focused')
        selection = plan(args.scope, args.platform, args.fixture, args.affected_fixture)
    except ValueError as error:
        parser.error(str(error))
    record = {'scope': args.scope, 'suites': selection, 'policy': '.agents/references/qa-scope.md',
              'full_gameplay_gate': args.scope == 'full', 'result': 'NotRun',
              'host_mode': 'exclusive' if args.exclusive else 'shared'}
    if args.plan:
        print(json.dumps(record, indent=2)); return 0
    destination = ROOT / '.local/qa'
    destination.mkdir(parents=True, exist_ok=True)
    evidence = Path(tempfile.mkdtemp(prefix=args.scope + '-', dir=destination))
    record.update(provenance())
    record.update(started_utc=datetime.now(timezone.utc).isoformat(), checks=[], result='Failed')
    (evidence / 'plan.json').write_text(json.dumps(record, indent=2) + '\n')
    # Capture untracked runner bytes too; HEAD alone cannot identify a dirty run.
    (evidence / 'runner.py').write_bytes(Path(__file__).read_bytes())
    (evidence / 'tracked.diff').write_bytes(subprocess.check_output(['git', '-C', str(ROOT), 'diff', 'HEAD']))
    started = time.monotonic()
    try:
        for platform, fixtures in selection:
            stem = platform.lower()
            xml = evidence / (stem + '.xml')
            log = evidence / (stem + '.log')
            command = [str(ROOT / 'tools/unity.sh'), record['host_mode'], '-batchmode', '-runTests',
                       '-testPlatform', platform, '-testResults', str(xml), '-logFile', str(log)]
            if platform == 'EditMode': command.append('-nographics')
            if fixtures: command.extend(['-testFilter', ';'.join(fixtures)])
            check = {'command': command, 'xml': str(xml), 'log': str(log)}
            record['checks'].append(check)
            print('Running ' + platform + '; evidence: ' + str(evidence), flush=True)
            tick = time.monotonic()
            with (evidence / (stem + '-host.log')).open('w') as host:
                process = subprocess.Popen(command, cwd=ROOT, stdout=subprocess.PIPE,
                                           stderr=subprocess.STDOUT, text=True)
                for line in process.stdout:
                    host.write(line); host.flush()
                    if line.startswith('unity-run: started '):
                        check['host_admission_seconds'] = time.monotonic() - tick
                process.stdout.close()
                exit_code = process.wait()
            check.update(exit_code=exit_code, wall_seconds=time.monotonic() - tick)
            if exit_code: raise ValueError(f'{platform} launcher exit {exit_code}')
            check.update(validate_results(xml, fixtures, args.scope == 'ui'))
            # This includes Editor startup/import/shutdown; it is not pure startup.
            check['non_test_wall_seconds'] = check['wall_seconds'] - check['test_duration_seconds']
        record['result'] = 'Passed'
    except (OSError, ValueError, ET.ParseError) as error:
        record['error'] = str(error)
        print('QA FAILED: ' + str(error), flush=True)
    finally:
        record['wall_seconds'] = time.monotonic() - started
        (evidence / 'result.json').write_text(json.dumps(record, indent=2) + '\n')
        print('QA evidence: ' + str(evidence), flush=True)
    return 0 if record['result'] == 'Passed' else 1


if __name__ == '__main__':
    raise SystemExit(run())
