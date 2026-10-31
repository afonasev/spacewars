import hashlib,importlib.util,json,pathlib,tempfile,unittest
from unittest.mock import patch
SOURCE=pathlib.Path(__file__).parent/'native-release/package.py'
spec=importlib.util.spec_from_file_location('native_package',SOURCE);package=importlib.util.module_from_spec(spec);spec.loader.exec_module(package)
class NativeBuildIdentityTests(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.root=pathlib.Path(self.temp.name);self.player=self.root/'Player.exe';self.player.write_bytes(b'verified native Player')
  self.record={'schema':1,'commit':'abc','version':'0.6.0','platform':'windows-x64','dirty':False,'files':{'Player.exe':{'size':self.player.stat().st_size,'sha256':hashlib.sha256(self.player.read_bytes()).hexdigest()}}};self.write()
 def tearDown(self):self.temp.cleanup()
 def write(self):(self.root/'native-build.json').write_text(json.dumps(self.record))
 def validate(self,head='abc',version='0.6.0',channel=None):return package.validate_build_identity(self.root,'windows-x64',version,head,channel=channel)
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
 def test_old_reused_build_without_test_marker_is_rejected(self):
  with self.assertRaisesRegex(ValueError,'channel/logging'):self.validate(channel='test')
 def test_test_and_production_require_matching_embedded_logging_marker(self):
  marker=self.root/'Player_Data/StreamingAssets/TestBuildIdentity.json';marker.parent.mkdir(parents=True)
  for channel,enabled in [('test',True),('production',False)]:
   marker.write_text(json.dumps({'TestBuild':enabled}));self.record.update(channel=channel,test_match_logging=enabled)
   self.record['files'][str(marker.relative_to(self.root))]={'size':marker.stat().st_size,'sha256':hashlib.sha256(marker.read_bytes()).hexdigest()};self.write()
   self.assertEqual(self.validate(channel=channel)['channel'],channel)
   with self.assertRaisesRegex(ValueError,'channel/logging'):self.validate(channel='production' if enabled else 'test')
 def test_matching_inventory_cannot_hide_wrong_embedded_marker(self):
  marker=self.root/'Player_Data/StreamingAssets/TestBuildIdentity.json';marker.parent.mkdir(parents=True);marker.write_text('{"TestBuild":false}')
  self.record.update(channel='test',test_match_logging=True);self.record['files'][str(marker.relative_to(self.root))]={'size':marker.stat().st_size,'sha256':hashlib.sha256(marker.read_bytes()).hexdigest()};self.write()
  with self.assertRaisesRegex(ValueError,'embedded'):self.validate(channel='test')
class VolumeIconTests(unittest.TestCase):
 def test_icon_is_copied_and_custom_icon_flag_set_on_volume_root(self):
  with tempfile.TemporaryDirectory() as temp:
   root=pathlib.Path(temp);volume=root/'volume';volume.mkdir();icon=root/'source.icns';icon.write_bytes(b'icns fixture')
   with patch.object(package,'run') as command:
    package.prepare_volume_icon(volume,icon)
   self.assertEqual((volume/'.VolumeIcon.icns').read_bytes(),icon.read_bytes())
   command.assert_called_once_with(['SetFile','-a','C',volume])
if __name__=='__main__':unittest.main()
