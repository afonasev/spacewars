#!/usr/bin/env python3
"""One command: exact-source builds -> signed packages -> upload metadata -> channel release.
No VPS operations. Use deploy_landing.py once and publish_legacy.py only for a production bridge.
"""
import importlib.util
import argparse,hashlib,json,os,pathlib,shutil,subprocess,sys,tempfile
_state_spec=importlib.util.spec_from_file_location("publication_state",pathlib.Path(__file__).with_name("publish_state.py"));state_module=importlib.util.module_from_spec(_state_spec);_state_spec.loader.exec_module(state_module)
ROOT=pathlib.Path(__file__).resolve().parents[2]
_notes_spec=importlib.util.spec_from_file_location('spacewars_release_notes',pathlib.Path(__file__).with_name('release_notes.py'));release_notes=importlib.util.module_from_spec(_notes_spec);_notes_spec.loader.exec_module(release_notes)
REPO='afonasev/spacewars'
PLATFORMS=('windows-x64','macos-universal')
def run(args,**kw):return subprocess.check_output([str(x) for x in args],text=True,**kw).strip()
def repository_url(protocol='auto'):
 if protocol=='auto':
  configured=subprocess.run(['gh','config','get','git_protocol','--host','github.com'],capture_output=True,text=True,check=True)
  protocol=configured.stdout.strip()
 if protocol not in ('ssh','https'):raise ValueError('configure gh git_protocol or pass --git-protocol ssh|https')
 return 'git@github.com:'+REPO+'.git' if protocol=='ssh' else 'https://github.com/'+REPO+'.git'
def prepare_output(path):
 # Never mix stale catalogs, packages or receipts into a new signed inventory.
 path=path.resolve()
 if path.exists() and any(path.iterdir()):raise ValueError('release output must be empty; preserve failed evidence and choose a fresh --output')
 path.mkdir(parents=True,exist_ok=True)
 return path
def hashfile(path):
 with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def validate_catalog(catalog,version,channel,head):
 if catalog.get('channel')!=channel or len(catalog['artifacts'])!=2 or {x['platform'] for x in catalog['artifacts']}!=set(PLATFORMS):raise ValueError('complete selected-channel pair required')
 for a in catalog['artifacts']:
  if a['version']!=version or a['commit']!=head or a.get('dirty_build',True) or a.get('test_origin') or a.get('channel')!=channel:raise ValueError('mixed/unpublishable build identity')
  expected=f'https://github.com/{REPO}/releases/download/v{version}/Spacewars-{version}-'+('Windows-x64.exe' if a['platform']=='windows-x64' else 'macOS-Universal.dmg')
  if a['url']!=expected:raise ValueError('wrong immutable installer URL')
def find_draft(releases,tag):
 matches=[r for r in releases if r['tag_name']==tag and r['draft']]
 if len(matches)!=1:raise ValueError('exact owned draft missing or ambiguous')
 return matches[0]
def source_names():
 # Public Unity source only; no historical private game/planning/history.
 names=subprocess.check_output(['git','ls-files','-z'],cwd=ROOT).decode().split('\0')
 return [n for n in names if n.startswith(('unity/Assets/','unity/Packages/','unity/ProjectSettings/','unity/Tests/','tools/','landing/','.github/')) or n=='docs/NATIVE_PROTOTYPE.md']
def safe_name(name):
 p=pathlib.PurePosixPath(name)
 if not name or name=='.' or p.is_absolute() or '..' in p.parts or '\\' in name or '\n' in name or '\t' in name:raise ValueError('unsafe public source path')
 return name

def clone_public(clone,repo_url):
 # Only public trees and the previous snapshot are needed; never fetch old Players/assets.
 run(['git','clone','--depth','1','--filter=blob:none','--no-checkout','https://github.com/'+REPO+'.git',clone])
 run(['git','config','gc.auto','0'],cwd=clone)
 # Do not hydrate historical blobs as delta bases while pushing from a partial clone.
 run(['git','config','pack.window','0'],cwd=clone)
 run(['git','config','pack.depth','0'],cwd=clone)
 run(['git','read-tree','HEAD'],cwd=clone)
 old=run(['git','show','HEAD:source-snapshot.json'],cwd=clone)
 (clone/'source-snapshot.json').write_text(old+'\n')
 run(['git','remote','set-url','origin',repo_url],cwd=clone)

