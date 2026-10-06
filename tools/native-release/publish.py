#!/usr/bin/env python3
"""One command: exact-source builds -> signed packages -> verified draft -> channel release.
No VPS operations. Use deploy_landing.py once and publish_legacy.py only for a production bridge.
"""
import argparse,hashlib,json,os,pathlib,shutil,subprocess,sys,tempfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
REPO='afonasev/spacewars'
PLATFORMS=('windows-x64','macos-universal')
def run(args,**kw):return subprocess.check_output([str(x) for x in args],text=True,**kw).strip()
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
 names=run(['git','ls-files'],cwd=ROOT).splitlines()
 return [n for n in names if n.startswith(('unity/Assets/','unity/Packages/','unity/ProjectSettings/','unity/Tests/','tools/','landing/','.github/')) or n=='docs/NATIVE_PROTOTYPE.md']
def export_source(clone,head):
 names=source_names();previous=clone/'source-snapshot.json'
 if previous.exists():
  old=json.loads(previous.read_text())['files']
  for n in old:
   if n not in names and (clone/n).is_file():(clone/n).unlink()
 files={};modes={}
 for n in names:
  target=clone/n;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/n,target);files[n]=hashfile(target);modes[n]=oct((ROOT/n).stat().st_mode&0o777)
 snapshot={'schema':1,'build_source_commit':head,'export_policy':'current Unity/tooling/landing only; no private Git history','files':files,'file_modes':modes}
 previous.write_text(json.dumps(snapshot,indent=2)+'\n')
 run(['git','add','--',*names,'source-snapshot.json'],cwd=clone)
 if run(['git','status','--porcelain'],cwd=clone):run(['git','commit','-m','Spacewars desktop source '+head[:12]],cwd=clone)
 run(['git','push','origin','HEAD:main'],cwd=clone)
 return run(['git','rev-parse','HEAD'],cwd=clone),previous
GENERATED=('unity/Assets/Spacewars/Content/Scenes/Playable.unity','unity/ProjectSettings/ProjectSettings.asset')
def restore_build_generation():
 names=run(['git','diff','--name-only'],cwd=ROOT).splitlines()
 allowed=lambda n:n in GENERATED or n.startswith('unity/Assets/Spacewars/Content/Resources/StyleA/') and n.endswith('-foundation-concrete.mat')
 if any(not allowed(n) for n in names):raise ValueError('unexpected source changes during build; preserving all')
 if names:run(['git','restore','--',*names],cwd=ROOT)
