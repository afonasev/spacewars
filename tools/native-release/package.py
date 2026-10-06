#!/usr/bin/env python3
"""Package native Players plus pinned differential updater, without network publication."""
import argparse, base64, hashlib, json, os, pathlib, plistlib, shutil, subprocess, sys, tempfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
UPDATER=ROOT/'tools/native-release/updater'
def run(args,**kw):
 return subprocess.check_output([str(x) for x in args],text=True,**kw).strip()
def validate_build_identity(build_dir,platform,version,head,allow_dirty=False):
 identity_path=build_dir/'native-build.json'
 if not identity_path.exists():raise ValueError('bound native build missing; use build.py')
 identity=json.loads(identity_path.read_text())
 if identity['commit']!=head or identity['version']!=version or identity['platform']!=platform:raise ValueError('native Player build identity differs from requested release')
 if identity['dirty'] and not allow_dirty:raise ValueError('dirty test Player cannot be packaged as a release')
 player_dir=build_dir if platform=='windows-x64' else build_dir/'Player.app'
 actual_names={str(p.relative_to(player_dir)) for p in player_dir.rglob('*') if p.is_file() and p.name!='native-build.json'}
 if actual_names!=set(identity['files']):raise ValueError('native Player file inventory changed after build')
 for name,record in identity['files'].items():
  f=player_dir/name
  with f.open('rb') as stream:actual=hashlib.file_digest(stream,'sha256').hexdigest()
  if f.stat().st_size!=record['size'] or actual!=record['sha256']:raise ValueError('native Player bytes differ from bound build: '+name)
 return identity

