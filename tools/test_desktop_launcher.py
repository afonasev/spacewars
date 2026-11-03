"""Focused native-view packaging contracts, without Unity or publication."""
import importlib.util
import pathlib
import tempfile
import unittest
from unittest.mock import patch

path=pathlib.Path(__file__).parent/'native-release/package.py'
spec=importlib.util.spec_from_file_location('launcher_package',path)
package=importlib.util.module_from_spec(spec);spec.loader.exec_module(package)

class LauncherPackaging(unittest.TestCase):
    def test_windows_view_is_exact_utf8_script_without_extra_runtime(self):
        with tempfile.TemporaryDirectory() as temp,patch.object(package,'run') as run:
            root=pathlib.Path(temp);package.build_launcher_view(root,'windows-x64')
            source=package.ROOT/'tools/native-release/launcher/LauncherView.ps1'
            self.assertEqual((root/'LauncherView.ps1').read_bytes(),source.read_bytes())
            self.assertTrue(source.read_bytes().startswith(b'\xef\xbb\xbf'))
            run.assert_not_called()
    def test_mac_compiles_universal_view_and_seals_complete_bundle(self):
        with tempfile.TemporaryDirectory() as temp,patch.object(package,'run') as run:
            root=pathlib.Path(temp);package.build_launcher_view(root,'macos-universal')
            commands=[x.args[0] for x in run.call_args_list]
            compile=commands[0]
            self.assertEqual(compile.count('-arch'),2)
            self.assertIn('arm64',compile);self.assertIn('x86_64',compile)
            self.assertEqual(compile[-1],root/'LauncherView.app/Contents/MacOS/LauncherView')
            import plistlib
            info=plistlib.loads((root/'LauncherView.app/Contents/Info.plist').read_bytes())
            self.assertEqual(info['LSMinimumSystemVersion'],'11.0')
            self.assertEqual(commands[1],['codesign','--force','--sign','-',root/'LauncherView.app'])
