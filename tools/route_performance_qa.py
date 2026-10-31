#!/usr/bin/env python3
"""Bounded, silent development Player profiling with verified child cleanup."""
import argparse
import csv
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import time
from qa.arrival_lifecycle import validate as validate_arrival_lifecycle
from qa.startup_stack_probe import StartupStackProbe, verify_app

ROOT = Path(__file__).resolve().parents[1]


def player_command(pid):
    result = subprocess.run(['ps', '-p', str(pid), '-o', 'command='],
                            capture_output=True, text=True)
    if result.returncode not in (0, 1):
        raise RuntimeError('Cannot verify Player PID: ' + str(pid))
    return result.stdout.strip()


def owned_player(pid, executable, output):
    command = player_command(pid)
    return owned_command(command, executable, output)


def owned_command(command, executable, output):
    return (command.startswith(str(executable) + ' ') and
            '-playableEvidence ' + str(output) + ' -routePerformanceEvidence' in command)


def close_owned_player(pid, executable, output):
    if not owned_player(pid, executable, output):
        return False
    os.kill(pid, signal.SIGTERM)
    deadline = time.monotonic() + 20
    while owned_player(pid, executable, output) and time.monotonic() < deadline:
        time.sleep(.25)
    if owned_player(pid, executable, output):
        os.kill(pid, signal.SIGKILL)
        deadline = time.monotonic() + 5
        while owned_player(pid, executable, output) and time.monotonic() < deadline:
            time.sleep(.25)
    if owned_player(pid, executable, output):
        raise RuntimeError('Owned Player still alive after cleanup: ' + str(pid))
    return True


