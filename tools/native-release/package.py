#!/usr/bin/env python3
"""Package native Players plus pinned differential updater, without network publication."""
import argparse, base64, hashlib, json, os, pathlib, plistlib, shutil, subprocess, sys, tempfile, struct
import xml.etree.ElementTree as ET
ROOT=pathlib.Path(__file__).resolve().parents[2]
UPDATER=ROOT/'tools/native-release/updater'
def run(args,**kw):
 return subprocess.check_output([str(x) for x in args],text=True,**kw).strip()
def prepare_volume_icon(install,icon):
 # Finder reads the mounted volume icon from its root and custom-icon flag.
 shutil.copy2(icon,install/'.VolumeIcon.icns')
 run(['SetFile','-a','C',install])
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

def windows_manifest(executable):
    """Read the process manifest from PE resources, not an incidental XML string."""
    data=pathlib.Path(executable).read_bytes()
    try:
        if data[:2]!=b'MZ':raise ValueError('Not a Windows PE executable')
        pe=struct.unpack_from('<I',data,0x3c)[0]
        if data[pe:pe+4]!=b'PE\0\0':raise ValueError('Invalid PE header')
        count=struct.unpack_from('<H',data,pe+6)[0]
        optional_size=struct.unpack_from('<H',data,pe+20)[0];optional=pe+24
        magic=struct.unpack_from('<H',data,optional)[0]
        if magic not in (0x10b,0x20b):raise ValueError('Unsupported PE optional header')
        directory=optional+(112 if magic==0x20b else 96)
        resource_rva=struct.unpack_from('<I',data,directory+16)[0]
        sections=optional+optional_size
        def offset(rva):
            for i in range(count):
                _,address,size,start=struct.unpack_from('<IIII',data,sections+i*40+8)
                if address<=rva<address+size:return start+rva-address
            raise ValueError('PE resource RVA is outside file-backed sections')
        root=offset(resource_rva)
        def entries(at):
            named,ids=struct.unpack_from('<HH',data,at+12)
            return [struct.unpack_from('<II',data,at+16+i*8) for i in range(named+ids)]
        def child(at,key):
            for ident,value in entries(at):
                if ident==key and value&0x80000000:return root+(value&0x7fffffff)
            raise ValueError('Windows bootstrap process manifest is missing')
        languages=entries(child(child(root,24),1))
        if not languages or languages[0][1]&0x80000000:raise ValueError('Invalid manifest language resource')
        rva,size=struct.unpack_from('<II',data,root+languages[0][1])
        start=offset(rva)
        if start+size>len(data):raise ValueError('Truncated manifest resource')
        return data[start:start+size]
    except struct.error as error:
        raise ValueError('Truncated Windows PE resource table') from error


def validate_windows_dpi(executable):
    manifest=ET.fromstring(windows_manifest(executable))
    legacy=manifest.find('.//{http://schemas.microsoft.com/SMI/2005/WindowsSettings}dpiAware')
    modern=manifest.find('.//{http://schemas.microsoft.com/SMI/2016/WindowsSettings}dpiAwareness')
    if legacy is None or legacy.text!='true' or modern is None or modern.text!='PerMonitorV2, PerMonitor':
        raise ValueError('Windows bootstrap must declare per-monitor DPI awareness')


def build_windows_helper(destination, flags, env, bridge=False):
 # Generate resources in a private module copy; never leave .syso in source.
 with tempfile.TemporaryDirectory(prefix='spacewars-win-dpi-') as temp:
  module=pathlib.Path(temp)/'updater';shutil.copytree(UPDATER,module)
  generator=pathlib.Path(temp)/'rsrc'
  run(['go','build','-o',generator,'github.com/akavel/rsrc'],cwd=UPDATER)
  run([generator,'-ico',ROOT/'tools/native-release/icons/Spacewars.ico',
       '-manifest',ROOT/'tools/native-release/bootstrap.manifest',
       '-arch','amd64','-o',module/'rsrc.syso'])
  run(['go','build','-trimpath','-ldflags',flags,'-o',destination,'.'],cwd=module,env=env)
  validate_windows_dpi(destination)
  if bridge:
   bridge_destination=pathlib.Path(str(destination)+'-bridge')
   run(['go','build','-trimpath','-ldflags',flags.replace('main.channel=test','main.channel=production'),
        '-o',bridge_destination,'.'],cwd=module,env=env)
   validate_windows_dpi(bridge_destination)

