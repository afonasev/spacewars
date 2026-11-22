"""Conservative bilingual release descriptions derived from changed player-facing areas.
The adjacent provenance JSON keeps commit subjects for review; internal IDs do not enter notes.
"""
import json,subprocess,re
from decimal import Decimal
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

import re as _download_re
from urllib.parse import quote as _download_quote
_DOWNLOAD_START = '<!-- desktop-downloads:start -->'
_DOWNLOAD_END = '<!-- desktop-downloads:end -->'

def with_installer_downloads(body, version, repository, windows_name, mac_name):
    if not _download_re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?', version):
        raise ValueError('Native version required')
    if not _download_re.fullmatch(r'afonasev/[a-z0-9-]+', repository):
        raise ValueError('Owned release repository required')
    if not windows_name.endswith('.exe') or not mac_name.endswith(('.dmg', '.pkg')):
        raise ValueError('Windows and macOS installers required')
    for name in (windows_name, mac_name):
        if '/' in name or not _download_re.fullmatch(r'[A-Za-z0-9_.-]+', name):
            raise ValueError('Installer basename required')
    base = f'https://github.com/{repository}/releases/download/v{_download_quote(version)}/'
    view = f'https://github.com/{repository}/releases?q=' + _download_quote('tag:v' + version.split('.')[0] + ' prerelease:false', safe='')
    block = f'{_DOWNLOAD_START}\n**Скачать / Download**\n\n| Windows | macOS |\n| --- | --- |\n| **[Download EXE]({base}{windows_name})** | **[Download {mac_name.rsplit(".",1)[1].upper()}]({base}{mac_name})** |\n\n[Только установщики / Installer releases]({view})\n\nInstaller version: **{version}**.\n{_DOWNLOAD_END}\n\n'
    clean = _download_re.sub(_download_re.escape(_DOWNLOAD_START) + r'[\s\S]*?' + _download_re.escape(_DOWNLOAD_END) + r'\s*', '', body)
    # Preserve canonical bilingual bodies required by existing publishers/history readers.
    if clean.startswith('## Русский\n'):
        prefix = '## Русский\n\n'
        return prefix + block + clean[len('## Русский\n'):].lstrip('\n')
    return block + clean.lstrip('\n')

def git(root,*args):return subprocess.check_output(['git',*args],cwd=root,text=True).strip()
def source_range(root,head,baseline=None):
 if baseline:
  # A source revision from the previous channel release must belong to this history.
  subprocess.run(['git','merge-base','--is-ancestor',baseline,head],cwd=root,check=True,capture_output=True)
 span=f'{baseline}..{head}' if baseline else head
 commits=git(root,'log','--no-merges','--format=%H%x09%s',span).splitlines()
 paths=git(root,'diff','--name-only',baseline,head).splitlines() if baseline else git(root,'ls-tree','-r','--name-only',head).splitlines()
 return {
  'baseline':baseline,
  'head':head,
  'commits':[dict(zip(('sha','subject'),row.split('\t',1))) for row in commits],
  'paths':paths,
 }

def generate(root,head,baseline=None):
 provenance=source_range(root,head,baseline)
 paths=provenance['paths']
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
 provenance['summary_policy']='Conservative area summaries; no inferred gameplay outcomes.'
 return notes,provenance
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

LANGUAGES = {'ru': 'Русский', 'en': 'English'}
HIGHLIGHTS = {'ru': 'Главное для игроков', 'en': 'Highlights for players'}
OTHER_CHANGES = {'ru': 'Всё остальное', 'en': 'Other changes'}
ABSENT = {'ru': 'нет', 'en': 'absent'}


def words(value):
    if not isinstance(value, str) or not value.strip() or any(
        marker in value for marker in ('\n', '\r', '![', '](', '<', '>')
    ):
        raise ValueError('notes text must be a nonempty plain line')
    return value.strip()


def localized(value):
    if not isinstance(value, dict) or value.keys() != LANGUAGES.keys():
        raise ValueError('notes require both ru and en text')
    return {lang: words(value[lang]) for lang in LANGUAGES}


def balance_transition(change):
    if not isinstance(change, dict) or not {'before', 'after'} <= change.keys():
        raise ValueError('balance changes require before and after')
    before, after = change['before'], change['after']
    if isinstance(before, dict) and isinstance(after, dict):
        previous, current = localized(before), localized(after)
        if any(previous[lang] == current[lang] for lang in LANGUAGES):
            raise ValueError('describe the changed rule in both languages')
    else:
        for value in (before, after):
            if value is None or type(value) is int or isinstance(value, Decimal) and value.is_finite():
                continue
            raise ValueError('balance values must be finite numbers or bilingual rule states')
        if before == after:
            raise ValueError('balance before and after must differ')
        unit = localized(change.get('unit'))
        previous = {lang: ABSENT[lang] if before is None else f'{before} {unit[lang]}' for lang in LANGUAGES}
        current = {lang: ABSENT[lang] if after is None else f'{after} {unit[lang]}' for lang in LANGUAGES}
    return {lang: f'{previous[lang]} → {current[lang]}' for lang in LANGUAGES}


