#!/usr/bin/env python3
"""Build native Player and bind its bytes to source revision before packaging."""
import argparse,hashlib,json,os,pathlib,subprocess,sys
ROOT=pathlib.Path(__file__).resolve().parents[2]
def git(*args):return subprocess.check_output(['git',*args],cwd=ROOT)
def inventory(folder):
 return {str(p.relative_to(folder)):{'size':p.stat().st_size,'sha256':hashfile(p)} for p in sorted(folder.rglob('*')) if p.is_file()}
def hashfile(path):
 with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def main():
 p=argparse.ArgumentParser();p.add_argument('--platform',choices=['macos-universal','windows-x64'],required=True);p.add_argument('--version',required=True);p.add_argument('--allow-dirty',action='store_true');a=p.parse_args()
 revision=git('rev-parse','HEAD').decode().strip();dirty=bool(git('status','--porcelain'));diff=git('diff','HEAD');code_before=git('diff','HEAD','--','unity/Assets/Spacewars/Presentation','unity/Assets/Spacewars/Editor')
 if dirty and not a.allow_dirty:raise ValueError('release build requires clean committed source; --allow-dirty is test-only')
 out=ROOT/'unity/Builds/release'/('macOS' if a.platform=='macos-universal' else 'windows');out.mkdir(parents=True,exist_ok=True)
 identity=out/'native-build.json';identity.unlink(missing_ok=True)
 log=ROOT/'.local'/('bound-build-'+a.platform+'.log');log.parent.mkdir(exist_ok=True);log.unlink(missing_ok=True)
 command=[str(ROOT/'tools/unity.sh'),'shared','-batchmode','-quit']
 if a.platform=='windows-x64':command+=['-buildTarget','Win64']
 command+=['-executeMethod','Spacewars.Editor.PlayableProject.'+('ReleaseMac' if a.platform=='macos-universal' else 'ReleaseWindows'),'-logFile',str(log)]
 env=os.environ.copy();env['SPACEWARS_RELEASE_VERSION']=a.version
 result=subprocess.run(command,cwd=ROOT,env=env)
 if result.returncode or not log.exists() or 'NATIVE_RELEASE_BUILD_PASS' not in log.read_text(errors='replace'):return 1
 if git('rev-parse','HEAD').decode().strip()!=revision:raise ValueError('source revision changed during build')
 # Release generation changes scene/settings/material data only. Code changes while compiling are rejected.
 if git('diff','HEAD','--','unity/Assets/Spacewars/Presentation','unity/Assets/Spacewars/Editor')!=code_before:raise ValueError('code changed during build')
 folder=out/'Player.app' if a.platform=='macos-universal' else out
 files=inventory(folder);files.pop('native-build.json',None)
 record={'schema':1,'commit':revision,'dirty':dirty,'source_diff_sha256':hashlib.sha256(diff).hexdigest(),'version':a.version,'platform':a.platform,'files':files,'build_log':str(log)}
 identity.write_text(json.dumps(record,indent=2)+'\n');print(json.dumps({k:v for k,v in record.items() if k!='files'}));return 0
if __name__=='__main__':sys.exit(main())
