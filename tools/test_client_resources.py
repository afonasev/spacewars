import contextlib
import io
import json
import importlib.util
import pathlib
import sys
import tempfile
import unittest
from unittest.mock import patch

from tools.test_release_pipeline import write_release_profile_fixture

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
                write_release_profile_fixture(root, kwargs['env']['SPACEWARS_RELEASE_VERSION'])
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

    def run_macos_build(self, failure=None):
        # Exercise the actual build CLI and validator; only platform commands are fake.
        import hashlib
        import subprocess
        from native_plugins import RECAST_EXPORTS
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            player = root/'unity/Builds/release/macOS/Player.app'
            (player/'Contents').mkdir(parents=True)
            source = root/'unity/Assets/Spacewars/Plugins/macOS'/build.MACOS_RECAST_NAME
            source.parent.mkdir(parents=True)
            if failure != 'missing':
                source.write_bytes(b'fixture plugin')
            plugin = player/'Contents/PlugIns'/build.MACOS_RECAST_NAME
            events = []
            def run(command, **kwargs):
                write_release_profile_fixture(root, kwargs['env']['SPACEWARS_RELEASE_VERSION'])
                (player/'Contents/Player').write_bytes(b'game')
                (player/'Contents/Player.pdb').write_bytes(b'debug')
                (root/'.local/bound-build-macos-universal.log').write_text('NATIVE_RELEASE_BUILD_PASS')
                return type('Exit', (), {'returncode': 0})()
            def inspect(command, **kwargs):
                self.assertEqual(command[-1], str(plugin))
                self.assertEqual(plugin.read_bytes(), source.read_bytes())
                events.append(command[0])
                if command[0] == 'lipo':
                    self.assertEqual(command[1], '-archs')
                    return 'arm64' if failure == 'architecture' else 'arm64 x86_64'
                self.assertEqual(command[:2], ['nm', '-gU'])
                exports = RECAST_EXPORTS - ({'sw_recast_build_layered'} if failure == 'exports' else set())
                return '\n'.join('0000 T _' + name for name in sorted(exports))
            def codesign(command):
                self.assertFalse((player/'Contents/Player.pdb').exists())
                events.append(command)
                if failure == 'plugin-sign' and command[-1] == str(plugin):
                    raise subprocess.CalledProcessError(1, command)
                if failure == 'bundle-sign' and command[-1] == str(player):
                    raise subprocess.CalledProcessError(1, command)
                if '--force' in command:
                    if command[-1] == str(plugin):
                        plugin.write_bytes(b'signed fixture plugin')
                    else:
                        (player/'Contents/CodeSignature').write_bytes(b'resealed')
                return 0
            def git(*args):
                return b'abc\n' if args == ('rev-parse', 'HEAD') else b''
            args = ['build.py', '--platform', 'macos-universal', '--version', '0.0.1']
            identity = player.parent/'native-build.json'
            with patch.object(build, 'ROOT', root), patch.object(build, 'git', side_effect=git), patch.object(build.subprocess, 'run', side_effect=run), patch.object(build.subprocess, 'check_output', side_effect=inspect), patch.object(build.subprocess, 'check_call', side_effect=codesign), patch.object(sys, 'argv', args), contextlib.redirect_stdout(io.StringIO()):
                try:
                    self.assertEqual(build.main(), 0)
                except (ValueError, FileNotFoundError, subprocess.CalledProcessError):
                    self.assertFalse(identity.exists(), 'Failed staging/signing must never bind a build')
                    raise
            record = json.loads(identity.read_text())
            self.assertEqual(events, [
                'lipo', 'nm',
                ['codesign', '--force', '--sign', '-', str(plugin)],
                ['codesign', '--verify', '--strict', str(plugin)],
                ['codesign', '--force', '--sign', '-', '--preserve-metadata=identifier,entitlements,flags', str(player)],
                ['codesign', '--verify', '--strict', str(player)],
            ])
            self.assertIn('Contents/CodeSignature', record['files'])
            self.assertNotIn('Contents/Player.pdb', record['files'])
            self.assertEqual(record['files']['Contents/PlugIns/'+build.MACOS_RECAST_NAME], {
                'size': plugin.stat().st_size, 'sha256': hashlib.sha256(plugin.read_bytes()).hexdigest()})
            self.assertEqual(source.read_bytes(), b'fixture plugin')

    def test_macos_bundle_is_resealed_before_inventory_binding(self):
        self.run_macos_build()

    def test_macos_invalid_plugin_never_binds_inventory(self):
        for failure, message in [('architecture', 'arm64 and x86_64'), ('exports', 'ABI exports missing')]:
            with self.subTest(failure=failure), self.assertRaisesRegex(ValueError, message):
                self.run_macos_build(failure)
        with self.assertRaises(FileNotFoundError):
            self.run_macos_build('missing')

    def test_macos_signing_failure_never_binds_inventory(self):
        import subprocess
        for failure in ['plugin-sign', 'bundle-sign']:
            with self.subTest(failure=failure), self.assertRaises(subprocess.CalledProcessError):
                self.run_macos_build(failure)

    def test_committed_aliases_retain_exact_bytes_importers_and_valid_references(self):
        self.assertGreater(resources.verify(resources.ROOT)['verified_aliases'], 0)


if __name__ == '__main__':
    unittest.main()
