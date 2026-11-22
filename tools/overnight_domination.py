"""Record ordinary Domination matches and index their raw journals."""
import argparse
from collections import Counter
from contextlib import closing
from datetime import datetime, timedelta, timezone
import fcntl
import gzip
from itertools import combinations, cycle
import json
import math
import os
from pathlib import Path
import select
import shutil
import signal
import sqlite3
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zlib

from qa_project_settings import ProjectSettingsGuard


ROOT = Path(__file__).resolve().parent.parent
CAMPAIGNS = ROOT / ".local" / "overnight-domination"
FIXTURE = 'OvernightDominationBatch.RecordLobbyMatches'
LEVELS = ('Recruit', 'Fighter', 'Veteran', 'Expert', 'Cheater')
DEFAULT_DIFFICULTY = 'Expert'
OWNERS = ('Player', 'Enemy', 'Third', 'Fourth', 'Fifth', 'Sixth', 'Seventh', 'Eighth')
MAPS = ('crossings', 'dock', 'basalt', 'moss', 'meridian', 'foundry', 'city', 'grove', 'polygon')
OPENINGS = ('double-income', 'greedy-ground', 'aggressive', 'all-in', 'air')
OPENING_MAPS = ('crossings', 'meridian')
DIFFICULTY_OPENING = 'double-income'
RECORDABLE_STATUSES = ('finished', 'horizon', 'wall-limit')
STALL_TICKS = 15 * 30
SCHEMA = """
CREATE TABLE IF NOT EXISTS campaign (key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS campaign_runs (
    id INTEGER PRIMARY KEY, started_utc TEXT NOT NULL, ended_utc TEXT,
    wall_seconds REAL, outcome TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS matches (
    id INTEGER PRIMARY KEY, phase TEXT NOT NULL, map TEXT NOT NULL, seed INTEGER NOT NULL,
    players INTEGER NOT NULL, teams_json TEXT NOT NULL, difficulties_json TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending', latest_batch INTEGER, openings_json TEXT
);
CREATE TABLE IF NOT EXISTS attempts (
    batch INTEGER NOT NULL, match_id INTEGER NOT NULL REFERENCES matches(id),
    status TEXT NOT NULL, tick INTEGER, winner_team INTEGER, victory_reason TEXT, wall_seconds REAL,
    failure TEXT, journal_status TEXT NOT NULL, dropped INTEGER, errors_count INTEGER NOT NULL,
    identity_json TEXT, result_json TEXT NOT NULL, PRIMARY KEY(batch, match_id)
);
CREATE TABLE IF NOT EXISTS files (
    batch INTEGER NOT NULL, match_id INTEGER NOT NULL, name TEXT NOT NULL, path TEXT NOT NULL,
    bytes INTEGER NOT NULL, PRIMARY KEY(batch, match_id, name)
);
CREATE TABLE IF NOT EXISTS performance (
    batch INTEGER NOT NULL, match_id INTEGER NOT NULL, tick INTEGER NOT NULL, wall_seconds REAL NOT NULL,
    tps REAL NOT NULL, target_tps REAL NOT NULL, no_progress_seconds REAL NOT NULL, data_json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS errors (
    batch INTEGER NOT NULL, match_id INTEGER NOT NULL, type TEXT, message TEXT, data_json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS unit_stalls (
    batch INTEGER NOT NULL, match_id INTEGER NOT NULL, owner_id TEXT, entity_id INTEGER, unit_kind TEXT,
    order_sequence INTEGER, order_kind TEXT, start_tick INTEGER, last_tick INTEGER,
    nav_outcome TEXT, x REAL, z REAL
);
CREATE TABLE IF NOT EXISTS ai_openings (
    batch INTEGER NOT NULL, match_id INTEGER NOT NULL, owner_id TEXT, tick INTEGER, template TEXT,
    decision_json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS event_counts (
    batch INTEGER NOT NULL, match_id INTEGER NOT NULL, type TEXT NOT NULL, count INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS performance_match ON performance(match_id, batch, tick);
CREATE INDEX IF NOT EXISTS errors_match ON errors(match_id, batch);
CREATE INDEX IF NOT EXISTS stalls_match ON unit_stalls(match_id, batch, start_tick);
CREATE INDEX IF NOT EXISTS openings_match ON ai_openings(match_id, batch, owner_id);
CREATE VIEW IF NOT EXISTS severe_tps AS
    SELECT * FROM performance WHERE tps < 15 OR no_progress_seconds >= 5;
CREATE VIEW IF NOT EXISTS latest_matches AS
    SELECT m.*, a.tick, a.winner_team, a.victory_reason, a.wall_seconds, a.failure,
           a.journal_status, a.dropped, a.errors_count, a.identity_json
    FROM matches m LEFT JOIN attempts a ON a.match_id = m.id AND a.batch = m.latest_batch;
CREATE VIEW IF NOT EXISTS recorded_matches AS
    SELECT * FROM latest_matches
    WHERE status IN ('finished', 'horizon', 'wall-limit') AND journal_status = 'complete';
CREATE VIEW IF NOT EXISTS completed_matches AS
    SELECT * FROM recorded_matches WHERE status = 'finished';
"""