def render_notes(sections, comparison):
    split = bool(sections[True] and sections[False])
    baseline = (
        f'Сравнение с {comparison}.' if comparison else 'Первый релиз канала.',
        f'Compared with {comparison}.' if comparison else 'First release in this channel.',
    )
    bodies = []
    for index, (lang, title) in enumerate(LANGUAGES.items()):
        parts = [f'## {title}', baseline[index]]
        for highlighted, heading in ((True, HIGHLIGHTS), (False, OTHER_CHANGES)):
            categories = sections[highlighted]
            if not categories:
                continue
            if split:
                parts.append(f'### {heading[lang]}')
            level = '####' if split else '###'
            for names, items in categories.items():
                parts.append(f'{level} {names[index]}\n' + '\n'.join(item[lang] for item in items))
        bodies.append('\n\n'.join(parts))
    return '\n\n'.join(bodies) + '\n'


# Authored notes are committed inputs. Images are release assets, never local URLs.
def authored(root, head, manifest, version, assets, comparison):
    import hashlib
    import pathlib

    def blob(path):
        path = str(path)
        if not path or pathlib.PurePosixPath(path).is_absolute() or '..' in pathlib.PurePosixPath(path).parts or '\\' in path:
            raise ValueError('notes paths must be repository-relative')
        return subprocess.check_output(['git', 'show', f'{head}:{path}'], cwd=root, stderr=subprocess.PIPE)

    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', version):
        raise ValueError('notes require release version')
    if comparison is not None and not re.fullmatch(r'v[0-9]+\.[0-9]+\.[0-9]+', comparison):
        raise ValueError('notes require a semantic comparison release tag')
    data = json.loads(blob(manifest), parse_float=Decimal)
    if not isinstance(data, dict) or data.get('schema') != 2 or data.get('balance_reviewed') is not True:
        raise ValueError('release requires schema 2 notes with a completed balance review')
    if 'baseline' not in data or data['baseline'] != comparison:
        raise ValueError(f'notes baseline must match the previous release of this channel: {comparison!r}')
    entries = data.get('entries')
    if not isinstance(entries, list) or not entries:
        raise ValueError('release notes require at least one categorized item')

    sections = {True: {}, False: {}}
    media = {}
    translations = {lang: {} for lang in LANGUAGES}
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError('release notes require structured entries')
        highlight = entry.get('highlight', False)
        if type(highlight) is not bool:
            raise ValueError('notes highlight must be a boolean')
        names = tuple(words(entry.get('category_' + lang)) for lang in LANGUAGES)
        for index, lang in enumerate(LANGUAGES):
            if translations[lang].setdefault(names[index], names) != names:
                raise ValueError('each notes category must have a consistent translation')
        transition = balance_transition(entry['balance']) if 'balance' in entry else {}
        rendered = {lang: ['- ' + words(entry.get(lang))] for lang in LANGUAGES}
        if transition:
            for lang in LANGUAGES:
                rendered[lang][0] += f' — {transition[lang]}.'

        for image in entry.get('images', []):
            source = image['source_commit']
            if not re.fullmatch(r'[a-f0-9]{40}', source):
                raise ValueError('image needs exact source revision')
            subprocess.run(['git', 'merge-base', '--is-ancestor', source, head], cwd=root, check=True, capture_output=True)
            content = blob(image['path'])
            validate_png(content)
            digest = hashlib.sha256(content).hexdigest()
            name = 'notes-' + digest + '.png'
            media[name] = {'sha256': digest, 'path': image['path'], 'source_commit': source, 'bytes': content}
            url = f'https://github.com/afonasev/spacewars/releases/download/v{version}/{name}'
            for lang in LANGUAGES:
                caption = words(image[lang]).replace('[', '').replace(']', '')
                rendered[lang].append(f'  ![{caption}]({url})')
        sections[highlight].setdefault(names, []).append({lang: '\n'.join(lines) for lang, lines in rendered.items()})

    body = render_notes(sections, comparison)
    # Validate all inputs before touching output. Asset names include their full content hash.
    assets = pathlib.Path(assets)
    assets.mkdir(parents=True, exist_ok=True)
    for name, item in media.items():
        (assets / name).write_bytes(item['bytes'])
    return body, {
        'manifest': str(manifest),
        'head': head,
        'baseline_release': comparison,
        'balance_reviewed': True,
        'images': [{k: v for k, v in item.items() if k != 'bytes'} | {'asset': name} for name, item in media.items()],
    }
