"""Conservative bilingual release descriptions derived from changed player-facing areas.
The adjacent provenance JSON keeps commit subjects for review; internal IDs do not enter notes.
"""
import json,subprocess,re
AREAS=[
 ('Presentation/NativeSettings','Настройки экрана и интерфейса','Display and interface settings'),
 ('Presentation/NativeDevicePreferences','Настройки управления и отображения','Controls and display preferences'),
 ('native-release/','Установка и обновление игры','Game installation and updates'),
 ('Presentation/NativeMainMenu','Главное меню и история версий','Main menu and release history'),
 ('Presentation/NativeMenuNavigation','Навигация по меню','Menu navigation'),
 ('Presentation/NativeControlsHelp','Справка по игровому управлению','Game controls help'),
 ('Simulation/','Правила и поведение игры','Game rules and behavior'),
 ('Presentation/Playable','Управление и интерфейс матча','Match controls and interface'),
 ('AI/','Поведение компьютерных соперников','Computer opponent behavior'),
 ('Content/','Игровые материалы и оформление','Game content and presentation'),
 ('Presentation/','Игровой интерфейс и отображение','Game interface and presentation'),
]
def git(root,*args):return subprocess.check_output(['git',*args],cwd=root,text=True).strip()
def generate(root,head,baseline=None):
 if baseline:
  # A source revision from the previous channel release must belong to this history.
  subprocess.run(['git','merge-base','--is-ancestor',baseline,head],cwd=root,check=True,capture_output=True)
 span=f'{baseline}..{head}' if baseline else head
 commits=git(root,'log','--no-merges','--format=%H%x09%s',span).splitlines()
 paths=git(root,'diff','--name-only',baseline,head).splitlines() if baseline else git(root,'ls-tree','-r','--name-only',head).splitlines()
 labels=[]
 for path in paths:
  if '/Tests/' in path or '/test_' in path or path.endswith('.meta'):continue
  for pattern,ru,en in AREAS:
   if pattern in path:
    if (ru,en) not in labels:labels.append((ru,en))
    break
 if not labels:labels=[('Состав поставки игры','Game distribution contents')]
 changed=git(root,'diff','--unified=0',baseline,head,'--','unity/Assets/Spacewars/Presentation','tools/native-release/updater') if baseline else ''
 additions='\n'.join(line[1:] for line in changed.splitlines() if line.startswith('+') and not line.startswith('+++'))
 details=[]
 for marker,ru,en in [
  ('main-cancel-update','Загрузку обновления можно отменить из меню.','Update downloads can be cancelled from the menu.'),
  ('history-version','В меню доступна история выпущенных версий.','Release history is available from the menu.'),
  ('stopDownload(true)','Перед матчем загрузка обновления приостанавливается до возврата в меню.','Update downloads pause before a match and resume after returning to the menu.'),
  ('settings-confirm','Изменение режима экрана требует подтверждения; без него возвращается прежний режим.','Display changes require confirmation and otherwise return to the previous mode.'),
  ('settings-reset-all','Все пользовательские настройки можно сбросить одним действием.','All preferences can be reset with one action.'),
  ('settings-fps-limit','Добавлен выбор ограничения частоты кадров.','A frame rate limit can be selected.'),
  ('settings-contrast','Добавлены повышенный контраст и масштаб интерфейса.','High contrast and interface scaling are available.'),
 ]:
  if marker in additions:details.append((ru,en))
 russian=[ru for ru,en in details]+['Обновлены: '+ru[0].lower()+ru[1:]+'.' for ru,en in labels]
 english=[en for ru,en in details]+['Updated '+en[0].lower()+en[1:]+'.' for ru,en in labels]
 notes='## Русский\n\n### Улучшения\n'+'\n'.join('- '+line for line in russian)+'\n\n## English\n\n### Improved\n'+'\n'.join('- '+line for line in english)+'\n'
 return notes,{'baseline':baseline,'head':head,'commits':[{'sha':row.split('\t',1)[0],'subject':row.split('\t',1)[1]} for row in commits],'paths':paths,'summary_policy':'Conservative area summaries; no inferred gameplay outcomes.'}
def previous_release(releases,channel):
 selected=[r for r in releases if not r['draft'] and r['prerelease']==(channel=='test')]
 selected=[r for r in selected if re.fullmatch(r'v[0-9]+\.[0-9]+\.[0-9]+',r['tag_name'])]
 return max(selected,key=lambda r:tuple(map(int,r['tag_name'][1:].split('.'))))['tag_name'] if selected else None