def matrix():
    jobs = []

    def add(phase, map_name, seed, teams, openings=None, difficulties=None):
        jobs.append(dict(Id=len(jobs) + 1, Phase=phase, Map=map_name, Seed=seed,
                         Teams=list(teams),
                         Difficulties=list(difficulties) if difficulties is not None else [DEFAULT_DIFFICULTY] * len(teams),
                         Openings=None if openings is None else list(openings)))

    for seed in (7, 41):
        for map_name in MAPS:
            add('duel', map_name, seed, (1, 2))
    # On each map, the same seed holds starts fixed when the openings swap.
    for map_name in OPENING_MAPS:
        for pair in combinations(OPENINGS, 2):
            for seats in (pair, pair[::-1]):
                add('opening-duel', map_name, 41, (1, 2), seats)
    for seed in (41, 83):
        for size, maps in ((2, ('moss', 'meridian', 'foundry', 'city', 'grove', 'polygon')),
                           (3, ('foundry', 'city', 'grove', 'polygon')),
                           (4, ('grove', 'polygon'))):
            for map_name in maps:
                add('team', map_name, seed, (1,) * size + (2,) * size)
    for index, (map_name, counts) in enumerate((
            ('moss', (3, 4, 4)), ('meridian', (3, 3, 4)), ('city', (3, 5, 6)),
            ('foundry', (4, 5, 6)), ('grove', (4, 6, 8)), ('polygon', (3, 5, 8)))):
        for variant, players in enumerate(counts):
            add('ffa', map_name, 101 + 2 * (3 * index + variant), range(1, players + 1))
    # Rotate maps between pairs. Both seats in a mirrored pair share the map and seed.
    difficulty_maps = cycle(MAPS)
    for seed, pairs in ((191, combinations(LEVELS, 2)), (193, zip(LEVELS, LEVELS[1:]))):
        for pair in pairs:
            map_name = next(difficulty_maps)
            for seats in (pair, pair[::-1]):
                add('difficulty-duel', map_name, seed, (1, 2),
                    openings=(DIFFICULTY_OPENING, DIFFICULTY_OPENING), difficulties=seats)
    return jobs


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()


def source_changed(revision):
    return (git('rev-parse', 'HEAD') != revision
            or bool(git('status', '--porcelain', '--untracked-files=no')))


def encode(data):
    return json.dumps(data, ensure_ascii=False, separators=(',', ':'))


def json_file(path, default=None):
    return json.loads(path.read_text()) if path.is_file() else default


def utc():
    return datetime.now(timezone.utc).isoformat(timespec='seconds')


def new_campaign(parent, date):
    parent.mkdir(parents=True, exist_ok=True)
    number = 1
    while True:
        root = parent / (date if number == 1 else f'{date}-{number:02d}')
        try:
            root.mkdir()
            return root
        except FileExistsError:
            number += 1


