"""Exercise channel propagation through the actual release-build CLI without Unity."""
import contextlib,importlib.util,io,json,pathlib,sys,tempfile,unittest
from unittest.mock import patch
spec=importlib.util.spec_from_file_location('logging_build',pathlib.Path(__file__).parent/'native-release/build.py');build=importlib.util.module_from_spec(spec);spec.loader.exec_module(build)
class TestBuildLoggingChannel(unittest.TestCase):
 def run_build(self,channel=None,extra=None):
  with tempfile.TemporaryDirectory() as temp:
   root=pathlib.Path(temp);player=root/'unity/Builds/release/windows/Player.exe';player.parent.mkdir(parents=True);player.write_bytes(b'Player fixture')
   dll=player.parent/build.WINDOWS_RECAST;dll.parent.mkdir(parents=True);dll.write_bytes((pathlib.Path(__file__).resolve().parents[1]/'unity/Assets/Spacewars/Plugins/x86_64/spacewars_recast.dll').read_bytes())
   calls=[]
   def run(command,**kwargs):
    calls.append(kwargs['env']);(root/'.local/bound-build-windows-x64.log').write_text('NATIVE_RELEASE_BUILD_PASS\n');return type('Exit',(),{'returncode':0})()
   argv=['build.py','--version','0.6.99','--platform','windows-x64','--release-date','2026-10-29']+(['--channel',channel] if channel else [])+(extra or [])
   def git(*args):return b'abc\n' if args==('rev-parse','HEAD') else b''
   with patch.object(build,'ROOT',root),patch.object(build,'git',side_effect=git),patch.object(build.subprocess,'run',side_effect=run),patch.object(sys,'argv',argv),contextlib.redirect_stdout(io.StringIO()):self.assertEqual(build.main(),0)
   return calls[0],json.loads((player.parent/'native-build.json').read_text())
 def test_default_build_is_test_and_carries_logging_requirement(self):
  env,record=self.run_build();self.assertEqual(env['SPACEWARS_RELEASE_CHANNEL'],'test');self.assertEqual(record['channel'],'test');self.assertTrue(record['test_match_logging'])
 def test_explicit_production_build_disables_test_recording(self):
  env,record=self.run_build('production');self.assertEqual(env['SPACEWARS_RELEASE_CHANNEL'],'production');self.assertFalse(record['test_match_logging'])

 def test_development_is_explicit_and_preserves_test_channel(self):
  env,record=self.run_build(extra=['--development']);self.assertEqual(env['SPACEWARS_RELEASE_DEVELOPMENT'],'1');self.assertTrue(record['development']);self.assertTrue(record['test_match_logging'])
 def test_optional_player_compression_is_lossless_lz4hc(self):
  env,record=self.run_build(extra=['--compression','lz4hc']);self.assertEqual(env['SPACEWARS_RELEASE_COMPRESSION'],'lz4hc');self.assertEqual(record['compression'],'lz4hc')
 def test_uncompressed_comparison_override_is_explicit(self):
  env,record=self.run_build(extra=['--compression','none']);self.assertEqual(env['SPACEWARS_RELEASE_COMPRESSION'],'none');self.assertEqual(record['compression'],'none')
 def test_development_production_combination_is_rejected(self):
  with self.assertRaisesRegex(ValueError,'test channel'):self.run_build('production',['--development'])

 def test_default_keeps_data_uncompressed_for_denser_installer(self):
  env,record=self.run_build();self.assertEqual(env['SPACEWARS_RELEASE_COMPRESSION'],'none');self.assertEqual(record['compression'],'none')
