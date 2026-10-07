import importlib.util,pathlib,unittest,copy,tempfile,subprocess
from unittest.mock import patch
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

class PublicationEnvironmentTests(unittest.TestCase):
 def test_explicit_protocol_does_not_change_or_query_auth(self):
  with patch.object(publisher.subprocess,'run') as command:
   self.assertEqual(publisher.repository_url('ssh'),'git@github.com:afonasev/spacewars.git')
   self.assertEqual(publisher.repository_url('https'),'https://github.com/afonasev/spacewars.git')
   command.assert_not_called()
 def test_auto_honors_existing_gh_configuration(self):
  with patch.object(publisher.subprocess,'run',return_value=subprocess.CompletedProcess([],0,'ssh\n')) as command:
   self.assertEqual(publisher.repository_url(),'git@github.com:afonasev/spacewars.git')
   command.assert_called_once_with(['gh','config','get','git_protocol','--host','github.com'],capture_output=True,text=True,check=True)
  with patch.object(publisher.subprocess,'run',return_value=subprocess.CompletedProcess([],0,'unknown')):
   with self.assertRaises(ValueError):publisher.repository_url()
 def test_output_rejects_stale_files_without_deleting_evidence(self):
  with tempfile.TemporaryDirectory() as temp:
   output=pathlib.Path(temp)/'release';self.assertEqual(publisher.prepare_output(output),output.resolve())
   evidence=output/'publication.json';evidence.write_text('keep')
   with self.assertRaises(ValueError):publisher.prepare_output(output)
   self.assertEqual(evidence.read_text(),'keep')

class SourceExportTests(unittest.TestCase):
 def test_deleted_snapshot_files_are_removed_from_public_commit(self):
  with tempfile.TemporaryDirectory() as temp:
   base=pathlib.Path(temp);source=base/'source';clone=base/'public';source.mkdir();clone.mkdir()
   old='unity/Assets/OldOverlay.cs';new='unity/Assets/NewMenu.cs'
   (clone/old).parent.mkdir(parents=True);(clone/old).write_text('old overlay')
   (source/new).parent.mkdir(parents=True);(source/new).write_text('menu only')
   (clone/'source-snapshot.json').write_text(publisher.json.dumps({'files':{old:'previous'}}))
   run=publisher.run
   run(['git','init','-b','main'],cwd=clone)
   run(['git','config','user.name','Source Export Test'],cwd=clone)
   run(['git','config','user.email','source-export@example.invalid'],cwd=clone)
   run(['git','add','.'],cwd=clone);run(['git','commit','-m','previous public snapshot'],cwd=clone)
   previous=run(['git','rev-parse','HEAD'],cwd=clone)
   def local_run(args,**kwargs):
    return '' if args[:2]==['git','push'] else run(args,**kwargs)
   with patch.object(publisher,'ROOT',source),patch.object(publisher,'source_names',return_value=[new]),patch.object(publisher,'run',side_effect=local_run):
    commit,snapshot=publisher.export_source(clone,'source-identity')
   self.assertEqual(set(run(['git','ls-files'],cwd=clone).splitlines()),{new,'source-snapshot.json'})
   self.assertEqual(run(['git','status','--porcelain'],cwd=clone),'')
   self.assertEqual(set(publisher.json.loads(snapshot.read_text())['files']),{new})
   run(['git','merge-base','--is-ancestor',previous,commit],cwd=clone)
