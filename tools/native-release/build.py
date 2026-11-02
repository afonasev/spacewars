#!/usr/bin/env python3
"""Build native Player and bind its bytes to source revision before packaging."""
import argparse,datetime,hashlib,json,os,pathlib,subprocess,sys
import shutil
import importlib.util
_state_spec=importlib.util.spec_from_file_location("release_build_state",pathlib.Path(__file__).with_name("build_state.py"));build_state=importlib.util.module_from_spec(_state_spec);_state_spec.loader.exec_module(build_state)
ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools'))
from native_plugins import WINDOWS_RECAST, validate_windows_recast
def git(*args):return subprocess.check_output(['git',*args],cwd=ROOT)
def inventory(folder):
 return {str(p.relative_to(folder)):{'size':p.stat().st_size,'sha256':hashfile(p)} for p in sorted(folder.rglob('*')) if p.is_file()}
def hashfile(path):
 with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def debug_artifact(path):
    name = path.name
    return (name.endswith(('_BurstDebugInformation_DoNotShip', '_BackUpThisFolder_ButDontShipItWithYourGame', '.dSYM'))
            or name.lower().endswith(('.pdb', '.mdb')) or name == '.DS_Store')


def clean_debug_artifacts(folder):
    removed = []
    # Parents first, so a removed debug folder is never visited again.
    for path in sorted(folder.rglob('*'), key=lambda p: len(p.parts)):
        if path.exists() and debug_artifact(path):
            removed.append(str(path.relative_to(folder)))
            if path.is_dir():
                shutil.rmtree(path)
            else:
                path.unlink()
    return removed

def main():
 p=argparse.ArgumentParser();p.add_argument('--platform',choices=['macos-universal','windows-x64'],required=True);p.add_argument('--version',required=True);p.add_argument('--channel',choices=['test','production'],default='test');p.add_argument('--release-date',default=datetime.datetime.now(datetime.timezone.utc).date().isoformat(),help='Release date yyyy-MM-dd; default: UTC release preparation date');p.add_argument('--compression',choices=['none','lz4hc'],default='none');p.add_argument('--development',action='store_true',help='Build a Development test Player with native QA hooks');p.add_argument('--allow-dirty',action='store_true');a=p.parse_args()
 if datetime.datetime.strptime(a.release_date,'%Y-%m-%d').date().isoformat()!=a.release_date:raise ValueError('release date must be yyyy-MM-dd')
 if a.development and a.channel!='test':raise ValueError('Development Player requires test channel')
 revision=git('rev-parse','HEAD').decode().strip();dirty=bool(git('status','--porcelain'));diff=git('diff','HEAD');code_before=git('diff','HEAD','--','unity/Assets/Spacewars/Presentation','unity/Assets/Spacewars/Editor')
 if dirty and not a.allow_dirty:raise ValueError('release build requires clean committed source; --allow-dirty is test-only')
 out=ROOT/'unity/Builds/release'/('macOS' if a.platform=='macos-universal' else 'windows');out.mkdir(parents=True,exist_ok=True)
 identity=out/'native-build.json';identity.unlink(missing_ok=True)
 log=ROOT/'.local'/('bound-build-'+a.platform+'.log');log.parent.mkdir(exist_ok=True);log.unlink(missing_ok=True)
 command=[str(ROOT/'tools/unity.sh'),'shared','-batchmode','-quit']
 if a.platform=='windows-x64':command+=['-buildTarget','Win64']
 command+=['-executeMethod','Spacewars.Editor.PlayableProject.'+('ReleaseMac' if a.platform=='macos-universal' else 'ReleaseWindows'),'-logFile',str(log)]
 env=os.environ.copy();env['SPACEWARS_RELEASE_VERSION']=a.version;env['SPACEWARS_RELEASE_DATE']=a.release_date;env['SPACEWARS_RELEASE_CHANNEL']=a.channel;env['SPACEWARS_RELEASE_DEVELOPMENT']='1' if a.development else '0';env['SPACEWARS_RELEASE_COMPRESSION']=a.compression
 baseline=build_state.capture(ROOT,git)
 result=subprocess.run(command,cwd=ROOT,env=env)
 build_state.restore(ROOT,baseline,log.parent/('generated-'+a.platform),git)
 if result.returncode or not log.exists() or 'NATIVE_RELEASE_BUILD_PASS' not in log.read_text(errors='replace'):return 1
 if git('rev-parse','HEAD').decode().strip()!=revision:raise ValueError('source revision changed during build')
 # Release generation changes scene/settings/material data only. Code changes while compiling are rejected.
 if git('diff','HEAD','--','unity/Assets/Spacewars/Presentation','unity/Assets/Spacewars/Editor')!=code_before:raise ValueError('code changed during build')
 folder=out/'Player.app' if a.platform=='macos-universal' else out
 removed_debug_artifacts=clean_debug_artifacts(out)
 # Unity may already have sealed the nested macOS Player. Removing symbols
 # inside the bundle invalidates that seal; bind bytes only after re-signing.
 if a.platform=='macos-universal' and any(name.startswith('Player.app/') for name in removed_debug_artifacts):
  subprocess.check_call(['codesign','--force','--sign','-','--preserve-metadata=identifier,entitlements,flags',str(folder)])
  subprocess.check_call(['codesign','--verify','--strict',str(folder)])
 if a.platform=='windows-x64':validate_windows_recast(folder/WINDOWS_RECAST)
 files=inventory(folder);files.pop('native-build.json',None)
 record={'schema':1,'development':a.development,'compression':a.compression,'channel':a.channel,'test_match_logging':a.channel=='test','commit':revision,'dirty':dirty,'source_diff_sha256':hashlib.sha256(diff).hexdigest(),'version':a.version,'release_date':a.release_date,'platform':a.platform,'files':files,'build_log':str(log),'removed_debug_artifacts':removed_debug_artifacts}
 identity.write_text(json.dumps(record,indent=2)+'\n');print(json.dumps({k:v for k,v in record.items() if k!='files'}));return 0
if __name__=='__main__':sys.exit(main())