def export_source(clone,head):
 names=[safe_name(n) for n in source_names()];previous=clone/'source-snapshot.json'
 old=json.loads(previous.read_text())['files'] if previous.exists() else {}
 removed=sorted(set(old)-set(names))
 for n in removed:
  safe_name(n);(clone/n).unlink(missing_ok=True)
 if removed:run(['git','update-index','--force-remove','--',*removed],cwd=clone)
 untouched=set(subprocess.check_output(['git','ls-files','-z'],cwd=clone).decode().rstrip('\0').split('\0'))-set(names)-{'source-snapshot.json'}
 if untouched:run(['git','update-index','--skip-worktree','--',*sorted(untouched)],cwd=clone)
 files={};modes={}
 for n in names:
  target=clone/n;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/n,target);files[n]=hashfile(target);modes[n]=oct((ROOT/n).stat().st_mode&0o777)
 snapshot={'schema':1,'build_source_commit':head,'export_policy':'current Unity/tooling/landing only; no private Git history','files':files,'file_modes':modes}
 previous.write_text(json.dumps(snapshot,indent=2)+'\n')
 # Populate current blobs/index directly: git add/verbose commit can lazily download old blobs.
 staged=names+['source-snapshot.json']
 hashes=subprocess.check_output(['git','hash-object','--no-filters','-w','--stdin-paths'],cwd=clone,input='\n'.join(staged)+'\n',text=True).splitlines()
 if len(hashes)!=len(staged):raise ValueError('incomplete public source blob export')
 entries=''.join(f'{"100755" if (clone/n).stat().st_mode&0o111 else "100644"} {sha}\t{n}\n' for n,sha in zip(staged,hashes))
 subprocess.run(['git','update-index','--add','--index-info'],cwd=clone,input=entries,text=True,check=True)
 changed=subprocess.run(['git','diff','--cached','--quiet'],cwd=clone)
 if changed.returncode==1:run(['git','commit','--quiet','-m','Spacewars desktop source '+head[:12]],cwd=clone)
 elif changed.returncode:raise ValueError('public source index comparison failed')
 if (ROOT/'.git').exists():assert_source(head)
 run(['git','push','origin','HEAD:main'],cwd=clone)
 return run(['git','rev-parse','HEAD'],cwd=clone),previous

def assert_source(head):
 if run(['git','rev-parse','HEAD'],cwd=ROOT)!=head or run(['git','status','--porcelain'],cwd=ROOT):raise ValueError('source changed during release; preserving output')

def validate_order(version,sequence,previous,document):
 if sequence<=document['sequence'] or tuple(map(int,version.split('.')))<=tuple(map(int,previous.lstrip('v').split('.'))):raise ValueError('release version and sequence must increase within the channel')

def uploaded(release,expected,channel,complete=True):
 state_module.check_upload(release,expected,channel,complete)

def api(route):return json.loads(run(['gh','api',f'repos/{REPO}/'+route]))

def owned_release(state,channel,tag):
 rid=state.data.get('release_id')
 if rid is None:return None
 release=api('releases/'+str(rid))
 if release['tag_name']!=tag or release['prerelease']!=(channel=='test'):raise ValueError('owned release identity differs')
 source=state.data['steps'].get('source',state.data['steps'].get('prepared',{})).get('public_commit')
 if source is not None and release.get('target_commitish')!=source:raise ValueError('owned release source differs')
 return release

def write_publication(out,release,head,public_commit,expected,channel,version):
 uploaded(release,expected,channel)
 if release['draft']:raise ValueError('release is still a draft')
 receipt={'version':version,'channel':channel,'url':release['html_url'],'commit':head,'public_commit':public_commit,'assets':expected,'verification':'local signed package verification and GitHub upload metadata','client_download_readback':'NotRun: user policy 2026-11-02; no draft/public payload downloads'}
 (out/'publication.json').write_text(json.dumps(receipt,indent=2)+'\n')
 print(release['html_url'])

