import json
import signal
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import route_performance_qa as qa


class PlayerCleanupTests(unittest.TestCase):
    def test_cli_seed_is_required_before_any_launch_or_output_creation(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'must-not-exist'
            with patch('sys.argv', ['route_performance_qa.py', '--output', str(output)]), \
                    patch.object(qa, 'run') as run:
                with self.assertRaises(SystemExit) as error:
                    qa.main()
                self.assertEqual(2, error.exception.code)
                run.assert_not_called()
                self.assertFalse(output.exists())

    def test_cli_passes_the_exact_explicit_seed(self):
        for seed in (7, 0, -1875503562):
            with self.subTest(seed=seed), patch('sys.argv', [
                    'route_performance_qa.py', '--output', '/capture', '--seed', str(seed)]), \
                    patch.object(qa, 'run', return_value=0) as run:
                self.assertEqual(0, qa.main())
                run.assert_called_once_with(Path('/capture'), seed, maximum_wall_seconds=120,
                    scenario='small', ai_route_audit=False, q10_readouts=False,
                    arrival_lifecycle_readouts=False, startup_stack_probe=False,
                    player_app=None, player_app_manifest=None,
                    player_app_manifest_sha256=None, player_cwd=None)

    def test_unadmitted_run_report_is_not_measured(self):
        from qa.split_screen_report import report
        with tempfile.TemporaryDirectory() as directory:
            output=Path(directory)
            terminal={'player_pid':None,'actual_player_alive':False,'stop':'Owned admission deadline'}
            (output/'terminal.json').write_text(json.dumps(terminal))
            result=report(output)
            self.assertEqual('NotRun',result['status'])
            self.assertEqual(terminal,result['terminal'])

    @staticmethod
    def complete_capture(output, motion=False):
        output.mkdir(exist_ok=True)
        (output/'qa-completion.txt').write_text('COMPLETE\nbounded observation\n')
        (output/'native-route-frames.csv').write_text('tick\n1\n# samples=1,dropped=0\n')
        if motion:
            (output/'eight-player-motion.csv').write_text('actor\n'+'\n'.join(map(str,range(400)))+'\n# samples=400,dropped=0\n')

    def test_split_screen_collection_rejects_reduced_resolution_and_missing_seats(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            self.complete_capture(output)
            (output/'precise-render-host.json').write_text('{"renderScale":1}')
            header = 'elapsed,phase,owner,units,screen_width,screen_height\n'
            data = ''.join(f'{phase*30},{phase},{owner},50,3456,2234\n'
                           for phase in range(4)
                           for owner in ('player-1','foundry-2','foundry-3','foundry-4'))
            path = output/'split-screen-seats.csv'
            path.write_text(header+data)
            self.assertTrue(qa.evidence_complete(output,'split-screen-200'))
            path.write_text(header+data.replace('3456','1280'))
            self.assertFalse(qa.evidence_complete(output,'split-screen-200'))
            path.write_text(header+data.replace('foundry-4','foundry-3'))
            self.assertFalse(qa.evidence_complete(output,'split-screen-200'))
            path.write_text(header+data)
            (output/'precise-render-host.json').write_text('{"renderScale":0.5}')
            self.assertFalse(qa.evidence_complete(output,'split-screen-200'))

    @staticmethod
    def complete_q10(output):
        (output/'eight-player-motion.csv').write_text('actor,expected_sequence,pending_sequence,active_sequence,current_sequence,meta_tick,meta_generation,actual_admission_requests,completion_sequence,completion_spatial\n'+''.join(f'{i},1,1,0,0,1,1,0,0,0\n' for i in range(400))+'# samples=400,dropped=0\n')
        (output/'eight-player-group-final.csv').write_text('actor,expected_sequence,pending_sequence,active_sequence,current_sequence,completion_sequence,completion_spatial,actual_admission_requests\n'+''.join(f'{i},1,1,0,0,0,0,0\n' for i in range(400))+'# rows=400,dropped=0\n')
        (output/'eight-player-receipts.csv').write_text('owner,generation,sequence,phase,status\n'+''.join(f'owner-{i},1,1,submission,Accepted\n' for i in range(8))+'# rows=8,dropped=0,expected=8\n')
        (output/'ai-route-review-audit.csv').write_text('owner,untracked_calls,foreign_profile_calls,repeated_safe,repeated_withdrawal,max_pairs,max_bound,foreign_observation_calls,observer_wall_ms\n'+''.join(f'owner-{i},0,0,0,0,1,4096,0,1\n' for i in range(8))+'# owners=8,dropped=0\n')

    def test_q10_completion_requires_bound_receipts_and_audit_but_never_fakes_applied(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            self.complete_capture(output, motion=True); self.complete_q10(output)
            self.assertTrue(qa.evidence_complete(output, 'eight-player-400-moving', q10_readouts=True, ai_route_audit=True), 'No terminal receipt remains unknown; it is not fabricated Applied/rejected')
            receipt = output/'eight-player-receipts.csv'
            good = receipt.read_text(); receipt.write_text(good.replace('owner-7', 'owner-6'))
            self.assertFalse(qa.evidence_complete(output, 'eight-player-400-moving', q10_readouts=True))
            receipt.write_text(good)
            for status in ('Rejected', 'Cancelled'):
                receipt.write_text(good.replace('# rows=8', ''.join(f'owner-{i},1,1,receipt,{status}\n' for i in range(8))+'# rows=16'))
                self.assertTrue(qa.evidence_complete(output, 'eight-player-400-moving', q10_readouts=True, ai_route_audit=True), 'Refused game commands do not erase complete diagnostic data or become Applied')
                summary = json.loads((output/'q10-collection-summary.json').read_text())
                self.assertEqual({status: 8}, summary['actual_receipt_status_counts'])
                self.assertFalse(summary['gameplay_qualified'])
            receipt.write_text(good)
            receipt.unlink()
            self.assertFalse(qa.evidence_complete(output, 'eight-player-400-moving', q10_readouts=True))
            self.assertFalse(json.loads((output/'q10-collection-summary.json').read_text())['collection_complete'])
            self.assertTrue(qa.evidence_complete(output, 'eight-player-400-moving'), 'Legacy completion does not require Q10 files')
            receipt.write_text(good)
            audit = output/'ai-route-review-audit.csv'; audit.write_text(audit.read_text().replace('dropped=0', 'dropped=1'))
            self.assertFalse(qa.evidence_complete(output, 'eight-player-400-moving', q10_readouts=True, ai_route_audit=True))

    def test_q10_real_submission_rejection_is_complete_data_but_not_gameplay_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            self.complete_capture(output, motion=True); self.complete_q10(output)
            receipt = output/'eight-player-receipts.csv'
            good = receipt.read_text()
            receipt.write_text(good.replace('submission,Accepted', 'submission,Rejected', 1))
            self.assertTrue(qa.evidence_complete(output, 'eight-player-400-moving', q10_readouts=True, ai_route_audit=True))
            summary = json.loads((output/'q10-collection-summary.json').read_text())
            self.assertEqual({'Rejected': 1, 'Accepted': 7}, summary['submission_status_counts'])
            self.assertEqual({}, summary['actual_receipt_status_counts'])
            self.assertFalse(summary['gameplay_qualified']); self.assertIn('refusal', summary['gameplay_reason'])
            receipt.write_text(good.replace('owner-7,1,1,submission,Accepted\n', '').replace('# rows=8', '# rows=7'))
            self.assertFalse(qa.evidence_complete(output, 'eight-player-400-moving', q10_readouts=True, ai_route_audit=True))

    def test_q10_flags_are_explicit_and_never_invoke_a_build(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            executable = root / 'unity/Builds/macOS/SpacewarsPlayable.app/Contents/MacOS/Spacewars'
            executable.parent.mkdir(parents=True); executable.touch()
            for enabled in (False, True):
                output = root / ('q10' if enabled else 'legacy')
                process = Mock(); process.poll.return_value = 0; process.wait.return_value = 0
                def launch(command, **kwargs):
                    self.complete_capture(output, motion=True)
                    if enabled:
                        self.complete_q10(output)
                    self.assertEqual(root,kwargs['cwd'],'Legacy launch retains ROOT cwd')
                    self.assertEqual(enabled, '-q10Readouts' in command)
                    self.assertEqual(enabled, '-aiRouteAuditEvidence' in command)
                    self.assertEqual(enabled, '-arrivalLifecycleReadouts' in command)
                    if enabled:
                        from test_arrival_lifecycle import ArrivalLifecycleTests
                        ArrivalLifecycleTests().fixture(output/'arrival-lifecycle.csv')
                    self.assertIn('-noaudio', command); self.assertIn('-muted', command)
                    self.assertNotIn('-executeMethod', command); self.assertNotIn('-runTests', command)
                    self.assertNotIn('build', command)
                    kwargs['stdout'].write('unity-run: started exclusive; guardian=10 pid=42\n'); kwargs['stdout'].flush()
                    return process
                with patch.object(qa, 'ROOT', root), patch.object(qa.subprocess, 'Popen', side_effect=launch), \
                        patch.object(qa, 'owned_player', return_value=False), \
                        patch.object(qa.subprocess, 'run', return_value=Mock(stdout='')):
                    self.assertEqual(0, qa.run(output, 7, scenario='eight-player-400-moving', q10_readouts=enabled, ai_route_audit=enabled, arrival_lifecycle_readouts=enabled))
            with self.assertRaisesRegex(ValueError, '--q10-readouts'):
                qa.run(root / 'invalid', 7, ai_route_audit=True)

    def test_watchdog_deadline_is_not_a_success_even_when_runner_has_exited(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            executable = root / 'unity/Builds/macOS/SpacewarsPlayable.app/Contents/MacOS/Spacewars'
            executable.parent.mkdir(parents=True); executable.touch()
            process = Mock()
            process.wait.side_effect = subprocess.TimeoutExpired('owned-runner', 1)
            process.poll.return_value = 0
            def launch(*args, **kwargs):
                self.assertIn('-noaudio', args[0])
                self.assertIn('-muted', args[0])
                self.assertNotIn('-batchmode', args[0], 'Visible frame timing requires scene rendering')
                kwargs['stdout'].write('unity-run: started exclusive; guardian=10 pid=42\n')
                kwargs['stdout'].flush()
                return process
            with patch.object(qa, 'ROOT', root), patch.object(qa.subprocess, 'Popen', side_effect=launch), \
                    patch.object(qa, 'owned_player', return_value=False), \
                    patch.object(qa.subprocess, 'run', return_value=Mock(stdout='')):
                self.assertEqual(1, qa.run(root / 'capture', 7, maximum_wall_seconds=1))

    def test_admission_does_not_consume_unchanged_player_watchdog(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            executable = root / 'unity/Builds/macOS/SpacewarsPlayable.app/Contents/MacOS/Spacewars'
            executable.parent.mkdir(parents=True); executable.touch()
            process = Mock(); process.poll.return_value = None
            clock = [0.0]; waits = []
            def launch(*args, **kwargs):
                self.complete_capture(root/'capture')
                log = kwargs['stdout']
                log.write('unity-run: waiting exclusive\n'); log.flush()
                def wait(timeout):
                    waits.append(timeout)
                    if len(waits) == 1:
                        clock[0] += .6
                        log.write('unity-run: started exclusive; guardian=10 pid=42\n'); log.flush()
                        raise subprocess.TimeoutExpired('waiting-runner', timeout)
                    process.poll.return_value = 0
                    return 0
                process.wait.side_effect = wait
                return process
            with patch.object(qa, 'ROOT', root), patch.object(qa.subprocess, 'Popen', side_effect=launch), \
                    patch.object(qa.time, 'monotonic', side_effect=lambda: clock[0]), \
                    patch.object(qa, 'owned_player', return_value=False), \
                    patch.object(qa.subprocess, 'run', return_value=Mock(stdout='')):
                self.assertEqual(0, qa.run(root / 'capture', 7, maximum_wall_seconds=1, maximum_admission_seconds=2))
                deadlines=list(map(int,(root/'capture/qa-deadline.txt').read_text().splitlines()))
            self.assertEqual(1, waits[-1], 'Player retains its entire original watchdog after queueing')
            finish,hard=deadlines
            self.assertEqual(100,hard-finish,'Graceful reserve is inside the unchanged1s test cap')
            self.assertEqual(.6, json.loads((root/'capture/terminal.json').read_text())['admission_seconds'])

    def test_admission_deadline_cancels_wait_without_a_player_phase(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            executable = root / 'unity/Builds/macOS/SpacewarsPlayable.app/Contents/MacOS/Spacewars'
            executable.parent.mkdir(parents=True); executable.touch()
            process = Mock(); process.poll.return_value = None; process.wait.return_value = 130
            with patch.object(qa, 'ROOT', root), patch.object(qa.subprocess, 'Popen', return_value=process), \
                    patch.object(qa.time, 'monotonic', side_effect=[0, 2, 2]), \
                    patch.object(qa.subprocess, 'run', return_value=Mock(stdout='')):
                self.assertEqual(1, qa.run(root/'capture', 7, maximum_wall_seconds=1, maximum_admission_seconds=1))
            process.send_signal.assert_called_once_with(signal.SIGINT)
            process.wait.assert_called_once_with(timeout=10)
            terminal = json.loads((root/'capture/terminal.json').read_text())
            self.assertEqual('Owned admission deadline', terminal['stop'])
            self.assertIsNone(terminal['player_phase_seconds'])

    def test_scripted_moving_load_retains_seed_silent_and_eight_player_routes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            executable = root / 'unity/Builds/macOS/SpacewarsPlayable.app/Contents/MacOS/Spacewars'
            executable.parent.mkdir(parents=True); executable.touch()
            process = Mock(); process.poll.return_value = 0; process.wait.return_value = 0
            def launch(command, **kwargs):
                self.complete_capture(root/'capture',motion=True)
                for argument in ['-noaudio', '-muted', '-eightPlayerPerformanceEvidence', '-eightPlayerMovingPerformanceEvidence']:
                    self.assertIn(argument, command)
                self.assertEqual('7', command[command.index('-matchSeed')+1])
                kwargs['stdout'].write('unity-run: started exclusive; guardian=10 pid=42\n'); kwargs['stdout'].flush()
                return process
            with patch.object(qa, 'ROOT', root), patch.object(qa.subprocess, 'Popen', side_effect=launch), \
                    patch.object(qa, 'owned_player', return_value=False), \
                    patch.object(qa.subprocess, 'run', return_value=Mock(stdout='')):
                self.assertEqual(0, qa.run(root/'capture', 7, scenario='eight-player-400-moving'))

    def test_completion_rejects_truncation_drops_and_incomplete400(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.complete_capture(root,motion=True)
            self.assertTrue(qa.evidence_complete(root,'eight-player-400-moving'))
            for text in ['tick\n1\n','tick\n1\n# samples=1,dropped=2\n','tick\n1\n# samples=2,dropped=0\n']:
                (root/'native-route-frames.csv').write_text(text)
                self.assertFalse(qa.evidence_complete(root,'small-five'))
            self.complete_capture(root,motion=False)
            (root/'eight-player-motion.csv').write_text('actor\n1\n# samples=1,dropped=0\n')
            self.assertFalse(qa.evidence_complete(root,'eight-player-400-moving'))
            (root/'qa-completion.txt').write_text('FAIL\nentry failure\n')
            self.assertFalse(qa.evidence_complete(root,'small-five'))

    def test_hard_cap_cannot_exceed120(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(qa.subprocess,'Popen') as launch:
                for limit in [0,-1,120.001]:
                    with self.assertRaises(ValueError):qa.run(Path(directory)/'unused',7,maximum_wall_seconds=limit)
                launch.assert_not_called()

    def test_eight_player_startup_headroom_keeps_silent_seed_and_complete_capture(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);executable=root/'unity/Builds/macOS/SpacewarsPlayable.app/Contents/MacOS/Spacewars'
            executable.parent.mkdir(parents=True);executable.touch()
            process=Mock();process.poll.return_value=0;process.wait.return_value=0
            def launch(command,**kwargs):
                self.complete_capture(root/'capture',motion=True)
                for argument in ['-noaudio','-muted','-eightPlayerPerformanceEvidence','-eightPlayerMovingPerformanceEvidence']:
                    self.assertIn(argument,command)
                self.assertEqual('7',command[command.index('-matchSeed')+1])
                kwargs['stdout'].write('unity-run: started exclusive; guardian=10 pid=42\n');kwargs['stdout'].flush()
                return process
            with patch.object(qa,'ROOT',root),patch.object(qa.subprocess,'Popen',side_effect=launch), \
                    patch.object(qa,'owned_player',return_value=False),patch.object(qa.subprocess,'run',return_value=Mock(stdout='')):
                self.assertEqual(0,qa.run(root/'capture',7,maximum_wall_seconds=150,scenario='eight-player-400-moving'))
            deadlines=(root/'capture/qa-deadline.txt').read_text().splitlines()
            self.assertEqual(10000,int(deadlines[1])-int(deadlines[0]),'Teardown remains inside the admitted hard limit')

    def test_eight_player_headroom_is_bounded_before_launch(self):
        with tempfile.TemporaryDirectory() as directory,patch.object(qa.subprocess,'Popen') as launch:
            for scenario in ['eight-player-400','eight-player-400-moving']:
                for limit in [0,-1,180.001]:
                    with self.assertRaises(ValueError):qa.run(Path(directory)/'unused',7,maximum_wall_seconds=limit,scenario=scenario)
            launch.assert_not_called()

    def test_same_pid_foreign_command_is_never_signaled(self):
        with patch.object(qa, 'player_command', return_value='/foreign/Player -other'), \
                patch.object(qa.os, 'kill') as kill:
            self.assertFalse(qa.close_owned_player(42, Path('/owned/Player'), Path('/capture')))
            kill.assert_not_called()

    def test_actual_child_exit_is_checked_after_termination(self):
        with patch.object(qa, 'owned_player', side_effect=[True, False, False, False]), \
                patch.object(qa.os, 'kill') as kill:
            self.assertTrue(qa.close_owned_player(42, Path('/owned/Player'), Path('/capture')))
            kill.assert_called_once_with(42, signal.SIGTERM)

    def test_exact_executable_and_run_directory_are_required(self):
        executable = Path('/owned/Player')
        output = Path('/capture')
        for command, expected in [
            ('/owned/Player -playableEvidence /capture -routePerformanceEvidence', True),
            ('/owned/Player -playableEvidence /capture-other -routePerformanceEvidence', False),
            ('/foreign/Player -playableEvidence /capture -routePerformanceEvidence', False),
            ('python unity-run /owned/Player -playableEvidence /capture -routePerformanceEvidence', False),
        ]:
            with self.subTest(command=command), patch.object(qa, 'player_command', return_value=command):
                self.assertEqual(expected, qa.owned_player(42, executable, output))


if __name__ == '__main__':
    unittest.main()