def resume_campaign(parent, config):
    candidates = sorted(parent.glob('*/matches.sqlite'), key=lambda path: path.stat().st_mtime_ns, reverse=True)
    for path in candidates:
        with closing(sqlite3.connect(path.as_uri() + '?mode=ro', uri=True)) as db:
            tables = {row[0] for row in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
            if not {'campaign', 'matches'} <= tables:
                continue
            saved = db.execute("SELECT value FROM campaign WHERE key='config'").fetchone()
            if (saved and json.loads(saved[0]) == config and
                    db.execute("SELECT 1 FROM matches WHERE status='pending' LIMIT 1").fetchone()):
                return path.parent
    raise ValueError('No unfinished campaign matches this revision and match settings. Run make overnight-domination to start one.')


def initialize(db, config, jobs):
    has_campaign = db.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name='campaign'").fetchone()
    saved = db.execute("SELECT value FROM campaign WHERE key='config'").fetchone() if has_campaign else None
    if saved:
        if json.loads(saved[0]) != config:
            raise ValueError('Existing campaign has a different source, matrix or match configuration. Start a new campaign.')
        return
    db.executescript(SCHEMA)
    columns = {row[1] for row in db.execute('PRAGMA table_info(matches)')}
    if 'openings_json' not in columns:
        db.execute('ALTER TABLE matches ADD COLUMN openings_json TEXT')
    with db:
        db.execute('INSERT INTO campaign VALUES (?, ?)', ('config', encode(config)))
        db.executemany("""
            INSERT INTO matches (id, phase, map, seed, players, teams_json, difficulties_json, openings_json)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """, [
            (j['Id'], j['Phase'], j['Map'], j['Seed'], len(j['Teams']), encode(j['Teams']),
             encode(j['Difficulties']), encode(j['Openings']) if j.get('Openings') is not None else None)
            for j in jobs
        ])


def compare_difficulties(db):
    comparison = {}
    rows = db.execute("""
        SELECT m.difficulties_json, m.teams_json, m.status, r.id, r.winner_team
        FROM matches m LEFT JOIN recorded_matches r ON r.id=m.id
        WHERE m.phase='difficulty-duel'
    """)
    for difficulties_json, teams_json, status, recorded_id, winner in rows:
        difficulties, teams = json.loads(difficulties_json), json.loads(teams_json)
        lower, higher = sorted(difficulties, key=LEVELS.index)
        totals = comparison.setdefault((lower, higher), dict(
            lower_difficulty=lower, higher_difficulty=higher, opening=DIFFICULTY_OPENING, scheduled=0,
            lower_wins=0, higher_wins=0, horizon=0, wall_limit=0, pending=0, unscored=0))
        totals['scheduled'] += 1
        if recorded_id is not None and status == 'finished' and winner in teams:
            winner_difficulty = difficulties[teams.index(winner)]
            totals['lower_wins' if winner_difficulty == lower else 'higher_wins'] += 1
        elif recorded_id is not None and status in ('horizon', 'wall-limit'):
            totals[status.replace('-', '_')] += 1
        elif status == 'pending':
            totals['pending'] += 1
        else:
            totals['unscored'] += 1
    return [comparison[pair] for pair in combinations(LEVELS, 2) if pair in comparison]


def report_timing(db, root, run_id, started, outcome):
    elapsed = round(time.monotonic() - started, 3)
    with db:
        db.execute('UPDATE campaign_runs SET ended_utc=?, wall_seconds=?, outcome=? WHERE id=?',
                   (utc(), elapsed, outcome, run_id))
    runs = [dict(id=i, started_utc=start, ended_utc=end, wall_seconds=seconds, outcome=state)
            for i, start, end, seconds, state in db.execute(
                'SELECT id, started_utc, ended_utc, wall_seconds, outcome FROM campaign_runs ORDER BY id')]
    matches = [dict(id=i, phase=phase, map=map_name, status=state, wall_seconds=seconds)
               for i, phase, map_name, state, seconds in db.execute(
                   'SELECT id, phase, map, status, wall_seconds FROM latest_matches ORDER BY id')]
    total = round(sum(run['wall_seconds'] for run in runs if run['wall_seconds'] is not None), 3)
    report = dict(campaign_wall_seconds=total, unmeasured_runs=sum(r['wall_seconds'] is None for r in runs),
                  recorded_matches=db.execute('SELECT count(*) FROM recorded_matches').fetchone()[0],
                  completed_matches=db.execute('SELECT count(*) FROM completed_matches').fetchone()[0],
                  runs=runs, matches=matches, difficulty_comparison=compare_difficulties(db))
    path = root / 'timing.json'
    temporary = path.with_suffix('.json.tmp')
    temporary.write_text(json.dumps(report, indent=2) + '\n')
    temporary.replace(path)
    print(f'{utc()}  elapsed={timedelta(seconds=round(elapsed))} '
          f'campaign_elapsed={timedelta(seconds=round(total))}; {path}', flush=True)


def records(paths):
    for path in paths:
        compressed = path.suffix == '.gz'
        number = 0
        try:
            with (gzip.open(path, 'rb') if compressed else path.open('rb')) as file:
                for number, line in enumerate(file, 1):
                    try:
                        row = json.loads(line)
                        if not isinstance(row, dict):
                            raise ValueError('expected a JSON object')
                        yield row, None
                    except (ValueError, UnicodeError) as error:
                        yield None, f'{path.name}:{number}: {error}'
        except (EOFError, gzip.BadGzipFile, zlib.error) as error:
            yield None, f'{path.name}:{number + 1}: {error}'


class Stalls:
    """Flag stationary own units on a sustained move. Samples can only show candidates."""

    def __init__(self, db, key, owners, interval):
        self.db, self.key, self.owners, self.interval = db, key, owners, interval
        self.active = {}

    def flush(self, row):
        if row[7] - row[6] >= STALL_TICKS:
            self.db.execute('INSERT INTO unit_stalls VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)',
                            (*self.key, *row[1:]))

    def state(self, event):
        tick = int(event['Tick'])
        current = {}
        for view in event['Owners']:
            owner_id = view['OwnerId']
            for unit in view['Entities']:
                order = unit.get('CurrentOrder')
                if unit.get('Owner') != self.owners.get(owner_id) or not order:
                    continue
                if order['Kind'] not in ('Move', 'AttackMove') or unit.get('Held') or unit.get('TargetId', 0) > 0:
                    continue
                if (unit.get('StasisRemaining', 0) > 0 or unit.get('MaterializationProgress', 1) < 1
                        or unit.get('Departing') or unit.get('NavigationOutcome') in ('Arrived', 'Held')):
                    continue
                pos = unit['Position']
                order_key = (owner_id, unit['Id'], order['CommandSequence'])
                row = self.active.pop(order_key, None)
                if row and (tick - row[7] > 2 * self.interval or
                            math.hypot(pos['X'] - row[9], pos['Z'] - row[10]) > .5):
                    self.flush(row)
                    row = None
                outcome = unit.get('NavigationOutcome')
                outcome = None if outcome is None else str(outcome)
                if row:
                    row[7] = tick
                    row[8] = outcome
                else:
                    row = [order_key, str(owner_id), int(unit['Id']), str(unit['Kind']), int(order['CommandSequence']),
                           order['Kind'], tick, tick, outcome, float(pos['X']), float(pos['Z'])]
                current[order_key] = row
        for row in self.active.values():
            self.flush(row)
        self.active = current

    def finish(self):
        for row in self.active.values():
            self.flush(row)


def file_size(path):
    try:
        return path.stat().st_size
    except FileNotFoundError:
        return 0


def tree_size(path):
    size = 0
    for directory, _, files in os.walk(path):
        for name in files:
            size += file_size(Path(directory) / name)
    return size


class StorageLimit(Exception):
    def __init__(self, reason, message):
        super().__init__(message)
        self.reason = reason


class StorageBudget:
    def __init__(self, root, args):
        self.root = root
        self.reserve = args.min_free_gib * 1024**3
        self.output_limit = args.max_output_gib * 1024**3
        self.match_limit = args.max_match_mib * 1024**2
        self.next_check = 0

    def check(self, force=False):
        now = time.monotonic()
        if not force and now < self.next_check:
            return
        self.next_check = now + .5
        for volume_path in (self.root, ROOT):
            if shutil.disk_usage(volume_path).free <= self.reserve:
                raise StorageLimit('low-disk', f'Free space reached the reserve at {volume_path}.')
        if tree_size(self.root) >= self.output_limit:
            raise StorageLimit('output-limit', f'Campaign output reached its limit at {self.root}.')


def archive(directory, root, files, budget):
    for source in files:
        target = root / 'logs' / directory.parent.name / directory.name / source.relative_to(directory)
        compressed = source.name.endswith('.jsonl.gz')
        if not compressed:
            target = target.with_name(target.name + '.gz')
        target.parent.mkdir(parents=True, exist_ok=True)
        temporary = target.with_suffix('.tmp')
        try:
            with source.open('rb') as reader, (temporary.open('wb') if compressed else
                    gzip.open(temporary, 'wb', compresslevel=3)) as writer:
                for chunk in iter(lambda: reader.read(256 * 1024), b''):
                    budget.check()
                    writer.write(chunk)
            budget.check(force=True)
            temporary.replace(target)
        finally:
            temporary.unlink(missing_ok=True)
        yield str(source.relative_to(directory)), str(target.relative_to(root)), source.stat().st_size


def acknowledge_collection(db, root, directory, key):
    for name, archive_path in db.execute('SELECT name, path FROM files WHERE batch=? AND match_id=?', key):
        source = directory / name
        if source.name.endswith(('.jsonl', '.jsonl.gz')) and (root / archive_path).is_file():
            source.unlink(missing_ok=True)
    (directory / 'collected').touch()


def lobby_owner_id(slot):
    return 'player-1' if slot == 0 else f'foundry-{slot+1}'


def collect(db, root, directory, budget, interrupted=None, *, acknowledge=True):
    batch = int(directory.parent.name)
    match_id = int(directory.name)
    key = (batch, match_id)
    if db.execute('SELECT 1 FROM attempts WHERE batch=? AND match_id=?', key).fetchone():
        if acknowledge:
            acknowledge_collection(db, root, directory, key)
        return
    budget.check(force=True)
    malformed_metadata = []

    def recorder_metadata(path):
        try:
            data = json_file(path, {})
            if not isinstance(data, dict):
                raise ValueError('expected a JSON object')
            return data
        except (ValueError, UnicodeError) as error:
            malformed_metadata.append(dict(Type='InvalidRecorderMetadata', Message=f'{path.name}: {error}'))
            return {}

    result = recorder_metadata(directory / 'result.json')
    if result and (not isinstance(result.get('Status'), str) or
                   any(not isinstance(result.get(field), kind) and result.get(field) is not None
                       for field, kind in (('Tick', int), ('WinnerTeam', int), ('WallSeconds', (int, float)),
                                           ('VictoryReason', str), ('Failure', str)))):
        malformed_metadata.append(dict(Type='InvalidRecorderMetadata', Message='result.json has invalid match fields'))
        result = {}
    if not result:
        result = {
            'Status': interrupted or 'interrupted',
            'Failure': 'Unity exited before writing a readable terminal match receipt.',
        }
    identity = recorder_metadata(directory / 'identity.json')
    players, planned_json = db.execute('SELECT players, openings_json FROM matches WHERE id=?', (match_id,)).fetchone()
    owners = {lobby_owner_id(slot): OWNERS[slot] for slot in range(players)}
    if identity:
        try:
            owners = {o['OwnerId']: o['Owner'] for o in identity['Owners']}
        except (KeyError, TypeError):
            malformed_metadata.append(dict(Type='InvalidRecorderMetadata', Message='identity.json has invalid owners'))
    journal = sorted(directory.glob('TestLogs/*/match-*/manifest.json'))
    summary = recorder_metadata(journal[0].with_name('summary.json')) if len(journal) == 1 else {}
    ends = [recorder_metadata(p) for p in directory.glob('TestLogs/*/session-end.json')]
    files = sorted(p for p in directory.rglob('*') if p.is_file() and p.name not in ('heartbeat', 'collected'))
    journal_status = 'complete' if (summary.get('Status') == 'complete' and len(ends) == 1
                                    and ends[0].get('RecorderFailure') is False
                                    and summary.get('Dropped') == 0) else 'partial'
    if not journal:
        journal_status = 'missing'
    manifest = recorder_metadata(journal[0]) if len(journal) == 1 else {}
    counts = Counter()
    openings = set()
    error_count = 0
    malformed = False

    def record_error(row):
        nonlocal error_count
        kind, message = row.get('Type'), row.get('Message')
        error_count += kind != 'Warning'
        db.execute('INSERT INTO errors VALUES (?, ?, ?, ?, ?)',
                   (*key, str(kind) if kind is not None else None,
                    str(message) if message is not None else None, encode(row)))

    def ingest(paths, consume):
        nonlocal malformed
        for row, error in records(paths):
            budget.check()
            if not error:
                try:
                    consume(row)
                except (KeyError, TypeError, ValueError, AttributeError, OverflowError) as invalid:
                    error = f'{type(invalid).__name__}: {invalid}'
            if error:
                # A malformed record could be a missing unit state sample.
                stalls.active.clear()
                malformed = True
                record_error(dict(Type='InvalidJsonLine', Message=error))

    def event(row):
        kind = row['Type']
        if not isinstance(kind, str):
            raise ValueError('expected an event type')
        counts[kind] += 1
        if kind == 'state':
            stalls.state(row)
        elif kind == 'ai-checkpoint':
            opening = row.get('OpeningExecution')
            if isinstance(opening, dict):
                owner, template = row['OwnerId'], opening.get('Template')
                if (owner, template) not in openings:
                    db.execute('INSERT INTO ai_openings VALUES (?, ?, ?, ?, ?, ?)',
                               (*key, str(owner), int(row['Tick']), None if template is None else str(template),
                                encode(opening)))
                    openings.add((owner, template))

    def performance(row):
        metrics = tuple(float(row[name]) for name in ('WallSeconds', 'Tps', 'TargetTps', 'NoProgressSeconds'))
        if not all(math.isfinite(value) for value in metrics):
            raise ValueError('non-finite performance sample')
        db.execute('INSERT INTO performance VALUES (?, ?, ?, ?, ?, ?, ?, ?)',
                   (*key, int(row['Tick']), *metrics, encode(row)))

    logging = manifest.get('Logging')
    interval = logging.get('stateIntervalTicks') if isinstance(logging, dict) else None
    with db:
        stalls = Stalls(db, key, owners, interval if isinstance(interval, int) and interval > 0 else 30)
        segments = (*directory.glob('TestLogs/*/match-*/events-*.jsonl'),
                    *directory.glob('TestLogs/*/match-*/events-*.jsonl.gz'))
        ingest(sorted(segments), event)
        stalls.finish()
        db.executemany('INSERT INTO event_counts VALUES (?, ?, ?, ?)',
                       [(*key, kind, count) for kind, count in counts.items()])
        ingest([directory / 'performance.jsonl'] if (directory / 'performance.jsonl').exists() else [], performance)
        ingest(sorted(directory.glob('TestLogs/*/errors.jsonl')), record_error)
        for error in malformed_metadata:
            record_error(error)
        if journal_status == 'complete' and (
                malformed or malformed_metadata or sum(counts.values()) != summary.get('Written')):
            journal_status = 'partial'
        status = result['Status']
        if planned_json is not None and journal_status == 'complete' and status in RECORDABLE_STATUSES:
            planned = json.loads(planned_json)
            expected = {(lobby_owner_id(slot), opening) for slot, opening in enumerate(planned)}
            if openings != expected:
                message = f'Expected {sorted(expected)}, recorded {sorted(openings, key=str)}.'
                record_error(dict(Type='OpeningMismatch', Message=message))
                status = 'opening-mismatch'
                result = {**result, 'ObservedStatus': result['Status'], 'Status': status, 'Failure': message}
        db.executemany('INSERT INTO files VALUES (?, ?, ?, ?, ?)',
                       [(*key, *row) for row in archive(directory, root, files, budget)])
        dropped = summary.get('Dropped')
        db.execute('INSERT INTO attempts VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)',
                   (*key, status, result.get('Tick'), result.get('WinnerTeam'), result.get('VictoryReason'),
                    result.get('WallSeconds'), result.get('Failure'), journal_status, dropped if isinstance(dropped, int) else None,
                    error_count, encode(identity), encode(result)))
        db.execute('UPDATE matches SET status=?, latest_batch=? WHERE id=?',
                   ('pending' if status == 'interrupted' else status, batch, match_id))
    if acknowledge:
        acknowledge_collection(db, root, directory, key)
    match_seconds = result.get('WallSeconds')
    duration = f'{match_seconds:.1f}s' if match_seconds is not None else 'unmeasured'
    print(f'{utc()}  #{match_id:03d} {status} tick={result.get("Tick")} wall={duration} '
          f'journal={journal_status} errors={error_count}', flush=True)


def stop_process(process):
    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=20)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()