def main():
 p=argparse.ArgumentParser()
 p.add_argument('--notes',help='committed bilingual notes JSON')
 p.add_argument('--version',required=True);p.add_argument('--sequence',type=int,required=True)
 p.add_argument('--channel',choices=['test','production'],required=True)
 p.add_argument('--production-approved',action='store_true');p.add_argument('--reuse-builds',action='store_true')
 p.add_argument('--publish',action='store_true');p.add_argument('--resume',action='store_true')
 p.add_argument('--prepare-legacy-bridge',action='store_true',help='only for an explicitly planned legacy migration')
 p.add_argument('--min-free-gib',type=float,default=2,help='minimum headroom before each heavy stage; not a reservation on a shared disk')
 p.add_argument('--git-protocol',choices=['auto','ssh','https'],default='auto')
 p.add_argument('--output',type=pathlib.Path)
 a=p.parse_args()
 if a.channel=='production' and not a.production_approved:raise ValueError('production requires explicit --production-approved')
 if a.min_free_gib<0 or a.sequence<1 or not __import__('re').fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+',a.version):raise ValueError('invalid release identity or disk headroom')
 head=run(['git','rev-parse','HEAD'],cwd=ROOT)
 if run(['git','status','--porcelain'],cwd=ROOT):raise ValueError('clean committed source required')
 tag='v'+a.version;out=a.output or ROOT/'.local/github-release'/a.channel/a.version
 identity={'commit':head,'version':a.version,'sequence':a.sequence,'channel':a.channel,'notes':a.notes,'legacy_bridge':a.prepare_legacy_bridge}
 state=state_module.State(out,identity,a.resume);out=state.root
 repo_url=repository_url(a.git_protocol);floor=int(a.min_free_gib*1024**3)
 release=owned_release(state,a.channel,tag)
 if release is None:
  matches=[r for r in api('releases?per_page=100') if r['tag_name']==tag]
  if matches:
   marker='<!-- spacewars-publisher:'+state.data['owner_token']+' -->'
   if len(matches)!=1 or not state.data.get('draft_intent') or marker not in matches[0].get('body',''):raise ValueError('immutable release/tag already exists; only an owned draft may resume')
   state.data['release_id']=matches[0]['id'];state.save();release=owned_release(state,a.channel,tag)
  elif run(['git','ls-remote',repo_url,'refs/tags/'+tag]):raise ValueError('immutable tag already exists')
 if release is not None and not release['draft']:
  prepared=state.get('prepared')
  if prepared is None:raise ValueError('published release has no verified local checkpoint')
  write_publication(out,release,head,prepared['public_commit'],prepared['expected'],a.channel,a.version);return
 assets=out/'public/native/v1/github';module=ROOT/'tools/native-release/updater'
 pub=json.loads((ROOT/'tools/native-release/trust.json').read_text())['public_key'];go=['go','run','-ldflags','-X main.publicKey='+pub,'.']
 prepared=state.get('prepared')
 if prepared is None or a.publish and prepared.get('public_commit') is None:
  assets.mkdir(parents=True,exist_ok=True)
  if state.get('notes') is None:
   with state.phase('notes'):
    previous=release_notes.previous_release(api('releases?per_page=100'),a.channel)
    baseline=None
    if previous:
     import base64
     doc=json.loads(run(['gh','release','download',previous,'--repo',REPO,'--pattern','release.json','--output','-']))['payload']
     document=json.loads(base64.b64decode(doc)) if isinstance(doc,str) else doc
     validate_order(a.version,a.sequence,previous,document)
     baseline=document.get('commit')
     if not baseline:raise ValueError('previous release has no source baseline')
    body,provenance=release_notes.generate(ROOT,head,baseline)
    if a.notes:body,authored=release_notes.authored(ROOT,head,a.notes,a.version,assets);provenance['authored']=authored
    body=body.replace('\n\n## English','\n\nУстановщики без подписи издателя ОС.\n\n## English')+'\nInstallers do not have an OS publisher signature.\n'
    (out/'release-notes.md').write_text(body+'\n<!-- spacewars-publisher:'+state.data['owner_token']+' -->\n')
    (out/'release-notes-source.json').write_text(json.dumps(provenance,indent=2)+'\n')
   state.record('notes',[out/'release-notes.md',out/'release-notes-source.json']+list(assets.glob('notes-*.png')))
  artifacts=[]
  for platform in PLATFORMS:
   key='package/'+platform;step=state.get(key)
   if step is None:
    build_dir=ROOT/'unity/Builds/release'/('windows' if platform=='windows-x64' else 'macOS')
    if not a.reuse_builds and not state.data['steps'].get('build/'+platform):
     state_module.space_guard(ROOT,(2 if (ROOT/'unity/Library').is_dir() else 5)*1024**3,floor)
     with state.phase('build/'+platform):
      subprocess.check_call([sys.executable,str(ROOT/'tools/native-release/build.py'),'--version',a.version,'--platform',platform,'--channel',a.channel])
      state.record('build/'+platform,identity_sha256=hashfile(build_dir/'native-build.json'))
    elif state.data['steps'].get('build/'+platform):
     if hashfile(build_dir/'native-build.json')!=state.data['steps']['build/'+platform]['identity_sha256']:raise ValueError('bound build identity changed during resume')
    state_module.space_guard(out,(5 if a.prepare_legacy_bridge else 3)*1024**3,floor)
    with state.phase(key):
     command=[sys.executable,str(ROOT/'tools/native-release/package.py'),'--version',a.version,'--sequence',str(a.sequence),'--channel',a.channel,'--platform',platform,'--output',str(out)]
     if a.prepare_legacy_bridge:command.append('--prepare-legacy-bridge')
     subprocess.check_call(command)
     catalog=json.loads((out/'public/native/v1/downloads.json').read_text());item=next(i for i in catalog['artifacts'] if i['platform']==platform)
     installer=out/'public/native/v1/installers'/pathlib.Path(item['url']).name
     assets.mkdir(parents=True,exist_ok=True);target=assets/installer.name
     if not target.exists():
      try:os.link(installer,target)
      except OSError:shutil.copy2(installer,target)
     elif not os.path.samefile(installer,target):shutil.copy2(installer,target)
     files=[target,assets/f'Spacewars-{a.version}-{platform}.json',assets/f'Spacewars-{a.version}-{platform}.pack']
     if a.prepare_legacy_bridge:
      files += [out/f'legacy-bridge-{platform}.json']+[f for f in (out/'legacy-bridge').rglob('*') if f.is_file()]
     state.record(key,files,artifact=item)
     step=state.data['steps'][key]
    # These are owned generated duplicates, not bound Players or signed assets.
    for folder in ['payload-'+platform,'install-'+platform]:
     if (out/folder).exists():shutil.rmtree(out/folder)
   artifacts.append(step['artifact'])
  catalog={'schema':1,'channel':a.channel,'artifacts':artifacts};validate_catalog(catalog,a.version,a.channel,head)
  (assets/'downloads.json').write_text(json.dumps(catalog,indent=2)+'\n')
  if a.prepare_legacy_bridge:
   import zipfile
   with zipfile.ZipFile(assets/f'Spacewars-{a.version}-Legacy-Bridge.zip','w',compression=zipfile.ZIP_DEFLATED,compresslevel=1) as archive:
    for f in sorted((out/'legacy-bridge').rglob('*')):
     if f.is_file():archive.write(f,f.relative_to(out/'legacy-bridge'))
    for platform in PLATFORMS:archive.write(out/f'legacy-bridge-{platform}.json',f'bridge-{platform}.json')
  for platform in PLATFORMS:
   key='verify-package/'+platform
   if state.get(key) is None:
    with state.phase(key):
     manifest=assets/f'Spacewars-{a.version}-{platform}.json';package=assets/f'Spacewars-{a.version}-{platform}.pack'
     run([*go,'--verify-package',manifest,platform,package],cwd=module)
     state.record(key,[manifest,package])
  public_commit=None
  if a.publish:
   source=state.get('source')
   if source is None:
    state_module.space_guard(out,1024**3,floor)
    with state.phase('public-source'):
     with tempfile.TemporaryDirectory(prefix='spacewars-public-source-') as temp:
      assert_source(head)
      clone=pathlib.Path(temp)/'repo';clone_public(clone,repo_url);public_commit,snapshot=export_source(clone,head);shutil.copy2(snapshot,assets/'source-snapshot.json')
     state.record('source',[assets/'source-snapshot.json'],public_commit=public_commit)
   else:public_commit=source['public_commit']
  with state.phase('sign-inventory'):
   inventory={n:r for n,r in state_module.inventory(assets).items() if n!='release.json'}
   payload=assets/'release-payload.json';payload.write_text(json.dumps({'schema':1,'version':a.version,'sequence':a.sequence,'channel':a.channel,'commit':head,'assets':inventory},sort_keys=True)+'\n')
   run([*go,'--sign-document',payload,pathlib.Path.home()/'.local/share/spacewars-release/native-ed25519.key',assets/'release.json'],cwd=module);payload.unlink()
   expected=state_module.inventory(assets)
   state.record('prepared',list(assets.iterdir())+[out/'release-notes.md',out/'release-notes-source.json'],public_commit=public_commit,expected=expected)
  prepared=state.data['steps']['prepared']
 if not a.publish:print(json.dumps({'status':'packaged-only','assets':str(assets),'resume':str(state.path)}));return
 assert_source(head)
 expected=prepared['expected'];public_commit=prepared['public_commit']
 if release is None:
  with state.phase('create-draft'):
   args=['gh','release','create',tag,'--repo',REPO,'--target',public_commit,'--draft','--title',f'Spacewars {a.version} ({a.channel})','--notes-file',out/'release-notes.md']
   if a.channel=='test':args+=['--prerelease','--latest=false']
   state.data['draft_intent']=True;state.save()
   run(args)
   release=find_draft(api('releases?per_page=100'),tag);state.data['release_id']=release['id'];state.save()
 with state.phase('upload'):
  release=owned_release(state,a.channel,tag)
  if not release['draft']:raise ValueError('release was published by another process; refusing mutation')
  # Only incomplete zero-byte starter assets in this proven-owned draft may be removed.
  for item in release['assets']:
   if item['name'] in expected and item.get('state')=='starter' and item['size']==0:
    fresh=owned_release(state,a.channel,tag)
    if not fresh['draft']:raise ValueError('cannot remove assets from a published release')
    run(['gh','api','--method','DELETE',f'repos/{REPO}/releases/assets/{item["id"]}'])
  release=owned_release(state,a.channel,tag)
  uploaded(release,expected,a.channel,complete=False)
  missing=set(expected)-{i['name'] for i in release['assets']}
  if missing:run(['gh','release','upload',tag,*[assets/n for n in sorted(missing)],'--repo',REPO])
  release=owned_release(state,a.channel,tag);uploaded(release,expected,a.channel)
  (out/'upload.json').write_text(json.dumps({'tag':tag,'assets':expected,'github_assets':release['assets'],'status':'metadata-verified','client_download_readback':'NotRun: user policy 2026-11-02'},indent=2)+'\n')
 with state.phase('publish'):
  release=owned_release(state,a.channel,tag)
  if not release['draft']:raise ValueError('release is already published; refusing mutation')
  uploaded(release,expected,a.channel)
  public_notes=out/'release-public-notes.md'
  marker='<!-- spacewars-publisher:'+state.data['owner_token']+' -->'
  public_notes.write_text((out/'release-notes.md').read_text().replace(marker+'\n',''))
  run(['gh','release','edit',tag,'--repo',REPO,'--notes-file',public_notes])
  release=owned_release(state,a.channel,tag)
  if not release['draft']:raise ValueError('release changed during finalization; refusing mutation')
  run(['gh','api','--method','PATCH',f'repos/{REPO}/releases/{release["id"]}','-F','draft=false','-f','make_latest='+('false' if a.channel=='test' else 'true')])
  published=api('releases/tags/'+tag)
  write_publication(out,published,head,public_commit,expected,a.channel,a.version)
 return 0
if __name__=='__main__':main()
