import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import Mock, patch

import route_performance_qa as driver
from qa.startup_stack_probe import StartupStackProbe, frame_state, verify_app, is_owned, _BsdInfo

HEADER = 'elapsed,render_frame,tick,stopwatch_stamp,wall_frame_ms\n'


class StartupProbeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name).resolve()
        self.exe = self.output / 'frozen-context/unity/Builds/macOS/SpacewarsPlayable.app/Contents/MacOS/Spacewars'
        self.now = [0.0]
        self.identity = {'pid': 42, 'ppid': 10, 'birth': [100, 123456],
                         'executable': str(self.exe),
                         'command': f'{self.exe} -noaudio -muted -playableEvidence {self.output} -routePerformanceEvidence'}
        self.identify = Mock(side_effect=lambda pid: dict(self.identity))
        self.sampler = Mock(return_value=Mock(returncode=0))

    def probe(self, **kwargs):
        return StartupStackProbe(self.output, self.exe, 42, 10, kwargs.get('runner_pid', 10),
                                 clock=lambda: self.now[0], identify=self.identify, sampler=self.sampler)

    def receipt(self):
        return json.loads((self.output/'startup-stack-probe.json').read_text())

    def test_once_at90_with_unknown_csv_and_raw_artifacts(self):
        p = self.probe()
        self.now[0] = 89.99; p.check(0, 120, True)
        self.sampler.assert_not_called()
        self.now[0] = 90; p.check(0, 120, True); p.check(0, 120, True)
        self.sampler.assert_called_once()
        args, kwargs = self.sampler.call_args
        self.assertEqual(['/usr/bin/sample', '42', '1', '1', '-file', str(self.output/'startup-stack-sample.txt')], args[0])
        self.assertEqual(5, kwargs['timeout'])
        self.assertTrue((self.output/'startup-stack-stdout.txt').exists())
        self.assertTrue((self.output/'startup-stack-stderr.txt').exists())
        r = self.receipt()
        self.assertTrue(r['attempted']); self.assertEqual(0, r['exit_code'])
        self.assertEqual('UNKNOWN', r['activity']); self.assertFalse(r['performance_acceptance'])
        self.assertEqual([42, 42], [call.args[0] for call in self.identify.call_args_list])

    def test_row_states_do_not_invent_zero_activity(self):
        path = self.output/'frames.csv'
        cases = [(HEADER, False), (HEADER+'1,2,3,4,-1\n', True),
                 (HEADER+'1,2,3,4,-1', False), (HEADER+'1,2\n', False),
                 (HEADER+'nan,2,3,4,1\n', False), (HEADER+'1,2,3,0,1\n', False),
                 ('tick\n1\n', False), ('', False),
                 (HEADER+'bad\n1,2,3,4,-1\n', True),
                 (HEADER+'nan,2,3,4,1\n1,2,3,4,-1\n', True),
                 (HEADER+'1,2.5,3,4,1\n', False)]
        self.assertTrue(frame_state(path).startswith('UNKNOWN'))
        for text, trusted in cases:
            with self.subTest(text=text):
                path.write_text(text)
                state = frame_state(path)
                self.assertEqual(trusted, state == 'recorded_frame_row')
                if not trusted:
                    self.assertIn('UNKNOWN', state)

    def test_complete_row_skips_sample_even_without_footer(self):
        (self.output/'native-route-frames.csv').write_text(HEADER+'1,2,3,4,-1\n')
        p=self.probe();self.now[0]=90;p.check(0,120,True)
        self.sampler.assert_not_called();self.assertFalse(self.receipt()['attempted'])
        self.assertIn('row present', self.receipt()['reason'])

    def test_unknown_exited_foreign_or_reused_child_never_sampled(self):
        for mutation in ('exited', 'birth', 'ppid', 'executable', 'command', 'runner'):
            with self.subTest(mutation=mutation):
                self.now[0]=0
                p = self.probe(runner_pid=11 if mutation=='runner' else 10)
                changed = dict(self.identity)
                if mutation=='exited': changed = None
                elif mutation=='birth': changed['birth']=[100,123457]
                elif mutation=='ppid': changed['ppid']=999
                elif mutation=='executable': changed['executable']='/foreign'
                elif mutation=='command': changed['command'] += '-foreign' # removes exact flag
                self.identify.side_effect = None;self.identify.return_value=changed
                self.now[0]=90;p.check(0,120,True)
                self.sampler.assert_not_called();self.assertFalse(self.receipt()['attempted'])
                self.identify.side_effect=lambda pid:dict(self.identity)

    def test_time_budget_and_process_exit_skip(self):
        for deadline, alive in ((96,True),(120,False)):
            p=self.probe();self.now[0]=90;p.check(0,deadline,alive)
            self.assertFalse(self.receipt()['attempted']);self.sampler.assert_not_called()
        self.now[0]=0;p=self.probe();p.finish('Exited before90');self.now[0]=90;p.check(0,120,True)
        self.assertIn('before90',self.receipt()['reason']);self.sampler.assert_not_called()

    def test_identity_query_cost_cannot_extend_hard_deadline(self):
        p=self.probe()
        def delayed(pid):
            self.now[0]=116
            return dict(self.identity)
        self.identify.side_effect=delayed;self.now[0]=90;p.check(0,120,True)
        self.sampler.assert_not_called();self.assertIn('budget',self.receipt()['reason'])

    def test_timeout_nonzero_and_unavailable_have_no_retry(self):
        for outcome in (subprocess.TimeoutExpired('fake-sample',5), OSError('fake-unavailable'), Mock(returncode=7)):
            self.now[0]=0;p=self.probe();self.now[0]=90
            self.sampler.reset_mock();self.sampler.side_effect = outcome if isinstance(outcome,Exception) else None
            self.sampler.return_value=outcome;p.check(0,120,True);p.check(0,120,True)
            self.sampler.assert_called_once();self.assertEqual('unknown',self.receipt()['status'])
            self.assertTrue(self.receipt()['attempted'])

    def test_guard_uses_exact_output_argument_and_quiet_flags(self):
        self.assertTrue(is_owned(self.identity,42,10,self.exe,self.output))
        changed=dict(self.identity)
        changed['command']=changed['command'].replace(str(self.output)+' -route',str(self.output)+'-other -route')
        self.assertFalse(is_owned(changed,42,10,self.exe,self.output))
        self.assertFalse(is_owned(None,42,10,self.exe,self.output))
        self.assertFalse(is_owned({'pid':42},42,10,self.exe,self.output))
        import ctypes
        self.assertEqual(136,ctypes.sizeof(_BsdInfo),'local macOS SDK proc_bsdinfo layout')

    def make_app(self):
        app=self.output/'frozen-context/unity/Builds/macOS/SpacewarsPlayable.app'
        exe=app/'Contents/MacOS/Spacewars';exe.parent.mkdir(parents=True);exe.write_bytes(b'fake executable');exe.chmod(0o755)
        runtime=app/'Contents/Data/runtime';runtime.parent.mkdir();runtime.write_bytes(b'fake runtime')
        members=[{'path':p.relative_to(app).as_posix(),'size':p.stat().st_size,'mode':oct(p.stat().st_mode),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in app.rglob('*') if p.is_file()]
        manifest=self.output/'manifest.json';manifest.write_text(json.dumps({'app':str(app),'source':'fake-source','runtimeSource':'fake-runtime','members':members}))
        return app,manifest,hashlib.sha256(manifest.read_bytes()).hexdigest()

    def test_app_manifest_covers_runtime_modes_member_set_and_digest(self):
        app,manifest,digest=self.make_app()
        self.assertEqual(2,verify_app(app,manifest,digest)['members'])
        with self.assertRaises(ValueError):verify_app(app,manifest,'wrong')
        runtime=app/'Contents/Data/runtime';runtime.write_bytes(b'changed runtime')
        with self.assertRaises(ValueError):verify_app(app,manifest,digest)
        runtime.write_bytes(b'fake runtime');runtime.chmod(0o755)
        with self.assertRaises(ValueError):verify_app(app,manifest,digest)
        runtime.chmod(0o644);extra=app/'unexpected';extra.touch()
        with self.assertRaises(ValueError):verify_app(app,manifest,digest)
        extra.unlink();extra.symlink_to(runtime)
        with self.assertRaises(ValueError):verify_app(app,manifest,digest)

    def test_cli_opt_in_requires_all_identity_and_readout_flags(self):
        with patch.object(driver.subprocess,'Popen') as launch:
            with self.assertRaises(ValueError):driver.run(self.output/'out',7,startup_stack_probe=True)
            with self.assertRaises(ValueError):driver.run(self.output/'out',7,player_app=self.output/'app')
            with patch.object(driver,'ROOT',self.output):
                with self.assertRaisesRegex(ValueError,'default/manual'):
                    driver.run(self.output/'out',7,startup_stack_probe=True,q10_readouts=True,arrival_lifecycle_readouts=True,scenario='eight-player-400-moving',player_app=self.output/'unity/Builds/macOS/SpacewarsPlayable.app',player_app_manifest='fake',player_app_manifest_sha256='fake',player_cwd=self.output)
            launch.assert_not_called()

    def test_invalid_or_missing_probe_cwd_refused_before_launch(self):
        app,manifest,digest=self.make_app()
        with patch.object(driver.subprocess,'Popen') as launch:
            for cwd in (None,self.output/'missing',self.output):
                with self.subTest(cwd=cwd),self.assertRaises(ValueError):
                    driver.run(self.output/'run',7,startup_stack_probe=True,player_app=app,player_app_manifest=manifest,player_app_manifest_sha256=digest,player_cwd=cwd,q10_readouts=True,arrival_lifecycle_readouts=True,scenario='eight-player-400-moving')
            with self.assertRaises(ValueError):
                driver.run(self.output/'run',7,player_cwd=self.output)
            launch.assert_not_called()

    def test_cli_passes_explicit_flags_to_driver_without_launch(self):
        args=['route_performance_qa.py','--output',str(self.output/'run'),'--seed','7',
              '--scenario','eight-player-400-moving','--maximum-wall-seconds','120',
              '--q10-readouts','--arrival-lifecycle-readouts','--startup-stack-probe',
              '--player-app',str(self.output/'frozen.app'),'--player-app-manifest',str(self.output/'manifest.json'),
              '--player-app-manifest-sha256','fake-digest','--player-cwd',str(self.output/'frozen-context')]
        with patch('sys.argv',args),patch.object(driver,'run',return_value=0) as run:
            self.assertEqual(0,driver.main())
        self.assertTrue(run.call_args.kwargs['startup_stack_probe'])
        self.assertEqual('fake-digest',run.call_args.kwargs['player_app_manifest_sha256'])
        self.assertEqual(self.output/'frozen-context',run.call_args.kwargs['player_cwd'])
        self.assertEqual(120,run.call_args.kwargs['maximum_wall_seconds'])

    def run_probe_io_failure(self, constructor):
        app,manifest,digest=self.make_app();output=self.output/'run'
        process=Mock(pid=10);process.poll.return_value=0;process.wait.return_value=0
        def launch(command,**kwargs):
            kwargs['stdout'].write('unity-run: started exclusive; guardian=10 pid=42\n');kwargs['stdout'].flush()
            return process
        real_write=Path.write_text
        def write(path,*args,**kwargs):
            if path.name=='startup-stack-probe.json':
                raise OSError('fake probe disk/permission failure')
            return real_write(path,*args,**kwargs)
        def probe(*args):
            if constructor:
                return StartupStackProbe(*args,clock=lambda:0,identify=lambda pid:None,sampler=self.sampler)
            fake=Mock()
            # Simulate actual finalize save IO error through the same filesystem path.
            fake.finish.side_effect=lambda reason:(output/'startup-stack-probe.json').write_text('fake receipt')
            return fake
        with patch.object(driver,'ROOT',self.output/'code'),patch.object(driver.subprocess,'Popen',side_effect=launch),patch.object(driver,'StartupStackProbe',side_effect=probe),patch.object(Path,'write_text',new=write),patch.object(driver,'close_owned_player',return_value=True) as close,patch.object(driver,'owned_player',return_value=False),patch.object(driver.subprocess,'run') as shell:
            def run():
                return driver.run(output,7,startup_stack_probe=True,player_app=app,player_app_manifest=manifest,player_app_manifest_sha256=digest,player_cwd=self.output/'frozen-context',q10_readouts=True,arrival_lifecycle_readouts=True,scenario='eight-player-400-moving')
            if constructor:
                with self.assertRaisesRegex(OSError,'fake probe'):
                    run()
            else:
                self.assertEqual(1,run())
            close.assert_any_call(42,app/'Contents/MacOS/Spacewars',output)
            shell.assert_not_called()
        terminal=json.loads((output/'terminal.json').read_text())
        self.assertEqual(42,terminal['player_pid']);self.assertFalse(terminal['actual_player_alive'])
        self.assertIn('OSError',terminal['startup_stack_probe_error'])
        self.assertIn('probe',terminal['stop']);self.sampler.assert_not_called()

    def test_finalize_write_failure_preserves_owned_child_cleanup_and_terminal(self):
        self.run_probe_io_failure(constructor=False)

    def test_constructor_save_failure_preserves_owned_child_cleanup_and_terminal(self):
        self.run_probe_io_failure(constructor=True)

    def test_optional_nonfinite_metrics_do_not_hide_trustworthy_frame_row(self):
        path=self.output/'native-route-frames.csv'
        path.write_text(HEADER.rstrip() + ',gpu_ms,cpu_frame_ms\n1,2,3,4,-1,NaN,Infinity\n')
        self.assertEqual('recorded_frame_row',frame_state(path))
        path.write_text(HEADER+'NaN,2,3,4,-1\n')
        self.assertIn('UNKNOWN',frame_state(path))

    def test_driver_fake_clock_uses_exact_frozen_app_and_hard120_no_foreign_scan(self):
        app,manifest,digest=self.make_app();output=self.output/'run'
        process=Mock(pid=10);process.poll.return_value=None
        probes=[];waits=[]
        def sampled(*args,**kwargs):
            self.now[0]+=5
            return Mock(returncode=0)
        self.sampler.side_effect=sampled
        def launch(command,**kwargs):
            self.assertIn(str(app/'Contents/MacOS/Spacewars'),command)
            self.assertEqual(self.output/'frozen-context',kwargs['cwd'])
            self.assertNotIn('build',command);self.assertNotIn('-aiRouteAuditEvidence',command)
            kwargs['stdout'].write('unity-run: started exclusive; guardian=10 pid=42\n');kwargs['stdout'].flush()
            return process
        def wait(timeout):
            waits.append(timeout);self.now[0]+=timeout
            raise subprocess.TimeoutExpired('fake-runner',timeout)
        process.wait.side_effect=wait
        def probe(*args):
            identity=dict(self.identity);identity['command']=f'{self.exe} -noaudio -muted -playableEvidence {output} -routePerformanceEvidence'
            p=StartupStackProbe(*args,clock=lambda:self.now[0],identify=lambda pid:identity,sampler=self.sampler)
            probes.append(p);return p
        # child is intentionally owned only for sampling; cleanup is faked independently.
        with patch.object(driver,'ROOT',self.output/'code'), patch.object(driver.subprocess,'Popen',side_effect=launch), patch.object(driver.time,'monotonic',side_effect=lambda:self.now[0]), patch.object(driver,'StartupStackProbe',side_effect=probe), patch.object(driver,'close_owned_player',return_value=True), patch.object(driver,'owned_player',return_value=False), patch.object(driver.subprocess,'run') as shell:
            # End wrapper on watchdog cleanup without consuming another deadline.
            def signal(_):
                process.poll.return_value=127;process.wait.side_effect=None;process.wait.return_value=127
            process.send_signal.side_effect=signal
            self.assertEqual(1,driver.run(output,7,startup_stack_probe=True,player_app=app,player_app_manifest=manifest,player_app_manifest_sha256=digest,player_cwd=self.output/'frozen-context',q10_readouts=True,arrival_lifecycle_readouts=True,scenario='eight-player-400-moving'))
            shell.assert_not_called()
        self.assertEqual(str(self.output/'frozen-context'),json.loads((output/'player-app-identity.json').read_text())['player_cwd'])
        self.assertEqual(str(self.output/'frozen-context'),json.loads((output/'terminal.json').read_text())['player_cwd'])
        self.assertEqual(120,self.now[0]);self.assertEqual(1,len(probes));self.sampler.assert_called_once()
        self.assertEqual(90,json.loads((output/'startup-stack-probe.json').read_text())['admitted_seconds'])
        self.assertIn('watchdog',json.loads((output/'terminal.json').read_text())['stop'])


if __name__=='__main__':
    unittest.main()