def supervise(command):
    # EOF also arrives if the controller is killed. Keep its inherited campaign
    # lock until unity-run has stopped the Editor and closed the live journals.
    runner = subprocess.Popen(command, stdin=subprocess.DEVNULL)
    try:
        while runner.poll() is None:
            if select.select([sys.stdin], [], [], .2)[0]:
                break
    finally:
        stop_process(runner)
    return runner.returncode


def test_passed(path):
    if not path.is_file():
        return False
    try:
        cases = list(ET.parse(path).iter('test-case'))
    except ET.ParseError:
        return False
    return len(cases) == 1 and cases[0].get('fullname') == FIXTURE and cases[0].get('result') == 'Passed'


def flagged_matches(db):
    return db.execute("""
        SELECT count(*) FROM latest_matches WHERE status <> 'pending'
          AND (status NOT IN ('finished', 'horizon', 'wall-limit')
               OR journal_status <> 'complete' OR errors_count > 0)
    """).fetchone()[0]


def live_performance(path):
    try:
        with path.open('rb') as stream:
            stat = os.fstat(stream.fileno())
            start = max(0, stat.st_size - 65536)
            stream.seek(start)
            lines = stream.read(stat.st_size - start).split(b'\n')
        # Skip a partial first or last line if the writer is still appending.
        fields = ('Tick', 'WallSeconds', 'Tps', 'TargetTps', 'Units')
        for line in reversed(lines[bool(start):-1]):
            try:
                sample = json.loads(line)
            except ValueError:
                continue
            if isinstance(sample, dict) and all(
                    type(sample.get(key)) in (int, float) and math.isfinite(sample[key]) for key in fields):
                return dict(tick=sample['Tick'], wall_seconds=round(sample['WallSeconds'], 1),
                            tps=round(sample['Tps'], 1), target_tps=sample['TargetTps'], units=sample['Units'],
                            sample_age_seconds=round(max(0, time.time() - stat.st_mtime), 1))
    except OSError:
        pass
    return None


