"""Opt-in macOS startup diagnostic. No game launch or sampling on import."""
import csv
import ctypes
import hashlib
import json
import math
from pathlib import Path
import shlex
import subprocess
import sys
import time

PROBE_AT_SECONDS = 90.0
SAMPLE_SECONDS = 1
SAMPLE_TIMEOUT_SECONDS = 5.0


def file_sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def verify_app(app, manifest, digest):
    """Bind runtime content, modes and exact member set, never just the loader."""
    app, manifest = Path(app).resolve(), Path(manifest).resolve()
    raw = manifest.read_bytes()
    if hashlib.sha256(raw).hexdigest() != digest:
        raise ValueError('App manifest SHA256 mismatch')
    data = json.loads(raw)
    if Path(data['app']).resolve() != app or not data.get('source') or not data.get('runtimeSource'):
        raise ValueError('Manifest app/source identity mismatch')
    expected = {}
    for item in data['members']:
        relative = Path(item['path'])
        if relative.is_absolute() or '..' in relative.parts or item['path'] in expected:
            raise ValueError('Invalid/duplicate manifest member')
        expected[item['path']] = item
    if not expected:
        raise ValueError('Empty app manifest')
    actual = {}
    for path in app.rglob('*'):
        if path.is_symlink():
            raise ValueError('Symlink app member unsupported')
        if path.is_file():
            actual[path.relative_to(app).as_posix()] = path
    if actual.keys() != expected.keys():
        raise ValueError('App member set mismatch')
    for name, path in actual.items():
        item, stat = expected[name], path.stat()
        if stat.st_size != item['size'] or oct(stat.st_mode) != item['mode'] or file_sha256(path) != item['sha256']:
            raise ValueError('App content/mode mismatch: ' + name)
    executable = app / 'Contents/MacOS/Spacewars'
    if not executable.is_file() or not executable.stat().st_mode & 0o111:
        raise ValueError('Missing app executable')
    return {'app': str(app), 'manifest': str(manifest), 'manifest_sha256': digest,
            'source': data['source'], 'runtime_source': data['runtimeSource'], 'members': len(actual)}


def frame_state(path):
    """A complete row with valid core fields proves recording, without a footer.

    Missing/buffered/header-only/partial/invalid content cannot prove inactivity.
    """
    try:
        raw = Path(path).read_text()
        lines = raw.splitlines(keepends=True)
        if not lines or not lines[0].endswith('\n'):
            return 'UNKNOWN: missing/partial header'
        header = next(csv.reader([lines[0]]))
        required = ('elapsed', 'render_frame', 'tick', 'stopwatch_stamp', 'wall_frame_ms')
        if len(header) != len(set(header)) or not set(required).issubset(header):
            return 'UNKNOWN: invalid header'
        for line in lines[1:]:
            if not line.endswith('\n') or line.startswith('#'):
                continue
            try:
                values = next(csv.reader([line], strict=True))
                if len(values) != len(header):
                    continue
                row = dict(zip(header, values))
                if (all(math.isfinite(float(row[k])) for k in required) and
                        float(row['elapsed']) >= 0 and
                        all(int(row[k]) >= 0 for k in ('render_frame', 'tick')) and
                        int(row['stopwatch_stamp']) > 0):
                    return 'recorded_frame_row'
            except (ValueError, csv.Error):
                continue
        return 'UNKNOWN: no trustworthy complete row; buffering/activity unknown'
    except (OSError, UnicodeError, ValueError, csv.Error):
        return 'UNKNOWN: unreadable/invalid CSV'


# Layout/constants from the local macOS SDK sys/proc_info.h (PROC_PIDTBSDINFO=3),
# libproc.h. Microsecond process birth identity guards PID reuse.
class _BsdInfo(ctypes.Structure):
    _fields_ = [(name, ctypes.c_uint32) for name in (
        'flags', 'status', 'xstatus', 'pid', 'ppid', 'uid', 'gid', 'ruid',
        'rgid', 'svuid', 'svgid', 'reserved')]
    _fields_ += [('comm', ctypes.c_char * 16), ('name', ctypes.c_char * 32)]
    _fields_ += [(name, ctypes.c_uint32) for name in ('nfiles', 'pgid', 'pjobc', 'tdev', 'tpgid')]
    _fields_ += [('nice', ctypes.c_int32), ('start_sec', ctypes.c_uint64), ('start_usec', ctypes.c_uint64)]


