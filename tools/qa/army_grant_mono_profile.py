#!/usr/bin/env python3
"""One fixed embedded-Mono arrival Work grant profile at the pinned runtime."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tarfile

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
SOURCE_PIN = '13cf1397f81b24f6369faab8f0fce91ab569075a'
QA_PIN = '7c01ffcd7d8fa8dcfd72a20cea91123e5230908c'
ORIGINAL_RUNTIME = '83fff80bbdb50312238fda558bdef01f3c631e64'
DELTA = {
    'unity/Assets/Spacewars/Runtime/NavigationCrowdNameCatalog.cs',
    'unity/Assets/Spacewars/Runtime/NavigationCrowdStorage.cs',
    'unity/Assets/Spacewars/Runtime/PlayableDomain.cs',
    'unity/Assets/Spacewars/Simulation/NavigationCrowd.cs',
    'unity/Assets/Spacewars/Simulation/NavigationLiveCrowdStorage.cs',
}
NAME = 'ai-advance-subphase-attempt02'
SCHEDULER_SOURCE = 'unity/Assets/Spacewars/Simulation/LayeredNavigationSchedule.cs'
PRIOR_RAW = Path('/Users/eaafonasev/Projects/spacewars-planning/evidence/native-strategic-ai/frontier-materialization-20261018/arrival-grant-profile-01a153d8-raw.tar.gz')
PRIOR_RAW_SHA256 = '0181a5e3bd878f344d5c34a7d86feccd958dc676ae2866fe65471c90b23c9edc'
ADAPTER_DELTA = {
    'tools/qa/army_grant_mono_profile.py',
    'tools/qa/army_grant_mono_profile.cs',
}
APPROVED_QA_BLOBS = [
    'army_input_mono_qualification.py', 'army_input_mono_qualification.cs',
    'army_input_mono_qualification_host.c', 'army_reconstruction_mono_census.py',
    'army_reconstruction_mono_census.cs',
]
PHASES = ['scopeAdmission', 'ownedReservationAndForkOwned', 'advanceInclusive',
          'sealPrepared', 'attachAndCommit', 'scopeDisposalAndPreviousWorkRelease']
CHANNELS = ['wallNanoseconds', 'processCpuNanoseconds', 'threadCpuNanoseconds']


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT)


def validate_checkout_state(source_is_ancestor, tracked_delta, untracked_paths):
    if not source_is_ancestor:
        raise ValueError('source pin is not an ancestor of HEAD')
    if set(tracked_delta) != ADAPTER_DELTA:
        raise ValueError('checkout delta must contain exactly the two QA adapter files')
    if set(untracked_paths):
        raise ValueError('checkout contains untracked files')


def validate_checkout():
    ancestry = subprocess.run(['git', 'merge-base', '--is-ancestor', SOURCE_PIN, 'HEAD'], cwd=ROOT)
    changed = git('diff', '--name-only', SOURCE_PIN, '--').decode().splitlines()
    untracked = git('ls-files', '--others', '--exclude-standard').decode().splitlines()
    validate_checkout_state(ancestry.returncode == 0, changed, untracked)


def final_exit_code(native_returncode, profile_status):
    return 0 if native_returncode == 0 and profile_status == 'PASS_ARRIVAL_WORK_GRANT_PROFILE' else 2


def instrument_scheduler(source):
    """Add QA-only clocks to one exact temporary compile copy."""
    paid = [
        ('job.Demand.StepBookkeeping();', '0'),
        ('job.Demand.StepGraph();', '1'),
        ('job.Demand.StepConnector();', '2'),
    ]
    start = 'if(job.Request.DemandQuery&&job.Phase!=JobPhase.Direct&&job.Demand?.CertificateInsufficient!=true){'
    end = '                    UpdatePeak();continue;\n                }\n                if (job.Phase == JobPhase.Graph) {'
    if source.count(start) != 1 or source.count(end) != 1:
        raise ValueError('demand branch instrumentation anchor is missing or ambiguous')
    source = source.replace(start, start + 'ArmyInputMonoQualification.NavAdvanceProfile.BeginAdvance();try{', 1)
    source = source.replace(end,
        '                    UpdatePeak();continue;\n                    }finally{ArmyInputMonoQualification.NavAdvanceProfile.EndAdvance();}\n                }\n                if (job.Phase == JobPhase.Graph) {', 1)
    for call, bucket in paid:
        instrumented = f'ArmyInputMonoQualification.NavAdvanceProfile.BeginPaid({bucket});{call}ArmyInputMonoQualification.NavAdvanceProfile.EndPaid({bucket});'
        if source.count(call) != 1:
            raise ValueError('paid step instrumentation anchor is missing or ambiguous: ' + call)
        source = source.replace(call, instrumented, 1)
    return source


def compare_prior_trajectory(out):
    raw = PRIOR_RAW.read_bytes()
    if hashlib.sha256(raw).hexdigest() != PRIOR_RAW_SHA256:
        raise ValueError('saved prior raw SHA mismatch; trajectory comparison unavailable')
    prefix = 'ai-grant-mono-profile-01/'
    with tarfile.open(fileobj=__import__('io').BytesIO(raw), mode='r:gz') as archive:
        def prior(name):
            member = archive.extractfile(prefix + name)
            if member is None:
                raise ValueError('prior trajectory artifact missing: ' + name)
            return json.loads(member.read())
        old_services_member = archive.extractfile(prefix + 'services.ndjson')
        if old_services_member is None:
            raise ValueError('prior service progression missing')
        old_services = [json.loads(line) for line in old_services_member.read().splitlines()]
        old_profile = prior('grant-profile.json')
        old_census = prior('census-result.json')
        old_proofs = {name: prior('proof-' + name + '.json') for name in ('10', '12')}
        old_route = prior('final-route14.json')
    new_services = [json.loads(line) for line in (out / 'services.ndjson').read_text().splitlines()]
    new_census = json.loads((out / 'census-result.json').read_text())
    new_proofs = {name: json.loads((out / ('proof-' + name + '.json')).read_text()) for name in ('10', '12')}
    new_route = json.loads((out / 'final-route14.json').read_text())
    comparisons = {
        'all173ServiceRowsExact': len(old_services) == len(new_services) == 173 and old_services == new_services,
        'all173PriorGrantRowsPresent': old_profile.get('grants') == 173 and len(new_services) == 173,
        'preflightExact': old_census.get('preflight') == new_census.get('preflight'),
        'milestoneFingerprintExact': old_census.get('milestone', {}).get('actual') == new_census.get('milestone', {}).get('actual'),
        'proof10And12Exact': old_proofs == new_proofs,
        'finalTypedRoute14Exact': old_route == new_route,
        'finalReadyAndLedgerZero': new_census.get('status') == 'PASS_EXACT_PREFIX_AND_RECONSTRUCTION_TAIL_CENSUS' and
            new_route.get('EntityId') == 14 and len(new_route.get('legs', [])) == new_census.get('tail', {}).get('final14', {}).get('legCount') and
            new_census.get('terminalLedger') == {'PhysicalBytes': 0, 'HeldBytes': 0, 'ActiveScopes': 0},
    }
    return {'status': 'PASS' if all(comparisons.values()) else 'FAIL',
            'priorRawSha256': PRIOR_RAW_SHA256, 'priorSourcePin': SOURCE_PIN,
            'checks': comparisons}


def validate_profile_rows(profile):
    rows = profile.get('measuredGrantRows')
    if not isinstance(rows, list) or profile.get('grants') != len(rows):
        raise ValueError('profile grant row count is missing or inconsistent')
    samples = {phase: {channel: [] for channel in CHANNELS} for phase in PHASES}
    for index, row in enumerate(rows):
        if row.get('service') != index or not isinstance(row.get('phases'), dict):
            raise ValueError('profile service rows are not a contiguous trajectory')
        for phase in PHASES:
            values = row['phases'].get(phase)
            if not isinstance(values, dict):
                raise ValueError(f'missing measured phase {phase} in row {index}')
            for channel in CHANNELS:
                value = values.get(channel)
                if isinstance(value, bool) or not isinstance(value, int) or value < 0:
                    raise ValueError(f'negative or invalid clock delta at row {index} {phase} {channel}')
                samples[phase][channel].append(value)
    aggregate = profile.get('aggregate')
    if not isinstance(aggregate, dict):
        raise ValueError('profile phase aggregates are missing')
    for phase in PHASES:
        for channel in CHANNELS:
            ordered = sorted(samples[phase][channel])
            expected = {'sum': sum(ordered), 'min': ordered[0] if ordered else 0,
                        'median': ordered[len(ordered) // 2] if ordered else 0,
                        'max': ordered[-1] if ordered else 0}
            if aggregate.get(phase, {}).get(channel) != expected:
                raise ValueError(f'aggregate mismatch for {phase} {channel}')
    return {'rows': len(rows), 'phases': len(PHASES), 'channels': len(CHANNELS),
            'allDeltasNonnegative': True, 'aggregatesMatch': True}


def validate_report_preflight(out):
    subphase = json.loads((out / 'subphase-report-preflight.json').read_text())
    outer = json.loads((out / 'grant-profile-report-preflight.json').read_text())
    expected_buckets = {'advanceInclusiveDemandBranch', 'StepBookkeeping', 'StepGraph', 'StepConnector'}
    buckets = subphase.get('buckets', {})
    zero_aggregate = {'sum': 0, 'min': 0, 'median': 0, 'max': 0}
    okay = (subphase.get('clockCalls') == 0 and len(subphase.get('grants', [])) == 600 and set(buckets) == expected_buckets and
        all(v.get('calls') == 0 and all(c == zero_aggregate for c in v.get('perGrant', {}).values()) for v in buckets.values()) and
        all(all(not any(row['buckets'][name][channel] != 0 for channel in ('wallNanoseconds', 'processCpuNanoseconds', 'threadCpuNanoseconds', 'calls'))
                for name in expected_buckets) and row['demandBranchCalls'] == 0 for row in subphase['grants']) and
        outer.get('status') == 'REPORT_PREFLIGHT_ONLY' and outer.get('grants') == 0 and
        set(outer.get('aggregate', {})) == set(PHASES) and
        all(all(v == zero_aggregate for v in outer['aggregate'][phase].values()) for phase in PHASES))
    if not okay:
        raise ValueError('actual C# report preflight data shape, call counts, or zero accumulators failed validation')
    return {'status': 'PASS', 'actualCSharpReportImplementation': True, 'zeroAccumulatorRows': len(subphase['grants']),
            'subphaseBuckets': sorted(expected_buckets), 'outerGrantProfileSerializer': 'PASS',
            'subphaseReportBytes': (out / 'subphase-report-preflight.json').stat().st_size,
            'outerGrantProfileSerializerBytes': (out / 'grant-profile-report-preflight.json').stat().st_size}


def inverse_census_instrumentation(profile_source, historic_source):
    source = profile_source.replace('using System.Runtime.InteropServices;\n', '')
    profile_entry = ('internal static class ArmyGrantMonoProfile {\n    static int Main(string[] args) => args.Length == 1 && args[0] == "--clock-guard-self-test"\n'
        '        ? ArmyInputMonoQualification.RunClockGuardSelfTest()\n'
        '        : args.Length >= 2 && args[0] == "--report-preflight-only"\n'
        '            ? ArmyInputMonoQualification.RunReportPreflight(args[1]) : ArmyInputMonoQualification.RunProfile(args);\n}')
    if source.count(profile_entry) != 1:
        raise ValueError('profile entrypoint is missing or ambiguous during inverse validation')
    source = source.replace(profile_entry,
        'internal static class ArmyReconstructionMonoCensus {\n    static int Main(string[] args) => ArmyInputMonoQualification.RunCensus(args);\n}', 1)
    begin = source.index('\n    [StructLayout(LayoutKind.Sequential)] struct Timespec')
    closing = source.rfind('\n}')
    source = source[:begin] + source[closing:]
    source = source.replace('\n\n}', '\n}')
    replacements = [
        ('Begin(service, 0); bool admitted = budget.TryOpenScope(NavigationPreparationKind.Solve, 1, service + 1, out var scope); End(service, 0); Require(admitted, "normal service scope");',
         'Require(budget.TryOpenScope(NavigationPreparationKind.Solve, 1, service + 1, out var scope), "normal service scope");'),
        ('Begin(service, 1); var reservation = scope.OwnedReservation(); next = w.ForkOwned(reservation); End(service, 1);',
         'next = w.ForkOwned(scope.OwnedReservation());'),
        ('Begin(service, 2); next.Advance(service, 256, new NavEpochQuota(256, 256, 256, 256, 256)); End(service, 2);',
         'next.Advance(service, 256, new NavEpochQuota(256, 256, 256, 256, 256));'),
        ('Begin(service, 3); next.SealPrepared(); End(service, 3); Begin(service, 4); scope.Attach(next); scope.Commit(next, _ => {}); End(service, 4);\n                            Begin(service, 5);',
         'next.SealPrepared(); scope.Attach(next); scope.Commit(next, _ => {});'),
        ('w.Release(); w = next; End(service, 5);', 'w.Release(); w = next;'),
    ]
    for instrumented, historic in replacements:
        if source.count(instrumented) != 1:
            raise ValueError('inverse instrumentation hook is missing or ambiguous')
        source = source.replace(instrumented, historic, 1)
    service_marker = 'NavAdvanceProfile.Service(service); '
    if source.count(service_marker) != 1:
        raise ValueError('Advance service-row marker is missing or ambiguous')
    source = source.replace(service_marker, '', 1)
    if source != historic_source:
        raise ValueError('instrumented census differs from the historic helper outside timer hooks')
    return True


def load_module(raw, path, name):
    import types
    module = types.ModuleType(name)
    module.__file__ = str(path)
    exec(compile(raw, str(path), 'exec'), module.__dict__)
    return module


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-pin', required=True)
    parser.add_argument('--qa-pin', required=True)
    parser.add_argument('--out', required=True, type=Path)
    parser.add_argument('--report-preflight-only', action='store_true')
    args = parser.parse_args()
    out = args.out.resolve()
    if args.source_pin != SOURCE_PIN or args.qa_pin != QA_PIN:
        parser.error('only the reviewed source and QA pins are accepted')
    expected_name = 'ai-advance-subphase-preflight02' if args.report_preflight_only else NAME
    if out.parent != ROOT / '.local' or out.name != expected_name:
        parser.error('output must be the owned .local/' + expected_name + ' directory')
    try:
        validate_checkout()
    except ValueError as exc:
        parser.error(str(exc))
    if out.exists():
        parser.error('owned output already exists; refusing to overwrite evidence')

    qadir = out / 'qa-input'
    # Exactly the five approved historical blobs: input py/cs, reconstruction
    # Mono census py/cs, and the original embedded-Mono native host.
    qa_names = APPROVED_QA_BLOBS
    qa_blobs = {}
    qa_raw = {}
    for name in qa_names:
        raw = git('show', f'{QA_PIN}:tools/qa/{name}')
        qa_raw[name] = raw
        qa_blobs[name] = hashlib.sha256(raw).hexdigest()
    q = load_module(qa_raw['army_input_mono_qualification.py'], ROOT / 'tools/qa/army_input_mono_qualification.py', 'arrival_grant_qualification')
    sys.modules['army_input_mono_qualification'] = q
    census = load_module(qa_raw['army_reconstruction_mono_census.py'], ROOT / 'tools/qa/army_reconstruction_mono_census.py', 'arrival_grant_census')
    inverse_census_instrumentation(Path(__file__).with_suffix('.cs').read_text(), qa_raw['army_reconstruction_mono_census.cs'].decode())

    report = {'classification': 'FIXED_EMBEDDED_MONO_ARRIVAL_WORK_GRANT_PROFILE',
              'status': 'NOT_QUALIFIED', 'stage': 'pinned source and QA preparation',
              'sourcePin': SOURCE_PIN, 'qaPin': QA_PIN,
              'originalProducerRuntime': ORIGINAL_RUNTIME,
            'hostExternalAuthorityReservation': 'NotMeasured',
              'trajectory': 'one fixed trajectory, maximum 600 service epochs; no variants or warmups'}
    out.mkdir(exist_ok=False)
    qadir.mkdir()
    for name, raw in qa_raw.items():
        (qadir / name).write_bytes(raw)
    (out / 'source').mkdir()
    sources = []
    try:
        names = sorted(n for n in git('ls-tree', '-r', '--name-only', SOURCE_PIN,
                                      'unity/Assets/Spacewars/Runtime',
                                      'unity/Assets/Spacewars/Simulation').decode().splitlines()
                       if n.endswith('.cs'))
        if len(names) != 316:
            raise RuntimeError(f'expected 316 production source files, found {len(names)}')
        baseline_names = sorted(n for n in git('ls-tree', '-r', '--name-only', ORIGINAL_RUNTIME,
                                               'unity/Assets/Spacewars/Runtime',
                                               'unity/Assets/Spacewars/Simulation').decode().splitlines()
                               if n.endswith('.cs'))
        if names != baseline_names:
            raise RuntimeError('production source path set differs from original producer')
        actual_delta = set(git('diff', '--name-only', ORIGINAL_RUNTIME, SOURCE_PIN,
                               '--', 'unity/Assets/Spacewars/Runtime',
                               'unity/Assets/Spacewars/Simulation').decode().splitlines())
        if actual_delta != DELTA:
            raise RuntimeError('runtime delta differs from the reviewed five-file memory fix')
        patch = git('diff', '--binary', ORIGINAL_RUNTIME, SOURCE_PIN, '--', *sorted(DELTA))
        report['runtimePatchSHA256'] = q.sha(patch)
        report['runtimeDelta'] = []
        for name in names:
            raw = git('show', f'{SOURCE_PIN}:{name}')
            baseline = git('show', f'{ORIGINAL_RUNTIME}:{name}')
            if name not in DELTA and raw != baseline:
                raise RuntimeError('unexpected non-delta source change: ' + name)
            target = out / 'source' / name.replace('/', '__')
            target.write_bytes(raw)
            row = {'path': name, 'sha256': q.sha(raw), 'gitBlob': git('rev-parse', f'{SOURCE_PIN}:{name}').decode().strip(), 'compiledPath': str(target)}
            sources.append(row)
            if name in DELTA:
                report['runtimeDelta'].append({'path': name, 'candidateSHA256': row['sha256'], 'originalSHA256': q.sha(baseline)})

        scheduler = next(row for row in sources if row['path'] == SCHEDULER_SOURCE)
        compiled_source = instrument_scheduler(Path(scheduler['compiledPath']).read_text())
        compiled_dir = out / 'compiled-source'
        compiled_dir.mkdir()
        compiled_path = compiled_dir / 'LayeredNavigationSchedule.cs'
        compiled_path.write_text(compiled_source)
        scheduler['compilePath'] = str(compiled_path)
        scheduler['compileSha256'] = q.sha(compiled_path.read_bytes())
        report['qaOnlySchedulerInstrumentation'] = {
            'productionPath': SCHEDULER_SOURCE,
            'productionSourceSha256': scheduler['sha256'],
            'productionGitBlob': scheduler['gitBlob'],
            'compiledCopy': str(compiled_path),
            'compiledCopySha256': scheduler['compileSha256'],
            'instrumentation': 'demand branch inclusive; three mutually exclusive paid Step calls; no production file changes'
        }

        original, full, work, reps, actors = q.read_inputs(out)
        (out / 'Fixture.cs').write_text(q.fixture(full, work, reps, actors))
        census.oracle_fixture(out)
        (out / 'qa-plan.json').write_text(json.dumps({
            'baseline': SOURCE_PIN, 'originalProducerRuntime': ORIGINAL_RUNTIME,
            'diff': 'Exact reviewed five-file runtime delta; QA helpers are exact blobs from QA pin',
            'runKind': 'actual C# report preflight only; no solver' if args.report_preflight_only else 'one fixed 173-grant trajectory',
            'command': ['python3', str(Path(__file__).resolve()), *(['--report-preflight-only'] if args.report_preflight_only else []), '--source-pin', SOURCE_PIN,
                        '--qa-pin', QA_PIN, '--out', str(out)],
            'reason': 'Actual C# zero-accumulator report-only preflight.' if args.report_preflight_only else
                      'Single isolated embedded-Mono component trajectory; no Unity process or game-host performance claim.',
            'contract': {'policy': 21, 'quota': [256, 256, 256, 256, 256], 'serviceEpochCap': 600,
                         'retention': 'default', 'firstMismatch': 'stop and retain partial NOT_QUALIFIED'},
            'phases': ['scope admission', 'owned reservation plus ForkOwned', 'entire Advance',
                       'SealPrepared', 'Attach plus Commit', 'scope disposal plus previous Work release'],
            'subphaseAttribution': ['Advance demand branch inclusive', 'StepBookkeeping', 'StepGraph', 'StepConnector',
                                    'scheduler-overhead remainder is Advance branch inclusive less those nonoverlapping paid calls',
                                    'clock calls counted; calibration retained; no assumed overhead subtraction'],
            'limits': ['host external authority reservation NotMeasured', 'empty callback is not host publication',
                       'geometry and copy-on-write are not separated',
                       'process CPU includes background Mono GC/JIT; CPU minus thread CPU is not GC time',
                       'observer and gate interval NotMeasured; not inferred from total process wall',
                       'no Unity Editor, Player, game-thread, threaded-host, geometry-9, current revalidation, projection, build, or deploy']
        }, indent=2) + '\n')
        refs = [q.CORE / name for name in ['mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Numerics.dll']]
        deps = [q.DYLIB, q.CORE / 'mscorlib.dll', q.CONTENTS / 'Frameworks/libmono-native.dylib',
                q.CONTENTS / 'Frameworks/libMonoPosixHelper.dylib', q.DOTNET, q.CSC] + refs[1:]
        deps += sorted((q.MONO / 'etc').rglob('*'))
        q.write_json(out / 'bindings.json', {'sourcePin': SOURCE_PIN, 'originalProducerRuntime': ORIGINAL_RUNTIME,
                     'runtimePatchSHA256': report['runtimePatchSHA256'], 'source': sources,
                     'qaBlobs': qa_blobs,
                     'runtimeAndCompiler': [{'path': str(p), 'sha256': q.sha(p.read_bytes())} for p in deps if p.is_file()],
                     'runtimeRoute': 'cross-compile with pinned Roslyn; execute only embedded Unity Mono'})
        env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1')
        for key in ['MONO_OPTIONS', 'MONO_ENV_OPTIONS', 'MONO_PATH', 'MONO_CFG_DIR']:
            env.pop(key, None)
        report['stage'] = 'native host compilation'
        command = ['/usr/bin/clang', '-arch', 'arm64', '-Wall', '-Wextra', '-Werror', str(qadir / 'army_input_mono_qualification_host.c'), '-o', str(out / 'mono-host')]
        if q.run(out, 'compile-host', command, env):
            raise RuntimeError('native helper compile failed')
        report['stage'] = 'managed profile compilation'
        command = [str(q.DOTNET), str(q.CSC), '/noconfig', '/nostdlib+', '/langversion:latest', '/target:exe',
                   '/main:ArmyGrantMonoProfile', '/deterministic+', '/debug:portable', '/out:' + str(out / 'profile.exe')]
        command += ['/reference:' + str(p) for p in refs]
        # The new profile source carries the exact census helper body plus the
        # instrumentation, so compile it once alongside the original input helper.
        command += [str(qadir / 'army_input_mono_qualification.cs'),
                    str(Path(__file__).with_name('army_grant_mono_profile.cs')), str(out / 'Fixture.cs'), str(out / 'OracleFixture.cs')]
        command += [row.get('compilePath', row['compiledPath']) for row in sources]
        if q.run(out, 'compile-managed', command, env):
            raise RuntimeError('managed profile compile failed')
        q.write_json(out / 'compiled-bindings.json', [{'path': p.name, 'sha256': q.sha(p.read_bytes())}
                    for p in [out / 'mono-host', out / 'profile.exe', out / 'profile.pdb']])
        (out / 'army_grant_mono_profile.py').write_bytes(Path(__file__).read_bytes())
        (out / 'army_grant_mono_profile.cs').write_bytes(Path(__file__).with_suffix('.cs').read_bytes())
        q.write_json(out / 'profile-source-bindings.json', [
            {'path': name, 'sha256': q.sha((out / name).read_bytes())}
            for name in ['army_grant_mono_profile.py', 'army_grant_mono_profile.cs']])
        report['stage'] = 'one fixed embedded Mono trajectory'
        profile_args = ['--report-preflight-only', str(out)] if args.report_preflight_only else [str(out)]
        command = [str(out / 'mono-host'), str(q.DYLIB), str(q.MONO / 'lib'), str(q.MONO / 'etc'),
                   str(q.CORE), str(out / 'profile.exe'), *profile_args, str(q.CORE / 'mscorlib.dll')]
        rc = q.run(out, 'profile', command, env)
        report['returncode'] = rc
        if args.report_preflight_only:
            checks = [out / 'subphase-report-preflight.json', out / 'grant-profile-report-preflight.json']
            report['reportPreflight'] = validate_report_preflight(out) if all(p.is_file() for p in checks) else {'status': 'NOT_RUN', 'reason': 'C# preflight report files missing'}
            report['status'] = 'REPORT_PREFLIGHT_PASS' if rc == 0 and report['reportPreflight'].get('status') == 'PASS' else 'REPORT_PREFLIGHT_FAIL'
            report['stage'] = 'actual C# zero-accumulator report preflight; no solver invoked'
            result = None
        else:
            result = out / 'census-result.json'
        if not args.report_preflight_only and result.is_file():
            report['profileResult'] = json.loads(result.read_text())
            grant_result = out / 'grant-profile.json'
            if grant_result.is_file():
                report['grantProfile'] = json.loads(grant_result.read_text())
                report['clockValidation'] = validate_profile_rows(report['grantProfile'])
                report['status'] = report['grantProfile'].get('status', 'NOT_QUALIFIED')
                report['subphaseProfile'] = json.loads((out / 'subphase-profile.json').read_text())
                report['priorTrajectoryComparison'] = compare_prior_trajectory(out)
                if report['priorTrajectoryComparison']['status'] != 'PASS':
                    report['status'] = 'NOT_QUALIFIED_PRIOR_TRAJECTORY_MISMATCH'
            else:
                report['status'] = 'NOT_QUALIFIED'
        elif not args.report_preflight_only:
            report['launchBlocker'] = 'No managed result; no fallback runtime attempted'
    except Exception as exc:
        report['failure'] = repr(exc)
    finally:
        report['runtimeSourceChanged'] = []
        q.write_json(out / 'result.json', report)
        if (out / 'subphase-profile.json').is_file():
            phase = json.loads((out / 'subphase-profile.json').read_text())
            advance = phase['buckets']['advanceInclusiveDemandBranch']['perGrant']['threadCpuNanoseconds']['sum']
            parts = {name: phase['buckets'][name]['perGrant']['threadCpuNanoseconds']['sum']
                     for name in ('StepBookkeeping', 'StepGraph', 'StepConnector')}
            remainders = [row['schedulerOverheadRemainderThreadCpuNanoseconds'] for row in phase['grants']]
            subphase_summary = {'threadCpuNanoseconds': {'advanceInclusive': advance, **parts,
                'schedulerOverheadRemainder': sum(remainders)},
                'percentOfAdvance': {'StepBookkeeping': 100 * parts['StepBookkeeping'] / advance if advance else None,
                    'StepGraph': 100 * parts['StepGraph'] / advance if advance else None,
                    'StepConnector': 100 * parts['StepConnector'] / advance if advance else None,
                    'schedulerOverheadRemainder': 100 * sum(remainders) / advance if advance else None},
                'clockCalls': {'subphase': phase['clockCalls'], 'existingProfilePhases': report.get('grantProfile', {}).get('phaseClockCalls'),
                    'calibrationGetTime': report.get('grantProfile', {}).get('calibrationClockCalls'), 'calibrationGetRes': 3,
                    'totalGetTimeAndGetRes': phase['clockCalls'] + report.get('grantProfile', {}).get('phaseClockCalls', 0) +
                        report.get('grantProfile', {}).get('calibrationClockCalls', 0) + 3},
                'calibration': report.get('grantProfile', {}).get('clock'),
                'observerOverhead': 'Not subtracted; measured clock calls remain inside buckets'}
            summary = {'classification': 'ONE_FIXED_EMBEDDED_MONO_ARRIVAL_WORK_SUBPHASE_PROFILE',
                'status': report.get('status'), 'sourcePin': SOURCE_PIN, 'qaPin': QA_PIN,
                'candidateAndCompiledBindings': report.get('qaOnlySchedulerInstrumentation'),
                'priorTrajectoryComparison': report.get('priorTrajectoryComparison'),
                'subphase': subphase_summary,
                'qualification': 'Component-only C# Mono profile; not Unity, game thread, threaded host, CPU-real-host or Army acceptance',
                'openLimits': ['scheduler-overhead remainder includes loop/scheduling plus timer observer cost',
                    'clock reads are sequential; no assumed clock overhead subtraction',
                    'geometry and container/arithmetic attribution remain unmeasured',
                    'thread CPU does not establish real-host cadence or Army movement']}
            (out / 'evidence-summary.json').write_text(json.dumps(summary, indent=2) + '\n')
            shares = subphase_summary['percentOfAdvance']
            dominant = max((k for k in ('StepBookkeeping', 'StepGraph', 'StepConnector', 'schedulerOverheadRemainder')),
                           key=lambda k: shares[k] if shares[k] is not None else -1)
            markdown = ('# Arrival work Advance subphase profile\n\n'
                f"Status: **{report.get('status')}**. Source `{SOURCE_PIN}`; QA `{QA_PIN}`.\n\n"
                f"Dominant measured bucket: **{dominant}**, {shares[dominant]:.2f}% of demand-branch Advance thread CPU.\n\n"
                f"Advance inclusive: {advance:,} ns thread CPU across {len(phase['grants'])} grants. "
                f"Subphase clock calls: {phase['clockCalls']}; all clock overhead remains in the buckets.\n\n"
                'The remainder is scheduler/loop work plus timer observer cost. Geometry/container/arithmetic were not separated. '
                'This embedded-Mono component result does not qualify the real threaded host or Army movement.\n')
            (out / 'evidence-summary.md').write_text(markdown)
            raw_archive = out / 'raw.tar.gz'
            with tarfile.open(raw_archive, 'w:gz') as archive:
                for path in sorted(out.rglob('*')):
                    if path.is_file() and path.name not in ('raw.tar.gz', 'evidence-summary.json', 'evidence-summary.md'):
                        archive.add(path, arcname=str(path.relative_to(out)))
        print(json.dumps(report, indent=2))
    if args.report_preflight_only:
        return 0 if report.get('returncode') == 0 and report.get('status') == 'REPORT_PREFLIGHT_PASS' else 2
    return final_exit_code(report.get('returncode'), report.get('status'))


if __name__ == '__main__':
    raise SystemExit(main())