def main():
 p=argparse.ArgumentParser();p.add_argument('--version',required=True);p.add_argument('--sequence',required=True,type=int);p.add_argument('--platform',choices=['windows-x64','macos-universal'],required=True);p.add_argument('--test-origin');p.add_argument('--allow-dirty',action='store_true');p.add_argument('--output',type=pathlib.Path,default=ROOT/'.local/native-distribution');a=p.parse_args()
 if not all(c.isdigit() or c=='.' for c in a.version) or a.sequence<1:raise ValueError('invalid release version/sequence')
 if a.test_origin and (not a.allow_dirty or not a.test_origin.startswith('http://127.0.0.1:')):raise ValueError('test origin requires dirty smoke mode and loopback')
 out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
 build_dir=ROOT/'unity/Builds/release'/('windows' if a.platform=='windows-x64' else 'macOS')
 identity_path=build_dir/'native-build.json'
 head=run(['git','rev-parse','HEAD'],cwd=ROOT)
 identity=validate_build_identity(build_dir,a.platform,a.version,head,a.allow_dirty)
 key=pathlib.Path.home()/'.local/share/spacewars-release/native-ed25519.key'
 if not key.exists():run(['go','run','.','--keygen',key],cwd=UPDATER)
 private=base64.b64decode(key.read_text());pub=base64.b64encode(private[32:]).decode()
 trust=ROOT/'tools/native-release/trust.json'
 if trust.exists() and json.loads(trust.read_text())['public_key']!=pub:raise ValueError('signing key differs from committed trust')
 trust.write_text(json.dumps({'schema':1,'public_key':pub,'origin':'https://spacewars.afonasev.tech/native/v1'},indent=2)+'\n')
 env=os.environ.copy();env.update(CGO_ENABLED='0')
 binaries=out/'helpers';binaries.mkdir(exist_ok=True)
 for arch in (['amd64'] if a.platform=='windows-x64' else ['arm64','amd64']):
  env.update(GOOS='windows' if a.platform=='windows-x64' else 'darwin',GOARCH=arch)
  dest=binaries/(a.platform+'-'+arch)
  flags=f'-s -w -X main.publicKey={pub}'
  if a.test_origin:flags+=' -X main.origin='+a.test_origin
  if a.platform=='windows-x64':flags+=' -H windowsgui'
  if a.platform=='windows-x64':
   # Embed the canonical icon without adding generated .syso to source.
   with tempfile.TemporaryDirectory(prefix='spacewars-win-icon-') as temp:
    module=pathlib.Path(temp)/'updater';shutil.copytree(UPDATER,module)
    generator=pathlib.Path(temp)/'rsrc'
    run(['go','build','-o',generator,'github.com/akavel/rsrc'],cwd=UPDATER)
    run([generator,'-ico',ROOT/'tools/native-release/icons/Spacewars.ico','-arch','amd64','-o',module/'rsrc.syso'])
    run(['go','build','-trimpath','-ldflags',flags,'-o',dest,'.'],cwd=module,env=env)
  else:run(['go','build','-trimpath','-ldflags',flags,'-o',dest,'.'],cwd=UPDATER,env=env)
 if a.platform=='windows-x64':helper=binaries/(a.platform+'-amd64')
 else:
  helper=binaries/'macos-universal';run(['lipo','-create',binaries/'macos-universal-arm64',binaries/'macos-universal-amd64','-output',helper])
 payload=out/('payload-'+a.platform)
 if payload.exists():shutil.rmtree(payload)
 payload.mkdir()
 if a.platform=='windows-x64':
  player=ROOT/'unity/Builds/release/windows'
  if not (player/'Player.exe').is_file():raise ValueError('Windows release Player missing')
  shutil.copytree(player,payload,dirs_exist_ok=True);(payload/'native-build.json').unlink(missing_ok=True);shutil.copy2(helper,payload/'UpdateHost.exe');entry='Player.exe'
 else:
  player=ROOT/'unity/Builds/release/macOS/Player.app'
  if not player.is_dir():raise ValueError('macOS release Player missing')
  shutil.copytree(player,payload/'Player.app',symlinks=True);shutil.copy2(helper,payload/'UpdateHost');entry='Player.app/Contents/MacOS/Player'
  # ProductName determines Unity's bundle executable; inspect it instead of guessing.
  info=plistlib.loads((player/'Contents/Info.plist').read_bytes());entry='Player.app/Contents/MacOS/'+info['CFBundleExecutable']
 commit=identity['commit']
 feed=out/'public/native/v1';release=run(['go','run','.','--pack',payload,feed,key,a.platform,entry,a.version,a.sequence,commit],cwd=UPDATER)
 raw=(feed/a.platform/'latest.json').read_bytes()
 install=out/('install-'+a.platform)
 if install.exists():shutil.rmtree(install)
 resources=install if a.platform=='windows-x64' else install/'Spacewars.app/Contents/Resources'
 resources.mkdir(parents=True)
 target=resources/'releases'/release;shutil.copytree(payload,target);(target/'manifest.json').write_bytes(raw)
 (resources/'active.json').write_text(json.dumps({'id':release}))
 artifacts=feed/'installers';artifacts.mkdir(parents=True,exist_ok=True)
 if a.platform=='windows-x64':
  shutil.copy2(helper,resources/'Spacewars.exe');shutil.copy2(ROOT/'tools/native-release/icons/Spacewars.ico',resources/'Spacewars.ico');artifact=artifacts/f'Spacewars-{a.version}-Windows-x64.exe'
  compiler=[sys.executable,ROOT/'tools/native-release/remote_makensis.py'] if os.environ.get('SPACEWARS_REMOTE_NSIS')=='1' else ['makensis']
  run([*compiler,f'-DPAYLOAD={install}',f'-DOUTFILE={artifact}',f'-DVERSION={a.version}',f'-DRELEASE={release}',ROOT/'tools/native-release/installer.nsi'])
 else:
  shutil.copy2(ROOT/'tools/native-release/icons/Spacewars.icns',resources/'Spacewars.icns')
  contents=install/'Spacewars.app/Contents';(contents/'MacOS').mkdir();shutil.copy2(helper,contents/'MacOS/Spacewars')
  (contents/'Info.plist').write_bytes(plistlib.dumps({'CFBundleName':'Spacewars','CFBundleDisplayName':'Spacewars','CFBundleIdentifier':'tech.afonasev.spacewars','CFBundleExecutable':'Spacewars','CFBundlePackageType':'APPL','CFBundleIconFile':'Spacewars.icns','CFBundleShortVersionString':a.version,'CFBundleVersion':str(a.sequence),'LSMinimumSystemVersion':'11.0','NSHighResolutionCapable':True}))
  run(['codesign','--force','--sign','-',install/'Spacewars.app'])
  (install/'Applications').symlink_to('/Applications')
  artifact=artifacts/f'Spacewars-{a.version}-macOS-Universal.dmg';artifact.unlink(missing_ok=True)
  run(['hdiutil','create','-volname','Spacewars','-srcfolder',install,'-ov','-format','UDZO',artifact])
 catalog=feed/'downloads.json';data=json.loads(catalog.read_text()) if catalog.exists() else {'schema':1,'artifacts':[]}
 with artifact.open('rb') as f:sha=hashlib.file_digest(f,'sha256').hexdigest()
 item={'platform':a.platform,'version':a.version,'commit':commit,'release':release,'size':artifact.stat().st_size,'sha256':sha,'url':f'https://github.com/afonasev/spacewars/releases/download/v{a.version}/'+artifact.name,'unsigned':True,'dirty_build':identity['dirty'],'test_origin':a.test_origin,'build_identity_sha256':hashlib.sha256(identity_path.read_bytes()).hexdigest()}
 data['artifacts']=[x for x in data['artifacts'] if x['platform']!=a.platform]+[item];catalog.write_text(json.dumps(data,indent=2)+'\n');print(json.dumps(item))
if __name__=='__main__':main()