class CampaignProgress:
    def __init__(self, db, root, jobs, started):
        self.db = db
        self.path = root / 'progress.json'
        self.positions = {job['Id']: position for position, job in enumerate(jobs, 1)}
        self.started = started
        self.last_report = None
        self.write_failed = False

    def report(self, state, job=None, directory=None, *, force=False, announce=True):
        now = time.monotonic()
        if not force and self.last_report is not None and now - self.last_report < 30:
            return
        if announce:
            self.last_report = now
        total = len(self.positions)
        remaining = self.db.execute("SELECT count(*) FROM matches WHERE status='pending'").fetchone()[0]
        completed = self.db.execute('SELECT count(*) FROM completed_matches').fetchone()[0]
        recorded = self.db.execute('SELECT count(*) FROM recorded_matches').fetchone()[0]
        flagged = flagged_matches(self.db)
        remaining_percent = round(100 * remaining / total, 1)
        elapsed = round(now - self.started, 1)

        match = None
        match_label = ''
        if job is not None:
            position = self.positions[job['Id']]
            difficulties = '/'.join(job['Difficulties'])
            match = dict(id=job['Id'], position=position, phase=job['Phase'], map=job['Map'],
                         difficulties=job['Difficulties'], teams=job['Teams'], openings=job['Openings'])
            match_label = f" match {position}/{total} (#{job['Id']:03d} {job['Map']} {job['Phase']} {difficulties});"
            if directory is not None:
                sample = live_performance(directory / 'performance.jsonl')
                match['performance'] = sample
                if sample is not None:
                    match_label += f" match_time={timedelta(seconds=round(sample['wall_seconds']))}"
                    if sample['sample_age_seconds'] <= 5:
                        match_label += f" tick={sample['tick']} TPS={sample['tps']:g}/{sample['target_tps']:g} units={sample['units']};"
                    else:
                        match_label += f" TPS=stale ({sample['sample_age_seconds']:g}s);"

        snapshot = dict(updated_utc=utc(), state=state, elapsed_seconds=elapsed,
                        total_matches=total, remaining_matches=remaining, remaining_percent=remaining_percent,
                        completed_matches=completed, capped_matches=recorded - completed,
                        flagged_matches=flagged, current_match=match)
        if announce:
            print(f"{snapshot['updated_utc']}  {state};{match_label} remaining={remaining}/{total} ({remaining_percent:g}%) "
                  f"elapsed={timedelta(seconds=round(elapsed))} completed={completed} capped={recorded - completed} "
                  f"flagged={flagged}", flush=True)
        temporary = self.path.with_suffix('.json.tmp')
        try:
            temporary.write_text(encode(snapshot) + '\n')
            temporary.replace(self.path)
        except OSError as error:
            if not self.write_failed:
                print(f'{utc()}  progress.json: {error}', file=sys.stderr, flush=True)
            self.write_failed = True