def run(output, seed, maximum_wall_seconds=120, maximum_admission_seconds=180, scenario='small', ai_route_audit=False, q10_readouts=False, arrival_lifecycle_readouts=False, startup_stack_probe=False, player_app=None, player_app_manifest=None, player_app_manifest_sha256=None, player_cwd=None):
    launch_cwd = ROOT
    if startup_stack_probe:
        if not all((player_app, player_app_manifest, player_app_manifest_sha256, player_cwd)):
            raise ValueError('Startup probe requires explicit --player-app, --player-app-manifest and --player-app-manifest-sha256 and --player-cwd')
        launch_cwd = Path(player_cwd).resolve()
        if not launch_cwd.is_dir():
            raise ValueError('Player cwd must be an existing directory')
        if Path(player_app).resolve() != (launch_cwd / 'unity/Builds/macOS/SpacewarsPlayable.app').resolve():
            raise ValueError('Player cwd must match the explicit frozen app standard path')
        if not q10_readouts or not arrival_lifecycle_readouts or ai_route_audit:
            raise ValueError('Startup probe requires q10+lifecycle and no AI audit')
        if Path(player_app).resolve() == (ROOT / 'unity/Builds/macOS/SpacewarsPlayable.app').resolve():
            raise ValueError('Startup probe must not select the default/manual app')
    elif any((player_app, player_app_manifest, player_app_manifest_sha256, player_cwd)):
        raise ValueError('Explicit frozen app selection requires --startup-stack-probe')
    if arrival_lifecycle_readouts and not q10_readouts:
        raise ValueError("Arrival lifecycle requires explicit --q10-readouts")
    if ai_route_audit and not q10_readouts:
        raise ValueError('AI route audit requires explicit --q10-readouts')
    if q10_readouts and scenario != 'eight-player-400-moving':
        raise ValueError('Q10 readouts require the unchanged eight-player-400-moving diagnostic scenario')
    if scenario not in ('small', 'small-five', 'eight-player-400', 'eight-player-400-moving', 'split-screen-200'):
        raise ValueError('Unknown performance scenario: ' + scenario)
    # The eight-owner driver still observes exactly120s after scene startup.
    # Its admitted-process watchdog must also cover startup and final log drain.
    maximum_lifetime = 240 if scenario == 'split-screen-200' else 180 if scenario in ('eight-player-400', 'eight-player-400-moving') else 120
    if not 0 < maximum_wall_seconds <= maximum_lifetime:
        raise ValueError(f'Player hard limit must be positive and at most{maximum_lifetime} seconds')
    app_identity = verify_app(player_app, player_app_manifest, player_app_manifest_sha256) if startup_stack_probe else None
    if startup_stack_probe:
        app_identity['player_cwd'] = str(launch_cwd)
    output = Path(output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    executable = ROOT / 'unity/Builds/macOS/SpacewarsPlayable.app/Contents/MacOS/Spacewars'
    if startup_stack_probe:
        executable = Path(app_identity['app']) / 'Contents/MacOS/Spacewars'
        (output / 'player-app-identity.json').write_text(json.dumps(app_identity, indent=2) + '\n')
    if not executable.is_file():
        raise FileNotFoundError(executable)
    command = [str(ROOT / 'tools/unity.sh'), 'player', str(executable),
               '-noaudio', '-muted', '-matchSeed', str(seed),
               '-playableEvidence', str(output), '-routePerformanceEvidence',
               '-logFile', str(output / 'player.log'),
               '-qaDeadlineFile', str(output / 'qa-deadline.txt')]
    # Player batchmode suppresses scene rendering: its timings cannot diagnose
    # visible frame pacing. These runs own the exclusive graphical measurement.
    if scenario in ('eight-player-400', 'eight-player-400-moving'):
        command.append('-eightPlayerPerformanceEvidence')
    if scenario == 'split-screen-200':
        command += ['-splitScreenPerformanceEvidence', '-screen-fullscreen', '1', '-screen-width', '3456', '-screen-height', '2234']
    if scenario == 'small-five':
        command.append('-smallFivePerformanceEvidence')
    if scenario == 'eight-player-400-moving':
        command.append('-eightPlayerMovingPerformanceEvidence')
    if q10_readouts:
        command.append('-q10Readouts')
    if ai_route_audit:
        command.append('-aiRouteAuditEvidence')
    if arrival_lifecycle_readouts:
        command.append('-arrivalLifecycleReadouts')
    host_log = output / 'host.log'
    (output / 'command.json').write_text(json.dumps(command, indent=2) + '\n')
    with host_log.open('w') as log:
        process = subprocess.Popen(command, cwd=launch_cwd, stdout=log,
                                   stderr=subprocess.STDOUT,
                                   env=dict(os.environ, UNITY_RUN_MODE='exclusive'))
        code = None
        reason = 'Player autonomous exit'
        admission_started = time.monotonic()
        admission_seconds = 0
        player_started = None
        probe = None
        probe_failure = None
        def admitted():
            return re.search(r'started exclusive; guardian=\d+ pid=\d+', host_log.read_text()) is not None
        try:
            # Queue latency is not time spent by a live Player. Both phases remain
            # bounded; only the admitted phase owns the unchanged Player watchdog.
            while not admitted() and process.poll() is None:
                remaining = maximum_admission_seconds - (time.monotonic() - admission_started)
                if remaining <= 0:
                    break
                try:
                    process.wait(timeout=min(.25, remaining))
                except subprocess.TimeoutExpired:
                    pass
            admission_seconds = time.monotonic() - admission_started
            if admitted():
                player_started = time.monotonic()
                # Reserve teardown inside the same hard cap, including startup/import.
                reserve = min(10, maximum_wall_seconds / 10)
                now_ms = int(time.time() * 1000)
                deadline = output / 'qa-deadline.txt'
                temporary = output / 'qa-deadline.tmp'
                temporary.write_text(str(now_ms + int((maximum_wall_seconds-reserve)*1000)) + '\n' +
                                     str(now_ms + int(maximum_wall_seconds*1000)) + '\n')
                temporary.replace(deadline)
                remaining = max(0, maximum_wall_seconds-(time.monotonic()-player_started))
                if startup_stack_probe:
                    pair = re.findall(r'started exclusive; guardian=(\d+) pid=(\d+)', host_log.read_text())
                    guardian, child_pid = map(int, pair[-1])
                    probe = StartupStackProbe(output, executable, child_pid, guardian, process.pid)
                    hard_deadline = player_started + maximum_wall_seconds
                    while process.poll() is None:
                        if time.monotonic() >= hard_deadline:
                            raise subprocess.TimeoutExpired(command, maximum_wall_seconds)
                        probe.check(player_started, hard_deadline, process.poll() is None)
                        remaining = max(0, hard_deadline - time.monotonic())
                        try:
                            code = process.wait(timeout=min(.25, remaining))
                            break
                        except subprocess.TimeoutExpired:
                            pass
                    code = process.poll() if code is None else code
                else:
                    code = process.wait(timeout=remaining)
            else:
                code = process.poll()
                reason = 'Owned admission deadline' if code is None else 'Runner ended before Player admission'
        except subprocess.TimeoutExpired:
            reason = 'Owned watchdog deadline'
        except KeyboardInterrupt:
            reason = 'Owned launch cancelled'
            if player_started is None:
                admission_seconds = time.monotonic() - admission_started
        except Exception as error:
            if startup_stack_probe:
                probe_failure = f'{type(error).__name__}: {error}'
                reason = 'Owned run/probe failure'
            raise
        finally:
            # A terminal runner is not proof that its Unity child has exited.
            matches = re.findall(r'started exclusive; guardian=\d+ pid=(\d+)',
                                 host_log.read_text())
            child = int(matches[-1]) if matches else None
            if child is not None and close_owned_player(child, executable, output):
                reason += '; owned child terminated'
            if process.poll() is None:
                # With no admitted Player this cancels only our waiting command.
                process.send_signal(signal.SIGINT)
                code = process.wait(timeout=10)
            # Re-read after cancellation: admission could have raced the first read.
            matches = re.findall(r'started exclusive; guardian=\d+ pid=(\d+)',
                                 host_log.read_text())
            if matches:
                child = int(matches[-1])
                if close_owned_player(child, executable, output):
                    reason += '; late admitted owned child terminated'
            inventory_lines = []
            if not startup_stack_probe:
                inventory_lines = subprocess.run(['ps', '-axo', 'pid=,command='],
                                                 capture_output=True, text=True, check=True).stdout.splitlines()
            for line in inventory_lines:
                parts = line.strip().split(None, 1)
                if len(parts) == 2 and owned_command(parts[1], executable, output):
                    child = int(parts[0])
                    close_owned_player(child, executable, output)
                    reason += '; fingerprinted owned child terminated'
            alive = child is not None and owned_player(child, executable, output)
            terminal = {'exit_code': code, 'stop': reason,
                        'player_pid': child, 'actual_player_alive': alive,
                        'admission_seconds': admission_seconds,
                        'q10_readouts': q10_readouts, 'ai_route_audit': ai_route_audit,
                        'player_phase_seconds': None if player_started is None else time.monotonic()-player_started}
            if startup_stack_probe:
                terminal['player_cwd'] = str(launch_cwd)
            # Probe receipt IO must never run before required owned-child cleanup.
            # Even constructor/save failure leaves the original terminal path usable.
            try:
                if probe is not None:
                    probe.finish('Player/runner ended before probe decision')
                elif startup_stack_probe:
                    (output / 'startup-stack-probe.json').write_text(json.dumps({
                        'attempted': False,
                        'status': 'skipped' if player_started is None else 'unknown',
                        'reason': 'No admitted owned child' if player_started is None else 'Probe initialization incomplete',
                        'activity': 'UNKNOWN'}, indent=2) + '\n')
            except Exception as error:
                failure = f'{type(error).__name__}: {error}'
                probe_failure = failure if probe_failure is None else probe_failure + '; finalize: ' + failure
            if probe_failure is not None:
                terminal['startup_stack_probe_error'] = probe_failure
                reason += '; probe receipt/run failure'
                terminal['stop'] = reason
            (output / 'terminal.json').write_text(json.dumps(terminal, indent=2) + '\n')
    return 0 if code == 0 and reason == 'Player autonomous exit' and child is not None and not alive and evidence_complete(output, scenario, q10_readouts=q10_readouts, ai_route_audit=ai_route_audit, arrival_lifecycle_readouts=arrival_lifecycle_readouts) else 1


def evidence_complete(output, scenario, q10_readouts=False, ai_route_audit=False, arrival_lifecycle_readouts=False):
    output = Path(output)
    if arrival_lifecycle_readouts and (not q10_readouts or not validate_arrival_lifecycle(output / "arrival-lifecycle.csv")):
        return False
    if ai_route_audit and not q10_readouts:
        return False
    if q10_readouts:
        (output/'q10-collection-summary.json').write_text(json.dumps({'collection_complete': False, 'gameplay_qualified': None, 'reason': 'Missing/invalid requested Q10 evidence; no game outcome inferred'}, indent=2)+'\n')
    complete = output / 'qa-completion.txt'
    if not complete.is_file() or not complete.read_text().startswith('COMPLETE\n'):
        return False
    def complete_rows(path):
        if not path.is_file():
            return None
        lines = path.read_text().splitlines()
        match = re.fullmatch(r'# samples=(\d+),dropped=(\d+)', lines[-1]) if lines else None
        if not match or int(match[1]) < 1 or int(match[1]) != len(lines)-2 or int(match[2]) != 0:
            return None
        return list(csv.DictReader(lines[:-1]))
    if complete_rows(output / 'native-route-frames.csv') is None:
        return False
    if scenario == 'split-screen-200':
        samples = output / 'split-screen-seats.csv'
        if not samples.is_file():
            return False
        with samples.open() as source:
            rows = list(csv.DictReader(source))
        host = output / 'precise-render-host.json'
        if not host.is_file() or json.loads(host.read_text()).get('renderScale') != 1:
            return False
        try:
            if {r['owner'] for r in rows} != {'player-1', 'foundry-2', 'foundry-3', 'foundry-4'}:
                return False
            if any(int(r['screen_width']) != 3456 or int(r['screen_height']) != 2234 for r in rows):
                return False
            if {int(r['phase']) for r in rows} != {0, 1, 2, 3}:
                return False
            if any(int(r['units']) != 50 for r in rows if int(r['phase']) in (0, 1, 2)):
                return False
            if max(float(r['elapsed']) for r in rows) < 89:
                return False
        except (KeyError, ValueError):
            return False
    motion = None
    if scenario.startswith('eight-player-400'):
        motion = complete_rows(output / 'eight-player-motion.csv')
        if motion is None or len({r.get('actor') for r in motion}) != 400:
            return False
    if q10_readouts:
        receipt_path = output / 'eight-player-receipts.csv'
        if not receipt_path.is_file():
            return False
        lines = receipt_path.read_text().splitlines()
        footer = re.fullmatch(r'# rows=(\d+),dropped=0,expected=8', lines[-1]) if lines else None
        receipts = list(csv.DictReader(lines[:-1])) if footer else []
        if not footer or len(receipts) != int(footer[1]):
            return False
        submitted = [r for r in receipts if r.get('phase') == 'submission']
        expected = {(r.get('owner'), r.get('generation'), r.get('sequence')) for r in submitted}
        if len(submitted) != 8 or len(expected) != 8 or len({r.get('owner') for r in submitted}) != 8 :
            return False
        valid_statuses = {'Accepted', 'Applied', 'Rejected', 'Overflow', 'StaleGeneration', 'InvalidSequence', 'InvalidOwner', 'InvalidEntity', 'InvalidTarget', 'InsufficientCredits', 'OccupiedPad', 'Stopped', 'Cancelled'}
        if any(r.get('status') not in valid_statuses or r.get('phase') not in ('submission', 'receipt') for r in receipts):
            return False
        try:
            if any(not r.get('owner') or int(r['generation']) < 1 or int(r['sequence']) < 1 for r in receipts):
                return False
        except (KeyError, TypeError, ValueError):
            return False
        if any((r.get('owner'), r.get('generation'), r.get('sequence')) not in expected for r in receipts):
            return False
        if motion is None or not all(k in motion[0] for k in ('expected_sequence', 'pending_sequence', 'active_sequence', 'current_sequence', 'meta_tick', 'meta_generation', 'actual_admission_requests', 'completion_sequence', 'completion_spatial')):
            return False
        final_path = output / 'eight-player-group-final.csv'
        if not final_path.is_file():
            return False
        final_lines = final_path.read_text().splitlines()
        final_rows = list(csv.DictReader(final_lines[:-1]))
        if not final_lines or final_lines[-1] != '# rows=400,dropped=0' or len(final_rows) != 400 or len({r.get('actor') for r in final_rows}) != 400:
            return False
        if not all(k in final_rows[0] for k in ('expected_sequence', 'pending_sequence', 'active_sequence', 'current_sequence', 'completion_sequence', 'completion_spatial', 'actual_admission_requests')):
            return False
        try:
            available = sum(int(r['meta_generation']) > 0 and int(r['meta_tick']) >= 0 and int(r['actual_admission_requests']) >= 0 for r in motion)
        except (KeyError, TypeError, ValueError):
            return False
        if available == 0:
            return False
        summary = {'collection_complete': True, 'gameplay_qualified': None,
                   'expected_submissions': len(submitted), 'submission_status_counts': {}, 'actual_receipt_status_counts': {},
                   'motion_meta_available_rows': available, 'motion_meta_unknown_rows': len(motion)-available,
                   'receipt_absence_means': 'unknown, never rejected or Applied'}
        for r in submitted:
            status = r['status']
            summary['submission_status_counts'][status] = summary['submission_status_counts'].get(status, 0)+1
        refusals = [r for r in receipts if r['status'] not in ('Accepted', 'Applied')]
        if refusals:
            summary.update(gameplay_qualified=False, gameplay_reason='Real submission/receipt refusal; collection integrity is separate')
        for r in receipts:
            if r.get('phase') == 'receipt':
                status = r.get('status', '')
                summary['actual_receipt_status_counts'][status] = summary['actual_receipt_status_counts'].get(status, 0)+1
        # Missing terminal callbacks stay unknown, never synthesized as rejected.
    if ai_route_audit:
        path = output / 'ai-route-review-audit.csv'
        if not path.is_file():
            return False
        lines = path.read_text().splitlines()
        rows = list(csv.DictReader(lines[:-1]))
        if not lines or lines[-1] != '# owners=8,dropped=0' or len(rows) != 8 or len({r.get('owner') for r in rows}) != 8:
            return False
        if any(not all(k in r for k in ('untracked_calls', 'foreign_profile_calls', 'foreign_observation_calls', 'repeated_safe', 'repeated_withdrawal', 'max_bound', 'max_pairs', 'observer_wall_ms')) for r in rows):
            return False
        try:
            if any(not 0 <= int(r['max_pairs']) <= int(r['max_bound']) <= 4096 for r in rows):
                return False
            untracked = sum(int(r['untracked_calls']) for r in rows)
            foreign = sum(int(r['foreign_profile_calls'])+int(r['foreign_observation_calls']) for r in rows)
        except (ValueError, TypeError):
            return False
        summary.update(ai_untracked_calls=untracked, ai_foreign_binding_calls=foreign, ai_exact_multiplicity_counts=untracked == 0 and foreign == 0,
                       ai_observer_limitation='Observer wall/GC impact remains in raw game wall; untracked counts are not zero repeats')
    if q10_readouts:
        (output/'q10-collection-summary.json').write_text(json.dumps(summary, indent=2)+'\n')
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--q10-readouts', action='store_true', help='Opt-in bounded scripted receipts and pending/installed group readouts; no capture or build')
    parser.add_argument('--arrival-lifecycle-readouts', action='store_true', help='Opt-in bounded original-eight physical arrival lifecycle; requires --q10-readouts; no build/capture')
    parser.add_argument('--ai-route-audit', action='store_true', help='Opt-in bounded per-owner Review route-safety multiplicity observer; no cache or replay capture')
    parser.add_argument('--seed', type=int, required=True, help='Explicit fixed-input seed; no implicit replay default')
    parser.add_argument('--scenario', choices=['small', 'small-five', 'eight-player-400', 'eight-player-400-moving', 'split-screen-200'], default='small')
    parser.add_argument('--maximum-wall-seconds', type=float, default=120, help='Hard admitted lifetime: at most120s,180s for eight-player400, or240s for split-screen-200; gameplay horizons stay unchanged')
    parser.add_argument('--startup-stack-probe', action='store_true', help='One opt-in owned PID sample near90 admitted seconds without a trustworthy frame row; no build')
    parser.add_argument('--player-app', type=Path, help='Explicit frozen app; probe mode only, never the default/manual app')
    parser.add_argument('--player-app-manifest', type=Path)
    parser.add_argument('--player-app-manifest-sha256')
    parser.add_argument('--player-cwd', type=Path, help='Required probe-only existing cwd matching <cwd>/unity/Builds/macOS/SpacewarsPlayable.app')
    args = parser.parse_args()
    return run(args.output, args.seed, maximum_wall_seconds=args.maximum_wall_seconds, scenario=args.scenario, ai_route_audit=args.ai_route_audit, q10_readouts=args.q10_readouts, arrival_lifecycle_readouts=args.arrival_lifecycle_readouts, startup_stack_probe=args.startup_stack_probe, player_app=args.player_app, player_app_manifest=args.player_app_manifest, player_app_manifest_sha256=args.player_app_manifest_sha256, player_cwd=args.player_cwd)


if __name__ == '__main__':
    raise SystemExit(main())
