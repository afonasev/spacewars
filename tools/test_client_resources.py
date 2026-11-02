import contextlib
import io
import json
import importlib.util
import pathlib
import sys
import tempfile
import unittest
from unittest.mock import patch

import deduplicate_client_resources as resources

spec = importlib.util.spec_from_file_location('client_build', pathlib.Path(__file__).parent/'native-release/build.py')
build = importlib.util.module_from_spec(spec)
spec.loader.exec_module(build)


class ClientResourcesTests(unittest.TestCase):
    def test_exact_payload_and_importer_required_and_guid_references_rebound(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            folder = root/'unity'/resources.STYLE
            folder.mkdir(parents=True)
            editor = root/'unity/Assets/Spacewars/Editor'
            editor.mkdir(parents=True)
            for name, guid, payload, settings in [('a', 'a'*32, b'exact', b'sRGB: 0'), ('b', 'b'*32, b'exact', b'sRGB: 0'),
                                                   ('c', 'c'*32, b'exact', b'sRGB: 1'), ('d', 'd'*32, b'changed', b'sRGB: 0')]:
                path = folder/(name+'-mask.png')
                path.write_bytes(payload)
                pathlib.Path(str(path)+'.meta').write_bytes(b'guid: '+guid.encode()+b'\n'+settings+b'\n')
            material = folder/'material.mat'
            material.write_bytes(b'%YAML\ntexture: {guid: '+b'b'*32+b', fileID: 2800000}\n')
            result = resources.consolidate(root, apply=True)
            self.assertEqual(result['removed_files'], 1)
            self.assertEqual(result['saved_source_bytes'], 5)
            self.assertIn(b'guid: '+b'a'*32, material.read_bytes())
            self.assertEqual(resources.verify(root)['verified_aliases'], 1)
            self.assertEqual(resources.consolidate(root)['removed_files'], 0)
            (folder/'a-mask.png').write_bytes(b'drift')
            with self.assertRaises(AssertionError):
                resources.verify(root)

    def test_shipping_cleanup_preserves_runtime_and_match_logs(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            debug = ['Player_BurstDebugInformation_DoNotShip/a.txt', 'Player_BackUpThisFolder_ButDontShipItWithYourGame/a.cpp',
                     'Player.dSYM/Contents/data', 'Player_Data/Managed/Code.pdb', '.DS_Store']
            runtime = ['Player.exe', 'UnityPlayer.dll', 'Player_Data/globalgamemanagers', 'Player_Data/Managed/Code.dll',
                       'Player_Data/StreamingAssets/TestBuildIdentity.json', 'TestLogs/session/errors.jsonl']
            for name in debug + runtime:
                path = root/name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b'preserve bytes')
            self.assertEqual(len(build.clean_debug_artifacts(root)), 5)
            self.assertTrue(all(not (root/p).exists() for p in debug))
            self.assertTrue(all((root/p).read_bytes() == b'preserve bytes' for p in runtime))
            self.assertEqual(build.clean_debug_artifacts(root), [])

    def test_real_build_route_binds_only_cleaned_payload(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            player = root/'unity/Builds/release/windows'
            player.mkdir(parents=True)
            def run(command, **kwargs):
                (player/'Player.exe').write_bytes(b'game')
                dll=player/build.WINDOWS_RECAST;dll.parent.mkdir(parents=True)
                dll.write_bytes((pathlib.Path(__file__).resolve().parents[1]/'unity/Assets/Spacewars/Plugins/x86_64/spacewars_recast.dll').read_bytes())
                (player/'Player.pdb').write_bytes(b'debug')
                (root/'.local/bound-build-windows-x64.log').write_text('NATIVE_RELEASE_BUILD_PASS')
                return type('Exit', (), {'returncode': 0})()
            def git(*args):
                return b'abc\n' if args == ('rev-parse', 'HEAD') else b''
            args = ['build.py', '--platform', 'windows-x64', '--version', '0.0.1']
            with patch.object(build, 'ROOT', root), patch.object(build, 'git', side_effect=git), patch.object(build.subprocess, 'run', side_effect=run), patch.object(sys, 'argv', args), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(build.main(), 0)
            record = json.loads((player/'native-build.json').read_text())
            self.assertEqual(set(record['files']), {'Player.exe', str(build.WINDOWS_RECAST)})
            self.assertEqual(record['removed_debug_artifacts'], ['Player.pdb'])
            self.assertEqual(record['channel'], 'test')
            self.assertTrue(record['test_match_logging'])

    def test_macos_bundle_is_resealed_before_inventory_binding(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            player = root/'unity/Builds/release/macOS/Player.app'
            (player/'Contents').mkdir(parents=True)
            def run(command, **kwargs):
                (player/'Contents/Player').write_bytes(b'game')
                (player/'Contents/Player.pdb').write_bytes(b'debug')
                (root/'.local/bound-build-macos-universal.log').write_text('NATIVE_RELEASE_BUILD_PASS')
                return type('Exit', (), {'returncode': 0})()
            signed = []
            def codesign(command):
                self.assertFalse((player/'Contents/Player.pdb').exists())
                signed.append(command)
                if '--force' in command:
                    (player/'Contents/CodeSignature').write_bytes(b'resealed')
                return 0
            def git(*args):
                return b'abc\n' if args == ('rev-parse', 'HEAD') else b''
            args = ['build.py', '--platform', 'macos-universal', '--version', '0.0.1']
            with patch.object(build, 'ROOT', root), patch.object(build, 'git', side_effect=git), patch.object(build.subprocess, 'run', side_effect=run), patch.object(build.subprocess, 'check_call', side_effect=codesign), patch.object(sys, 'argv', args), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(build.main(), 0)
            record = json.loads((player.parent/'native-build.json').read_text())
            self.assertIn('Contents/CodeSignature', record['files'])
            self.assertNotIn('Contents/Player.pdb', record['files'])
            self.assertEqual(len(signed), 2)
            self.assertIn('--preserve-metadata=identifier,entitlements,flags', signed[0])
            self.assertEqual(signed[1], ['codesign', '--verify', '--strict', str(player)])

    def test_committed_aliases_retain_exact_bytes_importers_and_valid_references(self):
        self.assertGreater(resources.verify(resources.ROOT)['verified_aliases'], 0)


if __name__ == '__main__':
    unittest.main()
