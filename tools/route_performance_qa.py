#!/usr/bin/env python3
"""Bounded, silent development Player profiling with verified child cleanup."""
import argparse
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import time

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


def run(output, seed, maximum_wall_seconds=180, maximum_admission_seconds=180):
    output = Path(output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    executable = ROOT / 'unity/Builds/macOS/SpacewarsPlayable.app/Contents/MacOS/Spacewars'
    if not executable.is_file():
        raise FileNotFoundError(executable)
    command = [str(ROOT / 'tools/unity.sh'), 'player', str(executable),
               '-batchmode', '-noaudio', '-muted', '-matchSeed', str(seed),
               '-playableEvidence', str(output), '-routePerformanceEvidence',
               '-logFile', str(output / 'player.log')]
    host_log = output / 'host.log'
    (output / 'command.json').write_text(json.dumps(command, indent=2) + '\n')
    with host_log.open('w') as log:
        process = subprocess.Popen(command, cwd=ROOT, stdout=log,
                                   stderr=subprocess.STDOUT,
                                   env=dict(os.environ, UNITY_RUN_MODE='exclusive'))
        code = None
        reason = 'Player autonomous exit'
        admission_started = time.monotonic()
        admission_seconds = 0
        player_started = None
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
                code = process.wait(timeout=maximum_wall_seconds)
            else:
                code = process.poll()
                reason = 'Owned admission deadline' if code is None else 'Runner ended before Player admission'
        except subprocess.TimeoutExpired:
            reason = 'Owned watchdog deadline'
        except KeyboardInterrupt:
            reason = 'Owned launch cancelled'
            if player_started is None:
                admission_seconds = time.monotonic() - admission_started
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
            inventory = subprocess.run(['ps', '-axo', 'pid=,command='],
                                       capture_output=True, text=True, check=True)
            for line in inventory.stdout.splitlines():
                parts = line.strip().split(None, 1)
                if len(parts) == 2 and owned_command(parts[1], executable, output):
                    child = int(parts[0])
                    close_owned_player(child, executable, output)
                    reason += '; fingerprinted owned child terminated'
            alive = child is not None and owned_player(child, executable, output)
            terminal = {'exit_code': code, 'stop': reason,
                        'player_pid': child, 'actual_player_alive': alive,
                        'admission_seconds': admission_seconds,
                        'player_phase_seconds': None if player_started is None else time.monotonic()-player_started}
            (output / 'terminal.json').write_text(json.dumps(terminal, indent=2) + '\n')
    return 0 if code == 0 and reason == 'Player autonomous exit' and child is not None and not alive else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--seed', type=int, default=-1875503562)
    args = parser.parse_args()
    return run(args.output, args.seed)


if __name__ == '__main__':
    raise SystemExit(main())