def process_identity(pid):
    """Query only the supplied child PID. Failure is unknown, never a PID search."""
    if sys.platform != 'darwin' or not isinstance(pid, int) or pid <= 0:
        return None
    try:
        lib = ctypes.CDLL('/usr/lib/libproc.dylib', use_errno=True)
        lib.proc_pidinfo.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_uint64, ctypes.c_void_p, ctypes.c_int]
        lib.proc_pidinfo.restype = ctypes.c_int
        lib.proc_pidpath.argtypes = [ctypes.c_int, ctypes.c_void_p, ctypes.c_uint32]
        lib.proc_pidpath.restype = ctypes.c_int
        before, after = _BsdInfo(), _BsdInfo()
        size = ctypes.sizeof(before)
        if lib.proc_pidinfo(pid, 3, 0, ctypes.byref(before), size) != size:
            return None
        path = ctypes.create_string_buffer(4096)
        if lib.proc_pidpath(pid, path, len(path)) <= 0:
            return None
        command = subprocess.run(['/bin/ps', '-p', str(pid), '-o', 'command='],
                                 capture_output=True, text=True, timeout=.5, check=True).stdout.strip()
        if lib.proc_pidinfo(pid, 3, 0, ctypes.byref(after), size) != size:
            return None
        if bytes(before) != bytes(after):
            # Mutable counters may differ; birth/PID/parent must still agree.
            if (before.pid, before.ppid, before.start_sec, before.start_usec) != (after.pid, after.ppid, after.start_sec, after.start_usec):
                return None
        if before.pid != pid or before.start_sec <= 0:
            return None
        return {'pid': pid, 'ppid': before.ppid, 'birth': [before.start_sec, before.start_usec],
                'executable': path.value.decode(), 'command': command}
    except (OSError, ValueError, UnicodeError, subprocess.SubprocessError):
        return None


def is_owned(identity, pid, guardian, executable, output):
    try:
        if not identity or identity['pid'] != pid or identity['ppid'] != guardian or identity['executable'] != str(executable):
            return False
        args = shlex.split(identity['command'])
        return (args[0] == str(executable) and args.count('-playableEvidence') == 1 and
                args[args.index('-playableEvidence') + 1] == str(output) and
                all(flag in args for flag in ('-routePerformanceEvidence', '-noaudio', '-muted')))
    except (ValueError, IndexError, KeyError):
        return False


class StartupStackProbe:
    def __init__(self, output, executable, pid, guardian, runner_pid, *, clock=time.monotonic,
                 identify=process_identity, sampler=subprocess.run):
        self.output, self.executable = Path(output), Path(executable)
        self.pid, self.guardian = pid, guardian
        self.clock, self.identify, self.sampler = clock, identify, sampler
        self.done = False
        self.initial = identify(pid) if guardian == runner_pid else None
        self.valid = is_owned(self.initial, pid, guardian, self.executable, self.output)
        self.receipt = {'attempted': False, 'status': 'pending', 'reason': 'Awaiting90 admitted seconds',
                        'pid': pid, 'guardian': guardian, 'initial_identity': self.initial,
                        'sample_duration_seconds': SAMPLE_SECONDS, 'timeout_seconds': SAMPLE_TIMEOUT_SECONDS,
                        'activity': 'UNKNOWN', 'performance_acceptance': False}
        self.save()

    def save(self):
        (self.output / 'startup-stack-probe.json').write_text(json.dumps(self.receipt, indent=2) + '\n')

    def finish(self, reason):
        if not self.done:
            self.done = True
            self.receipt.update(status='skipped', reason=reason)
            self.save()

    def check(self, started, deadline, runner_alive):
        if self.done or self.clock() - started < PROBE_AT_SECONDS:
            return
        self.done = True  # one decision/attempt, even for failed or unknown guards
        self.receipt.update(status='skipped', admitted_seconds=self.clock() - started)
        state = frame_state(self.output / 'native-route-frames.csv')
        self.receipt['frame_state'] = state
        if state == 'recorded_frame_row':
            self.receipt['reason'] = 'Trustworthy frame row present'
        elif not runner_alive or not self.valid:
            self.receipt['reason'] = 'Exited runner or unknown initial child ownership'
        elif deadline - self.clock() < SAMPLE_TIMEOUT_SECONDS + 2:
            self.receipt['reason'] = 'Insufficient time inside unchanged hard deadline'
        else:
            # Revalidate immediately before invoking sample; never rediscover PID.
            current = self.identify(self.pid)
            self.receipt['pre_sample_identity'] = current
            if current != self.initial or not is_owned(current, self.pid, self.guardian, self.executable, self.output):
                self.receipt['reason'] = 'Exited/reused/changed PID or ownership UNKNOWN'
            elif deadline - self.clock() < SAMPLE_TIMEOUT_SECONDS + 1:
                self.receipt['reason'] = 'Identity check consumed remaining deadline budget'
            else:
                command = ['/usr/bin/sample', str(self.pid), str(SAMPLE_SECONDS), '1',
                           '-file', str(self.output / 'startup-stack-sample.txt')]
                self.receipt.update(attempted=True, status='unknown', reason='One diagnostic attempt; no causal/performance claim', command=command)
                self.save()
                began = self.clock()
                with (self.output / 'startup-stack-stdout.txt').open('w') as stdout, (self.output / 'startup-stack-stderr.txt').open('w') as stderr:
                    try:
                        result = self.sampler(command, stdout=stdout, stderr=stderr,
                                              timeout=SAMPLE_TIMEOUT_SECONDS, check=False)
                        self.receipt['exit_code'] = result.returncode
                        self.receipt['status'] = 'attempted' if result.returncode == 0 else 'unknown'
                    except subprocess.TimeoutExpired:
                        self.receipt.update(reason='Sampler timeout; no retry', exit_code=None, timed_out=True)
                    except OSError as error:
                        self.receipt.update(reason='Sampler unavailable: ' + str(error), exit_code=None)
                self.receipt['raw_wall_seconds'] = self.clock() - began
        self.save()