def run_batch(db, root, plan, jobs, args, deadline, budget, lock_fd, progress):
    budget.check(force=True)
    batches = root / 'batches'
    batches.mkdir(exist_ok=True)
    index = max((int(p.name) for p in batches.iterdir() if p.name.isdecimal()), default=0) + 1
    batch = batches / f'{index:04d}'
    batch.mkdir()
    (batch / 'jobs.json').write_text(encode({**plan, 'Jobs': jobs}))
    settings = ProjectSettingsGuard(ROOT, batch / 'project-settings')
    command = [str(ROOT / 'tools/unity.sh'), 'exclusive' if args.exclusive else 'shared', '-batchmode', '-nographics', '-noaudio', '-muted',
               '-runTests', '-testPlatform', 'PlayMode', '-testFilter', FIXTURE,
               '-testResults', str(batch / 'playmode.xml'), '-logFile', str(batch / 'unity.log')]
    env = {**os.environ, 'SPACEWARS_OVERNIGHT_BATCH': str(batch)}
    stop_reason = None
    crash_match = False
    storage_stop = False
    job_log_start = 0
    started = last_activity = time.monotonic()
    heartbeat = None
    print(f'{utc()}  batch {index:04d}: Unity starting, {len(jobs)} scheduled; {batch}', flush=True)
    pending = iter(jobs)
    job = next(pending)
    progress.report('starting-unity', job, force=True)
    with (batch / 'launcher.log').open('w') as launcher:
        process = subprocess.Popen([sys.executable, __file__, '--supervise-editor', *command],
                                   cwd=ROOT, env=env, stdin=subprocess.PIPE, stdout=launcher,
                                   stderr=subprocess.STDOUT, start_new_session=True, pass_fds=(lock_fd,))
        try:
            while process.poll() is None:
                now = time.monotonic()
                if deadline is not None and now >= deadline and not (batch / 'stop').exists():
                    (batch / 'stop').touch()
                    stop_reason = 'wall-budget'
                budget.check()
                if job:
                    directory = batch / f'{job["Id"]:04d}'
                    if (directory / 'result.json').is_file():
                        progress.report('collecting', job, directory, force=True, announce=heartbeat is None)
                        collect(db, root, directory, budget, 'engine-failure', acknowledge=False)
                        if source_changed(plan['Revision']):
                            stop_reason = 'source-drift'
                            (batch / 'stop').touch()
                        acknowledge_collection(db, root, directory, (index, job['Id']))
                        progress.report('between-matches', force=True, announce=False)
                        row = db.execute('SELECT result_json FROM attempts WHERE batch=? AND match_id=?',
                                         (index, job['Id'])).fetchone()
                        result = json.loads(row[0])
                        last_activity = time.monotonic()
                        if result.get('RestartEditor'):
                            crash_match = True
                            stop_reason = stop_reason or 'shutdown-stalled'
                            break
                        heartbeat = None
                        job = next(pending, None)
                        continue
                    if (directory / 'heartbeat').is_file():
                        mark = (directory / 'heartbeat').stat().st_mtime_ns
                        engine_logs = file_size(batch / 'unity.log') + file_size(batch / 'launcher.log')
                        first_heartbeat = heartbeat is None
                        if first_heartbeat:
                            job_log_start = engine_logs
                        progress.report('running', job, directory, force=first_heartbeat)
                        if tree_size(directory) + engine_logs - job_log_start >= budget.match_limit:
                            crash_match = True
                            stop_reason = 'match-log-limit'
                            break
                        if mark != heartbeat:
                            heartbeat = mark
                            last_activity = now
                        elif now - last_activity > args.stall_seconds + 60:
                            crash_match = True
                            stop_reason = stop_reason or 'engine-watchdog'
                            break
                    else:
                        progress.report('waiting-for-match', job)
                else:
                    progress.report('finishing-unity')
                timeout = args.startup_minutes * 60 if job and heartbeat is None else args.stall_seconds + 60
                if now - last_activity > timeout:
                    stop_reason = stop_reason or 'engine-watchdog'
                    break
                time.sleep(.5)
        except StorageLimit as limit:
            stop_reason, storage_stop = limit.reason, True
            print(str(limit), flush=True)
        except KeyboardInterrupt:
            stop_reason = 'interrupted'
        finally:
            process.stdin.close()
            try:
                process.wait(timeout=25)
            except subprocess.TimeoutExpired:
                stop_process(process)
            settings_result = settings.finish()
    # A terminal receipt can appear during shutdown. Archive only while space allows.
    if not storage_stop:
        try:
            for directory in sorted(p for p in batch.iterdir() if p.is_dir() and p.name.isdecimal()):
                if stop_reason == 'match-log-limit':
                    missing = 'match-log-limit'
                elif crash_match or stop_reason is None:
                    missing = 'engine-failure'
                else:
                    missing = 'interrupted'
                collect(db, root, directory, budget, missing)
        except StorageLimit as limit:
            stop_reason, storage_stop = limit.reason, True
            print(str(limit), flush=True)
    collected = db.execute('SELECT count(*) FROM attempts WHERE batch=?', (index,)).fetchone()[0]
    if stop_reason is None and process.returncode:
        stop_reason = 'unity-exit-error'
        # Restart only after a match was recorded. Startup failures cannot loop.
        crash_match = 0 < collected < len(jobs)
    passed = process.returncode == 0 and test_passed(batch / 'playmode.xml')
    (batch / 'batch-result.json').write_text(encode(dict(
        Reason=stop_reason, ExitCode=process.returncode, HarnessPassed=passed,
        ProjectSettingsUnchanged=settings_result['unchanged'], WallSeconds=time.monotonic() - started)))
    if not settings_result['unchanged']:
        return 'project-settings-drift', False
    if stop_reason:
        return stop_reason, crash_match and not storage_stop
    return ('done' if passed and collected == len(jobs) else 'unity-harness-failure'), False


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    destination = parser.add_mutually_exclusive_group()
    destination.add_argument('--out', type=Path)
    destination.add_argument('--resume', action='store_true', help='resume the most recent compatible unfinished campaign')
    parser.add_argument('--exclusive', action='store_true', help='reserve the host for performance measurement')
    parser.add_argument('--plan', action='store_true', help='print scheduled configurations without Unity')
    selection = parser.add_mutually_exclusive_group()
    selection.add_argument('--smoke', action='store_true', help='short matches covering both maps, roster formats and swapped openings')
    selection.add_argument('--case', type=int, help='run a single match ID from the plan')
    selection.add_argument('--difficulty-matrix', action='store_true', help='run only the mixed-difficulty duels')
    parser.add_argument('--difficulty', choices=LEVELS, help='override all bot difficulties for the selected --case')
    parser.add_argument('--hours', type=float, help='optional wall time limit for starting matches')
    parser.add_argument('--speed', type=int, choices=(1, 2, 4), default=4)
    parser.add_argument('--horizon-minutes', type=int, default=20)
    parser.add_argument('--match-wall-minutes', type=int, default=15)
    parser.add_argument('--stall-seconds', type=int, default=90)
    parser.add_argument('--startup-minutes', type=int, default=40)
    parser.add_argument('--min-free-gib', type=float, default=2, help='reserve on the output and Unity project volumes')
    parser.add_argument('--max-output-gib', type=float, default=4, help='stop when campaign files reach this size')
    parser.add_argument('--max-match-mib', type=float, default=512, help='interrupt a match when its live logs reach this size')
    args = parser.parse_args()
    if args.difficulty is not None and args.case is None:
        parser.error('--difficulty requires --case')
    limits = (args.hours, args.horizon_minutes, args.match_wall_minutes, args.stall_seconds,
              args.startup_minutes, args.min_free_gib, args.max_output_gib, args.max_match_mib)
    if not all(value is None or (math.isfinite(value) and value > 0) for value in limits):
        parser.error('time and storage limits must be finite and positive')
    return args


