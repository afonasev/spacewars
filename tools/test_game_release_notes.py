import importlib.util,pathlib,subprocess,tempfile,unittest
path=pathlib.Path(__file__).parent/'native-release/release_notes.py'
spec=importlib.util.spec_from_file_location('game_release_notes',path);notes=importlib.util.module_from_spec(spec);spec.loader.exec_module(notes)
class ReleaseNotesTests(unittest.TestCase):
 def test_previous_channel_uses_semantic_versions_not_api_order(self):
  rows=[{'tag_name':tag,'draft':False,'prerelease':pre} for tag,pre in [('v1.2.0',False),('v1.10.0',False),('v2.0.0',True)]]
  self.assertEqual('v1.10.0',notes.previous_release(rows,'production'));self.assertEqual('v2.0.0',notes.previous_release(rows,'test'));self.assertIsNone(notes.previous_release([],'test'))
 def test_notes_use_exact_git_range_and_matching_languages(self):
  with tempfile.TemporaryDirectory() as temp:
   root=pathlib.Path(temp)
   def git(*args):return subprocess.check_output(['git',*args],cwd=root,text=True,stderr=subprocess.DEVNULL).strip()
   git('init');git('config','user.name','Fixture');git('config','user.email','fixture@example.invalid')
   (root/'before').write_text('old');git('add','.');git('commit','-m','Old change');base=git('rev-parse','HEAD')
   p=root/'unity/Assets/Spacewars/Presentation/NativeSettingsPage.cs';p.parent.mkdir(parents=True);p.write_text('fixture')
   git('add','.');git('commit','-m','internal-task-Q100 settings implementation');head=git('rev-parse','HEAD')
   body,source=notes.generate(root,head,base)
   self.assertIn('## Русский',body);self.assertIn('## English',body);self.assertIn('настройки',body);self.assertIn('settings',body);self.assertNotIn('Q100',body)
   self.assertEqual([head],[c['sha'] for c in source['commits']]);self.assertEqual(base,source['baseline']);self.assertNotIn('Old change',str(source))
   first,_=notes.generate(root,head);self.assertIn('## Русский',first)
   with self.assertRaises(subprocess.CalledProcessError):notes.generate(root,head,'unknown-source')