def main():
 p=argparse.ArgumentParser();p.add_argument('--version',required=True);p.add_argument('--sequence',required=True,type=int);p.add_argument('--platform',choices=['windows-x64','macos-universal'],required=True);p.add_argument('--prepare-legacy-bridge',action='store_true');p.add_argument('--channel',choices=['production','test'],required=True);p.add_argument('--test-origin');p.add_argument('--allow-dirty',action='store_true');p.add_argument('--output',type=pathlib.Path,default=ROOT/'.local/native-distribution');a=p.parse_args()
 if not all(c.isdigit() or c=='.' for c in a.version) or a.sequence<1:raise ValueError('invalid release version/sequence')
 if a.test_origin and (not a.allow_dirty or not a.test_origin.startswith('http://127.0.0.1:')):raise ValueError('test origin requires dirty smoke mode and loopback')
 appname='Spacewars Test' if a.channel=='test' else 'Spacewars'
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
  flags=f'-s -w -X main.publicKey={pub} -X main.channel={a.channel}'
  if a.test_origin:flags+=' -X main.origin='+a.test_origin
  if a.platform=='windows-x64':flags+=' -H windowsgui'
  if a.platform=='windows-x64':
   build_windows_helper(dest,flags,env,a.prepare_legacy_bridge)
  else:
   run(['go','build','-trimpath','-ldflags',flags,'-o',dest,'.'],cwd=UPDATER,env=env)
   if a.prepare_legacy_bridge:run(['go','build','-trimpath','-ldflags',flags.replace('main.channel=test','main.channel=production'),'-o',str(dest)+'-bridge','.'],cwd=UPDATER,env=env)
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
 feed=out/'public/native/v1';release=run(['go','run','.','--pack',payload,feed,key,a.platform,entry,a.version,a.sequence,commit,a.channel],cwd=UPDATER)
 raw=(feed/a.platform/'latest.json').read_bytes()
 if a.prepare_legacy_bridge:
  bridge_payload=out/('bridge-payload-'+a.platform)
  if bridge_payload.exists():shutil.rmtree(bridge_payload)
  shutil.copytree(payload,bridge_payload)
  if a.platform=='windows-x64':bridge_helper=pathlib.Path(str(binaries/'windows-x64-amd64')+'-bridge')
  else:
   bridge_helper=binaries/'macos-universal-bridge';run(['lipo','-create',str(binaries/'macos-universal-arm64')+'-bridge',str(binaries/'macos-universal-amd64')+'-bridge','-output',bridge_helper])
  shutil.copy2(bridge_helper,bridge_payload/('UpdateHost.exe' if a.platform=='windows-x64' else 'UpdateHost'))
  bridge_feed=out/'legacy-bridge/v1'
  bridge_id=run(['go','run','.','--pack',bridge_payload,bridge_feed,key,a.platform,entry,a.version,a.sequence,commit],cwd=UPDATER)
  (out/('legacy-bridge-'+a.platform+'.json')).write_text(json.dumps({'platform':a.platform,'version':a.version,'sequence':a.sequence,'id':bridge_id,'schema':1,'new_host_channel':'production','advertised':False})+'\n')
 install=out/('install-'+a.platform)
 if install.exists():shutil.rmtree(install)
 resources=install if a.platform=='windows-x64' else install/(appname+'.app')/'Contents/Resources'
 resources.mkdir(parents=True)
 target=resources/'releases'/release;shutil.copytree(payload,target);(target/'manifest.json').write_bytes(raw)
 (resources/'active.json').write_text(json.dumps({'id':release}))
 artifacts=feed/'installers';artifacts.mkdir(parents=True,exist_ok=True)
 if a.platform=='windows-x64':
  shutil.copy2(helper,resources/'Spacewars.exe');shutil.copy2(ROOT/'tools/native-release/icons/Spacewars.ico',resources/'Spacewars.ico');artifact=artifacts/f'Spacewars-{a.version}-Windows-x64.exe'
  compiler=[sys.executable,ROOT/'tools/native-release/remote_makensis.py'] if os.environ.get('SPACEWARS_REMOTE_NSIS')=='1' else ['makensis']
  run([*compiler,f'-DPAYLOAD={install}',f'-DOUTFILE={artifact}',f'-DVERSION={a.version}',f'-DRELEASE={release}',f'-DAPPNAME={"Spacewars Test" if a.channel=="test" else "Spacewars"}',ROOT/'tools/native-release/installer.nsi'])
 else:
  shutil.copy2(ROOT/'tools/native-release/icons/Spacewars.icns',resources/'Spacewars.icns')
  contents=install/(appname+'.app')/'Contents';(contents/'MacOS').mkdir();shutil.copy2(helper,contents/'MacOS/Spacewars')
  (contents/'Info.plist').write_bytes(plistlib.dumps({'CFBundleName':appname,'CFBundleDisplayName':appname,'CFBundleIdentifier':'tech.afonasev.spacewars'+('.test' if a.channel=='test' else ''),'CFBundleExecutable':'Spacewars','CFBundlePackageType':'APPL','CFBundleIconFile':'Spacewars.icns','CFBundleShortVersionString':a.version,'CFBundleVersion':str(a.sequence),'LSMinimumSystemVersion':'11.0','NSHighResolutionCapable':True}))
  run(['codesign','--force','--sign','-',install/(appname+'.app')])
  (install/'Applications').symlink_to('/Applications')
  prepare_volume_icon(install,ROOT/'tools/native-release/icons/Spacewars.icns')
  artifact=artifacts/f'Spacewars-{a.version}-macOS-Universal.dmg';artifact.unlink(missing_ok=True)
  run(['hdiutil','create','-volname','Spacewars','-srcfolder',install,'-ov','-format','UDZO',artifact])
 catalog=feed/'downloads.json';data=json.loads(catalog.read_text()) if catalog.exists() else {'schema':1,'artifacts':[]}
 with artifact.open('rb') as f:sha=hashlib.file_digest(f,'sha256').hexdigest()
 item={'channel':a.channel,'platform':a.platform,'version':a.version,'commit':commit,'release':release,'size':artifact.stat().st_size,'sha256':sha,'url':f'https://github.com/afonasev/spacewars/releases/download/v{a.version}/'+artifact.name,'unsigned':True,'dirty_build':identity['dirty'],'test_origin':a.test_origin,'build_identity_sha256':hashlib.sha256(identity_path.read_bytes()).hexdigest()}
 data['channel']=a.channel
 data['artifacts']=[x for x in data['artifacts'] if x['platform']!=a.platform]+[item];catalog.write_text(json.dumps(data,indent=2)+'\n');print(json.dumps(item))
if __name__=='__main__':main()