def validate_png(content):
 import struct,zlib
 if len(content)>8*1024*1024 or content[:8]!=b'\x89PNG\r\n\x1a\n':raise ValueError('notes image must be a PNG up to 8 MiB')
 offset=8;compressed=bytearray();width=height=0;ended=False
 while offset+12<=len(content):
  size=struct.unpack('>I',content[offset:offset+4])[0];kind=content[offset+4:offset+8];end=offset+12+size
  if end>len(content):raise ValueError('truncated PNG')
  payload=content[offset+8:end-4]
  if zlib.crc32(kind+payload)!=struct.unpack('>I',content[end-4:end])[0]:raise ValueError('PNG checksum mismatch')
  if offset==8:
   if kind!=b'IHDR' or size!=13:raise ValueError('missing PNG header')
   width,height,depth,color,compression,filters,interlace=struct.unpack('>IIBBBBB',payload)
   if not 0<width<=4096 or not 0<height<=4096 or depth!=8 or color not in (0,2,4,6) or compression or filters or interlace:raise ValueError('use noninterlaced 8-bit PNG up to 4096 per side')
   stride=width*{0:1,2:3,4:2,6:4}[color]+1
  elif kind==b'IHDR':raise ValueError('duplicate PNG header')
  if kind==b'IDAT':compressed.extend(payload)
  if kind==b'IEND':
   if size or end!=len(content):raise ValueError('invalid PNG ending')
   ended=True;break
  offset=end
 if not ended or not compressed:raise ValueError('incomplete PNG')
 stream=zlib.decompressobj();raw=stream.decompress(compressed,height*stride+1)
 if len(raw)!=height*stride or not stream.eof or stream.unused_data or any(raw[row*stride]>4 for row in range(height)):raise ValueError('invalid PNG pixels')
 return width,height

# Authored notes are committed inputs. Images are release assets, never local URLs.
def authored(root, head, manifest, version, assets):
 import pathlib, hashlib, struct
 root=pathlib.Path(root)
 def blob(path):
  path=str(path)
  if not path or pathlib.PurePosixPath(path).is_absolute() or '..' in pathlib.PurePosixPath(path).parts or '\\' in path:
   raise ValueError('notes paths must be repository-relative')
  return subprocess.check_output(['git','show',f'{head}:{path}'],cwd=root,stderr=subprocess.PIPE)
 def words(value):
  if not isinstance(value,str) or not value.strip() or any(x in value for x in ('\n','\r','![','](', '<','>')):
   raise ValueError('notes text must be a nonempty plain line')
  return value.strip()
 if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+',version):raise ValueError('notes require release version')
 data=json.loads(blob(manifest));entries=data['entries']
 if data.get('schema')!=1 or not isinstance(entries,list) or not entries or len(entries)>100:raise ValueError('invalid notes manifest')
 sections={'ru':{},'en':{}};media={}
 for entry in entries:
  rendered={lang:['- '+words(entry[lang])] for lang in sections}
  for image in entry.get('images',[]):
   source=image['source_commit']
   if not re.fullmatch(r'[a-f0-9]{40}',source):raise ValueError('image needs exact source revision')
   subprocess.run(['git','merge-base','--is-ancestor',source,head],cwd=root,check=True,capture_output=True)
   content=blob(image['path'])
   validate_png(content)
   digest=hashlib.sha256(content).hexdigest();name='notes-'+digest+'.png'
   media[name]={'sha256':digest,'path':image['path'],'source_commit':source,'bytes':content}
   for lang in sections:
    caption=words(image[lang]).replace('[','').replace(']','')
    rendered[lang].append(f'\n  ![{caption}](https://github.com/afonasev/spacewars/releases/download/v{version}/{name})')
  for lang in sections:
   category=words(entry['category_'+lang]);sections[lang].setdefault(category,[]).append('\n'.join(rendered[lang]))
 # Validate all inputs before touching output. Asset names include their full content hash.
 assets=pathlib.Path(assets);assets.mkdir(parents=True,exist_ok=True)
 for name,item in media.items():(assets/name).write_bytes(item['bytes'])
 body='\n\n'.join('## '+title+'\n\n'+'\n\n'.join('### '+category+'\n'+'\n'.join(items) for category,items in sections[lang].items()) for lang,title in [('ru','Русский'),('en','English')])+'\n'
 return body,{'manifest':str(manifest),'head':head,'images':[{k:v for k,v in item.items() if k!='bytes'}|{'asset':name} for name,item in media.items()]}