def main():
    args = arguments()
    jobs = matrix()
    if args.case is not None:
        selected = next((job for job in jobs if job['Id'] == args.case), None)
        if selected is None:
            raise ValueError(f'Unknown match ID {args.case}. See make overnight-domination-plan.')
        if args.difficulty is not None:
            if selected['Phase'] == 'difficulty-duel':
                raise ValueError('Use --case alone for mixed-difficulty duels to preserve both scheduled levels.')
            selected['Difficulties'] = [args.difficulty] * len(selected['Teams'])
        jobs = [selected]
    elif args.difficulty_matrix:
        jobs = [job for job in jobs if job['Phase'] == 'difficulty-duel']
    elif args.smoke:
        cases = (('duel', 'crossings'), ('duel', 'meridian'), ('team', 'meridian'), ('ffa', 'meridian'))
        opening_pair = [j for j in jobs if j['Phase'] == 'opening-duel' and j['Map'] == OPENING_MAPS[0]
                        and set(j['Openings']) == {'double-income', 'air'}]
        jobs = sorted([next(j for j in jobs if (j['Phase'], j['Map']) == case) for case in cases] + opening_pair,
                      key=lambda j: j['Id'])
    settings = dict(Mode='Domination', Speed=args.speed, HorizonTicks=150 if args.smoke else args.horizon_minutes * 1800,
                    MatchWallSeconds=args.match_wall_minutes * 60, StallSeconds=args.stall_seconds,
                    OpeningCatalog=list(OPENINGS))
    counts = dict(Counter(j['Phase'] for j in jobs))
    if args.plan:
        print(json.dumps({**settings, 'Counts': counts, 'Jobs': jobs}, indent=2))
        return 0
    if git('status', '--porcelain'):
        raise ValueError('Commit tracked and untracked project changes before recording a source-bound campaign.')
    revision = git('rev-parse', 'HEAD')
    binding = {**settings, 'Revision': revision}
    config = {'Schema': 3, 'Settings': binding, 'Jobs': jobs}
    if args.resume:
        root = resume_campaign(CAMPAIGNS, config)
    elif args.out is not None:
        root = args.out.resolve()
        root.mkdir(parents=True, exist_ok=True)
    else:
        root = new_campaign(CAMPAIGNS, datetime.now().date().isoformat())
    with (root / '.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise ValueError(f'Campaign is still running or shutting down at {root}. Try again after it stops.') from None
        print(f'Campaign: {root}', flush=True)
        budget = StorageBudget(root, args)
        budget.check(force=True)
        with closing(sqlite3.connect(root / 'matches.sqlite')) as db:
            db.execute('PRAGMA foreign_keys=ON')
            initialize(db, config, jobs)
            started = time.monotonic()
            with db:
                db.execute("UPDATE campaign_runs SET outcome='interrupted' WHERE outcome='running'")
                run_id = db.execute("INSERT INTO campaign_runs (started_utc, outcome) VALUES (?, 'running')",
                                    (utc(),)).lastrowid
            progress = CampaignProgress(db, root, jobs, started)
            reason = 'controller-error'
            try:
                progress.report('recovering', force=True)
                for directory in sorted(root.glob('batches/[0-9]*/[0-9]*')):
                    if directory.is_dir() and directory.name.isdecimal() and directory.parent.name.isdecimal():
                        collect(db, root, directory, budget, 'interrupted')
                deadline = None if args.hours is None else time.monotonic() + args.hours * 3600
                reason = 'wall-budget'
                while deadline is None or time.monotonic() < deadline:
                    if source_changed(revision):
                        reason = 'source-drift'
                        break
                    pending_ids = {row[0] for row in db.execute("SELECT id FROM matches WHERE status='pending'")}
                    pending = [job for job in jobs if job['Id'] in pending_ids]
                    if not pending:
                        reason = 'done'
                        break
                    reason, restart = run_batch(db, root, binding, pending, args, deadline, budget, lock.fileno(), progress)
                    if not restart:
                        break
                remaining = db.execute("SELECT count(*) FROM matches WHERE status='pending'").fetchone()[0]
                if reason in ('done', 'wall-budget') and source_changed(revision):
                    reason = 'source-drift'
                failures = flagged_matches(db)
                slow = db.execute('SELECT count(DISTINCT match_id) FROM severe_tps').fetchone()[0]
                stalled = db.execute('SELECT count(DISTINCT match_id) FROM unit_stalls').fetchone()[0]
                print(f'{utc()}  {reason}; pending={remaining}, flagged={failures}, '
                      f'matches_with_severe_tps={slow}, matches_with_unit_stalls={stalled}; '
                      f'{root / "matches.sqlite"}', flush=True)
                if reason in ('done', 'wall-budget'):
                    return 3 if failures else 0
                return 130 if reason == 'interrupted' else 2
            except KeyboardInterrupt:
                reason = 'interrupted'
                raise
            except StorageLimit as limit:
                reason = limit.reason
                raise
            except Exception:
                reason = 'controller-error'
                raise
            finally:
                progress.report(reason, force=True, announce=False)
                report_timing(db, root, run_id, started, reason)


def interrupted(signum, frame):
    raise KeyboardInterrupt


if __name__ == '__main__':
    signal.signal(signal.SIGTERM, interrupted)
    try:
        if sys.argv[1:2] == ['--supervise-editor']:
            raise SystemExit(supervise(sys.argv[2:]))
        raise SystemExit(main())
    except KeyboardInterrupt:
        raise SystemExit(130)
    except (OSError, ValueError, sqlite3.Error, subprocess.CalledProcessError, StorageLimit) as error:
        raise SystemExit(f'overnight-domination: {error}')
