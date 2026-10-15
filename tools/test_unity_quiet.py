"""Verify the real shell launcher forwards startup flags without starting Unity."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]

class UnityQuietRoutes(unittest.TestCase):
    def launch(self, arguments):
        with tempfile.TemporaryDirectory() as directory:
            runner = Path(directory) / "record-runner"
            runner.write_text("#!" + sys.executable + "\nimport json,sys\nprint(json.dumps(sys.argv[1:]))\n")
            runner.chmod(0o700)
            result = subprocess.run([str(ROOT / "tools/unity.sh"), *arguments],
                env=dict(os.environ, UNITY_RUNNER=str(runner), UNITY_EDITOR="/fake/Unity"),
                capture_output=True, text=True, check=True)
            return json.loads(result.stdout)

    def test_editor_batch_selects_both_quiet_contracts(self):
        args = self.launch(["shared", "-batchmode"])
        self.assertEqual(1, args.count("-noaudio"))
        self.assertEqual(1, args.count("-muted"))
        self.assertIn("-batchmode", args)

    def test_player_couples_either_flag_and_retains_exact_parameters(self):
        for switch in ["-muted", "-noaudio"]:
            with self.subTest(switch=switch):
                args = self.launch(["player", "/fake/Player", switch, "-matchSeed", "7"])
                self.assertEqual(1, args.count("-noaudio"))
                self.assertEqual(1, args.count("-muted"))
                self.assertIn("/fake/Player", args)
                self.assertEqual("7", args[args.index("-matchSeed")+1])

    def test_human_launch_without_quiet_selection_retains_arguments(self):
        args = self.launch(["player", "/fake/-muted-extra", "-matchSeed", "7"])
        self.assertNotIn("-noaudio", args)
        self.assertNotIn("-muted", args)

if __name__ == "__main__":
    unittest.main()
