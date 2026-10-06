import hashlib,importlib.util,json,pathlib,tempfile,unittest
SOURCE=pathlib.Path(__file__).parent/'native-release/package.py'
spec=importlib.util.spec_from_file_location('native_package',SOURCE);package=importlib.util.module_from_spec(spec);spec.loader.exec_module(package)
class NativeBuildIdentityTests(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.root=pathlib.Path(self.temp.name);self.player=self.root/'Player.exe';self.player.write_bytes(b'verified native Player')
  self.record={'schema':1,'commit':'abc','version':'0.6.0','platform':'windows-x64','dirty':False,'files':{'Player.exe':{'size':self.player.stat().st_size,'sha256':hashlib.sha256(self.player.read_bytes()).hexdigest()}}};self.write()
 def tearDown(self):self.temp.cleanup()
 def write(self):(self.root/'native-build.json').write_text(json.dumps(self.record))
 def validate(self,head='abc',version='0.6.0'):return package.validate_build_identity(self.root,'windows-x64',version,head)
 def test_current_bound_player_is_accepted(self):self.assertEqual(self.validate()['commit'],'abc')
 def test_old_head_and_mislabelled_version_are_rejected(self):
  with self.assertRaises(ValueError):self.validate(head='def')
  with self.assertRaises(ValueError):self.validate(version='0.6.1')
 def test_changed_or_added_binary_is_rejected(self):
  self.player.write_bytes(b'other binary')
  with self.assertRaises(ValueError):self.validate()
  self.player.write_bytes(b'verified native Player');(self.root/'extra.exe').write_bytes(b'not built')
  with self.assertRaises(ValueError):self.validate()
 def test_dirty_build_cannot_be_published_as_release(self):
  self.record['dirty']=True;self.write()
  with self.assertRaises(ValueError):self.validate()
if __name__=='__main__':unittest.main()
