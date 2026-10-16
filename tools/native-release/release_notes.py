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