def main():
 p=argparse.ArgumentParser();p.add_argument('--version',required=True);p.add_argument('--sequence',type=int,required=True);p.add_argument('--channel',choices=['test','production'],required=True);p.add_argument('--production-approved',action='store_true');p.add_argument('--reuse-builds',action='store_true');p.add_argument('--publish',action='store_true');p.add_argument('--output',type=pathlib.Path,default=ROOT/'.local/github-release');a=p.parse_args()
 if a.channel=='production' and not a.production_approved:raise ValueError('production requires explicit --production-approved; test is the approved default delivery')
 head=run(['git','rev-parse','HEAD'],cwd=ROOT)
 if run(['git','status','--porcelain'],cwd=ROOT):raise ValueError('clean committed source required')
 tag='v'+a.version
 prior=subprocess.run(['gh','release','view',tag,'--repo',REPO],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 if prior.returncode==0:raise ValueError('release already exists; never overwrite published/draft versions')
 # Also never retarget a previously published tag.
 refs=run(['git','ls-remote','https://github.com/'+REPO+'.git','refs/tags/'+tag])
 if refs:raise ValueError('immutable tag already exists')
 out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
 for platform in PLATFORMS:
  if not a.reuse_builds:
   subprocess.check_call([sys.executable,str(ROOT/'tools/native-release/build.py'),'--version',a.version,'--platform',platform]);restore_build_generation()
  subprocess.check_call([sys.executable,str(ROOT/'tools/native-release/package.py'),'--version',a.version,'--sequence',str(a.sequence),'--channel',a.channel,'--platform',platform,'--output',str(out),'--prepare-legacy-bridge'])
 feed=out/'public/native/v1';assets=feed/'github';catalog=json.loads((feed/'downloads.json').read_text());validate_catalog(catalog,a.version,a.channel,head)
 shutil.copy2(feed/'downloads.json',assets/'downloads.json')
 for item in catalog['artifacts']:shutil.copy2(feed/'installers'/pathlib.Path(item['url']).name,assets/pathlib.Path(item['url']).name)
 # Keep the production-host schema1 transition as a prepared, unadvertised artifact.
 import zipfile
 bridge=assets/f'Spacewars-{a.version}-Legacy-Bridge.zip'
 with zipfile.ZipFile(bridge,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=1) as zip:
  for f in sorted((out/'legacy-bridge').rglob('*')):
   if f.is_file():zip.write(f,f.relative_to(out/'legacy-bridge'))
  for platform in PLATFORMS:zip.write(out/f'legacy-bridge-{platform}.json',f'bridge-{platform}.json')
 pub=json.loads((ROOT/'tools/native-release/trust.json').read_text())['public_key'];go=['go','run','-ldflags','-X main.publicKey='+pub,'.'];module=ROOT/'tools/native-release/updater'
 for platform in PLATFORMS:run([*go,'--verify-package',assets/f'Spacewars-{a.version}-{platform}.json',platform,assets/f'Spacewars-{a.version}-{platform}.pack'],cwd=module)
 # Signed release-wide inventory authenticates installer hashes, build identity and channel.
 inventory={x.name:{'size':x.stat().st_size,'sha256':hashfile(x)} for x in sorted(assets.iterdir()) if x.is_file() and x.name not in ('release.json','release-payload.json')}
 payload=assets/'release-payload.json';payload.write_text(json.dumps({'schema':1,'version':a.version,'sequence':a.sequence,'channel':a.channel,'commit':head,'assets':inventory},sort_keys=True)+'\n')
 run([*go,'--sign-document',payload,pathlib.Path.home()/'.local/share/spacewars-release/native-ed25519.key',assets/'release.json'],cwd=module);payload.unlink()
 if not a.publish:print(json.dumps({'status':'packaged-only','assets':str(assets)}));return
 # Fresh clone preserves only the public history. Never push the private repository.
 with tempfile.TemporaryDirectory(prefix='spacewars-public-source-') as temp:
  clone=pathlib.Path(temp)/'repo';run(['git','clone','https://github.com/'+REPO+'.git',clone])
  public_commit,snapshot=export_source(clone,head);shutil.copy2(snapshot,assets/'source-snapshot.json')
 # Re-sign the final complete inventory including the public source snapshot.
 inventory={x.name:{'size':x.stat().st_size,'sha256':hashfile(x)} for x in sorted(assets.iterdir()) if x.is_file() and x.name!='release.json'}
 payload.write_text(json.dumps({'schema':1,'version':a.version,'sequence':a.sequence,'channel':a.channel,'commit':head,'assets':inventory},sort_keys=True)+'\n')
 run([*go,'--sign-document',payload,pathlib.Path.home()/'.local/share/spacewars-release/native-ed25519.key',assets/'release.json'],cwd=module);payload.unlink()
 files=sorted(x for x in assets.iterdir() if x.is_file());expected={x.name:{'size':x.stat().st_size,'sha256':hashfile(x)} for x in files}
 notes=out/'release-notes.md';notes.write_text(f'''Spacewars {a.version} — {a.channel} channel

Unity Windows x64 / macOS Universal. Existing icon; protected installation and default finish actions. GitHub-only Ed25519-authenticated differential updates: Check, explicit background Update, Restart or activation after exit. Profiles/settings retained.

Unsigned test distribution: expected Windows SmartScreen and macOS Gatekeeper notices. Metadata signing is separate from OS publisher signing. Physical Windows/UAC and Intel Mac acceptance remain pending.

Private build source: {head}; public snapshot: {public_commit}. No production/VPS feed cutover by this test publication.
''')
 args=['gh','release','create',tag,'--repo',REPO,'--target',public_commit,'--draft','--title',f'Spacewars {a.version} ({a.channel})','--notes-file',notes]
 if a.channel=='test':args+=['--prerelease','--latest=false']
 run(args);run(['gh','release','upload',tag,*files,'--repo',REPO])
 release=find_draft(json.loads(run(['gh','api',f'repos/{REPO}/releases?per_page=100'])),tag)
 if not release['draft'] or release['prerelease']!=(a.channel=='test') or {x['name'] for x in release['assets']}!=set(expected):raise ValueError('draft completeness/channel rejected')
 with tempfile.TemporaryDirectory(prefix='spacewars-draft-readback-') as temp:
  for item in release['assets']:
   target=pathlib.Path(temp)/item['name']
   with target.open('wb') as f:subprocess.check_call(['gh','api',f'repos/{REPO}/releases/assets/{item["id"]}','-H','Accept: application/octet-stream'],stdout=f)
   if target.stat().st_size!=expected[item['name']]['size'] or hashfile(target)!=expected[item['name']]['sha256']:raise ValueError('draft readback hash rejected')
  for platform in PLATFORMS:run([*go,'--verify-package',pathlib.Path(temp)/f'Spacewars-{a.version}-{platform}.json',platform,pathlib.Path(temp)/f'Spacewars-{a.version}-{platform}.pack'],cwd=module)
 (out/'draft-readback.json').write_text(json.dumps({'tag':tag,'assets':expected,'status':'verified','source':head,'public_source':public_commit},indent=2)+'\n')
 run(['gh','api','--method','PATCH',f'repos/{REPO}/releases/{release["id"]}','-F','draft=false','-f','make_latest='+('false' if a.channel=='test' else 'true')])
 published=json.loads(run(['gh','api',f'repos/{REPO}/releases/tags/{tag}']))
 if published['draft'] or published['prerelease']!=(a.channel=='test'):raise ValueError('publication channel readback failed')
 (out/'publication.json').write_text(json.dumps({'version':a.version,'channel':a.channel,'url':published['html_url'],'commit':head,'public_commit':public_commit,'assets':expected},indent=2)+'\n');print(published['html_url'])
if __name__=='__main__':main()
