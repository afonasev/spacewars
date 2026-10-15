import json
import signal
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import route_performance_qa as qa


class PlayerCleanupTests(unittest.TestCase):
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
            self.assertEqual(1, waits[-1], 'Player retains its entire original watchdog after queueing')
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
