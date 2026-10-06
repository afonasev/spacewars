import importlib.util,pathlib,unittest,copy
spec=importlib.util.spec_from_file_location('github_publish',pathlib.Path(__file__).parent/'native-release/publish.py');publisher=importlib.util.module_from_spec(spec);spec.loader.exec_module(publisher)
class GithubPublicationTests(unittest.TestCase):
 def catalog(self):
  return {'channel':'test','artifacts':[{'channel':'test','platform':p,'version':'0.6.5','commit':'abc','dirty_build':False,'test_origin':None,'url':'https://github.com/afonasev/spacewars/releases/download/v0.6.5/Spacewars-0.6.5-'+suffix} for p,suffix in [('windows-x64','Windows-x64.exe'),('macos-universal','macOS-Universal.dmg')]]}
 def test_pair(self):publisher.validate_catalog(self.catalog(),'0.6.5','test','abc')
 def test_channel_source_missing_pair_and_nonversioned_urls_rejected(self):
  bad=[];x=self.catalog();x['channel']='production';bad.append(x);x=self.catalog();x['artifacts'].pop();bad.append(x)
  for key,value in [('dirty_build',True),('test_origin','http://127.0.0.1:1'),('commit','old'),('channel','production'),('url','https://github.com/afonasev/spacewars/releases/latest/download/installer.exe')]:
   x=self.catalog();x['artifacts'][0][key]=value;bad.append(x)
  for x in bad:
   with self.assertRaises(ValueError):publisher.validate_catalog(x,'0.6.5','test','abc')

class DraftLookupTests(unittest.TestCase):
 def test_draft_is_found_by_list_not_published_tag_endpoint(self):
  r={'tag_name':'v0.6.5','draft':True,'id':404988196};self.assertEqual(publisher.find_draft([r],'v0.6.5')['id'],404988196)
  with self.assertRaises(ValueError):publisher.find_draft([dict(r,draft=False)],'v0.6.5')
  with self.assertRaises(ValueError):publisher.find_draft([r,r],'v0.6.5')
