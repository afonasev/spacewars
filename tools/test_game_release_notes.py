import importlib.util,json,pathlib,subprocess,tempfile,unittest
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

class IllustratedNotesTests(unittest.TestCase):
 def setUp(self):
  import base64,json
  self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=pathlib.Path(self.temp.name)
  self.git('init');self.git('config','user.name','Fixture');self.git('config','user.email','fixture@example.invalid')
  import struct,zlib
  def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data))
  self.png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',1,1,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b'\0\xff\0\0'))+chunk(b'IEND',b'')
  (self.root/'screen.png').write_bytes(self.png);self.git('add','.');self.git('commit','-m','Screenshot source');self.source=self.git('rev-parse','HEAD')
  self.entry={'category_ru':'Интерфейс','category_en':'Interface','ru':'Панель настроек','en':'Settings panel','images':[{'path':'screen.png','ru':'Новая панель','en':'New panel','source_commit':self.source}]}
 def git(self,*args):return subprocess.check_output(['git',*args],cwd=self.root,text=True,stderr=subprocess.DEVNULL).strip()
 def commit_manifest(self,entry=None,**changes):
  data={'schema':2,'baseline':'v1.2.2','balance_reviewed':True,'entries':[entry or self.entry]}
  data.update(changes)
  (self.root/'notes.json').write_text(json.dumps(data,ensure_ascii=False));self.git('add','.');self.git('commit','-m','Author illustrated release');return self.git('rev-parse','HEAD')
 def test_bilingual_items_and_content_addressed_release_assets_use_committed_bytes(self):
  import hashlib
  head=self.commit_manifest();(self.root/'screen.png').write_bytes(b'foreign local edit')
  body,source=notes.authored(self.root,head,'notes.json','1.2.3',self.root/'assets','v1.2.2')
  name='notes-'+hashlib.sha256(self.png).hexdigest()+'.png'
  self.assertEqual((self.root/'assets'/name).read_bytes(),self.png)
  self.assertIn('### Интерфейс\n- Панель настроек',body);self.assertIn('### Interface\n- Settings panel',body)
  self.assertEqual(body.count('/releases/download/v1.2.3/'+name),2)
  self.assertEqual(source['images'][0]['source_commit'],self.source)
 def test_missing_uncommitted_traversal_and_bad_images_are_rejected_before_output(self):
  for path in ['../screen.png','/screen.png','not-in-git.png']:
   with self.subTest(path=path):
    self.entry['images'][0]['path']=path;head=self.commit_manifest()
    with self.assertRaises((ValueError,subprocess.CalledProcessError)):notes.authored(self.root,head,'notes.json','1.0.0',self.root/'assets','v1.2.2')
    self.assertFalse((self.root/'assets').exists())
  self.entry['images'][0]['path']='screen.png';(self.root/'screen.png').write_bytes(b'not an image');head=self.commit_manifest()
  with self.assertRaises(ValueError):notes.authored(self.root,head,'notes.json','1.0.0',self.root/'assets','v1.2.2')
 def test_external_image_urls_and_invalid_source_identity_are_not_accepted(self):
  self.entry['images'][0]['source_commit']='temporary'
  head=self.commit_manifest()
  with self.assertRaises(ValueError):notes.authored(self.root,head,'notes.json','1.0.0',self.root/'assets','v1.2.2')

 def test_large_patch_groups_matching_transitions_and_keeps_images_with_items(self):
  entries = [
   self.entry,
   {'category_ru': 'Юниты', 'category_en': 'Units', 'ru': 'Исследователь — время производства',
    'en': 'Explorer production time', 'highlight': True,
    'balance': {'before': 15, 'after': 12, 'unit': {'ru': 'с', 'en': 's'}}},
   {'category_ru': 'Юниты', 'category_en': 'Units', 'ru': 'Исследователь — дальность',
    'en': 'Explorer range',
    'balance': {'before': 10, 'after': 6.5, 'unit': {'ru': 'ед.', 'en': 'units'}}},
   {'category_ru': 'Экономика', 'category_en': 'Economy', 'ru': 'Условие выплаты',
    'en': 'Payment condition',
    'balance': {'before': {'ru': 'При захвате', 'en': 'On capture'},
                'after': {'ru': 'По таймеру', 'en': 'On timer'}}},
  ]
  head = self.commit_manifest(entries=entries)
  body, source = notes.authored(self.root, head, 'notes.json', '1.2.3', self.root/'assets', 'v1.2.2')
  ru, en = body.split('## English\n')
  self.assertEqual(source['baseline_release'], 'v1.2.2')
  for text, baseline, highlights, rest, categories, values in (
   (ru, 'Сравнение с', 'Главное для игроков', 'Всё остальное', ['Юниты', 'Интерфейс', 'Юниты', 'Экономика'],
    ['15 с → 12 с', '10 ед. → 6.5 ед.', 'При захвате → По таймеру']),
   (en, 'Compared with', 'Highlights for players', 'Other changes', ['Units', 'Interface', 'Units', 'Economy'],
    ['15 s → 12 s', '10 units → 6.5 units', 'On capture → On timer']),
  ):
   self.assertIn(f'{baseline} v1.2.2.', text)
   self.assertLess(text.index(f'### {highlights}'), text.index(f'### {rest}'))
   self.assertEqual([line[5:] for line in text.splitlines() if line.startswith('#### ')], categories)
   for value in values:
    self.assertEqual(text.count(value), 1)
  self.assertIn('- Панель настроек\n  ![Новая панель]', ru)

 def test_rejects_legacy_unreviewed_stale_or_incomplete_notes_before_writing_assets(self):
  invalid_manifests = [
   {'schema': 1}, {'balance_reviewed': False}, {'baseline': 'v1.2.1'},
   {'entries': [self.entry | {'balance': {'before': 5, 'after': 6}}]},
   {'entries': [self.entry | {'balance': {'before': 5, 'after': 5.0, 'unit': {'ru': 'с', 'en': 's'}}}]},
   {'entries': [self.entry | {'balance': {'before': True, 'after': 2, 'unit': {'ru': 'с', 'en': 's'}}}]},
  ]
  for changes in invalid_manifests:
   with self.subTest(changes=changes):
    head = self.commit_manifest(**changes)
    with self.assertRaises(ValueError):
     notes.authored(self.root, head, 'notes.json', '1.2.3', self.root/'assets', 'v1.2.2')
    self.assertFalse((self.root/'assets').exists())

 def test_first_release_and_added_or_removed_values(self):
  unit = {'ru': 'кредитов', 'en': 'credits'}
  entries = [
   self.entry | {'balance': {'before': None, 'after': 12, 'unit': unit}},
   self.entry | {'balance': {'before': 7, 'after': None, 'unit': unit}},
  ]
  head = self.commit_manifest(baseline=None, entries=entries)
  body, _ = notes.authored(self.root, head, 'notes.json', '1.2.3', self.root/'assets', None)
  self.assertIn('Первый релиз канала.', body)
  self.assertIn('нет → 12 кредитов', body)
  self.assertIn('7 credits → absent', body)
  self.assertNotIn('Всё остальное', body)
