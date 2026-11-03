#!/usr/bin/env python3
"""Keep planning Git textual while storing immutable artifact bytes externally."""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile

STORE = Path(os.environ.get('SPACEWARS_ARTIFACT_ROOT', str(Path.home()/'.local/share/spacewars/artifacts')))
TEXT_SUFFIXES = {'.md','.markdown','.rst','.json','.yaml','.yml','.toml','.txt','.ini','.cfg','.py','.sh'}
MAX_TEXT = 8*1024*1024

def textual(name, data):
    if len(data)>MAX_TEXT or b'\0' in data or data.startswith(b'version https://git-lfs.github.com/spec/v1\n'):
        return False
    if Path(name).suffix.lower() not in TEXT_SUFFIXES and Path(name).name not in {'.gitignore','.gitattributes','AGENTS.md'}:
        return False
    if 'evidence' in Path(name).parts and Path(name).suffix.lower()=='.json' and len(data)>256*1024:
        record=Path(name).name.lower()
        if not any(t in record for t in ['manifest','inventory','sha256','summary','verification','receipt','report','schema','contract','scope','delivery','status','source-bindings']):
            return False
    try:
        data.decode('utf-8')
        return True
    except UnicodeDecodeError:
        return False

def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream,'sha256').hexdigest()

def clone(source,target):
    target=Path(target);target.parent.mkdir(parents=True,exist_ok=True)
    if sys.platform=='darwin':
        libc=ctypes.CDLL('/usr/lib/libSystem.B.dylib',use_errno=True)
        if libc.clonefile(os.fsencode(source),os.fsencode(target),0)==0:
            return
    shutil.copy2(source,target)

def store_stream(name, stream):
    (STORE/'.tmp').mkdir(parents=True,exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=STORE/'.tmp',delete=False) as f:
        temporary=Path(f.name);sha=hashlib.sha256();size=0
        while chunk:=stream.read(1024*1024):
            sha.update(chunk);size+=len(chunk);f.write(chunk)
    oid=sha.hexdigest();body=STORE/'objects'/oid[:2]/oid[2:]
    body.parent.mkdir(parents=True,exist_ok=True)
    if body.exists():
        if digest(body)!=oid:
            temporary.unlink();raise ValueError('Artifact store checksum mismatch')
        temporary.unlink()
    else:
        os.replace(temporary,body);body.chmod(0o444)
    return {'spacewars_artifact':1,'sha256':oid,'bytes':size,'path':name,'store':str(STORE)}

def clean(name, stream, output):
    # The bounded first read also detects oversized text without loading a
    # complete Player/archive into memory.
    prefix=stream.read(MAX_TEXT+1)
    if textual(name,prefix):
        output.write(prefix);return
    class Prefixed:
        def __init__(self):self.first=prefix
        def read(self,size):
            if self.first:
                block=self.first[:size];self.first=self.first[size:];return block
            return stream.read(size)
    record=store_stream(name,Prefixed())
    output.write((json.dumps(record,sort_keys=True)+'\n').encode())

def git(root,*args,input=None):
    return subprocess.check_output(['git','-C',str(root),*args],input=input)

def pre_commit(root):
    root=Path(root)
    names=git(root,'diff','--cached','--name-only','--diff-filter=ACMRT','-z').split(b'\0')
    updates={}
    for raw in names:
        if not raw:continue
        name=os.fsdecode(raw)
        mode=git(root,'ls-files','--stage','--',name).split()[0]
        if mode==b'120000':continue
        if int(git(root,'cat-file','-s',':'+name))>MAX_TEXT:
            raise ValueError('Planning accepts text/manifests only; stage with the configured artifact filter: '+name)
        data=git(root,'show',':'+name)
        try:record=json.loads(data)
        except (json.JSONDecodeError,UnicodeDecodeError):record=None
        if not isinstance(record,dict) or record.get('spacewars_artifact')!=1:
            if not textual(name,data):
                raise ValueError('Nontext planning blob bypassed the artifact filter: '+name)
            continue
        body=Path(record['store'])/'objects'/record['sha256'][:2]/record['sha256'][2:]
        working=root/name
        if not body.is_file() or digest(body)!=record['sha256']:
            raise ValueError('External artifact was not saved: '+name)
        if working.is_symlink() or not working.is_file() or digest(working)!=record['sha256']:
            raise ValueError('Artifact changed after staging; git add it again: '+name)
        # A mutable compatibility view is separate from immutable CAS bytes.
        # Later writes through the old path cannot overwrite recovery content.
        view=Path(record['store'])/'latest'/name
        view.parent.mkdir(parents=True,exist_ok=True)
        temporary=view.with_name(view.name+'.planning-new')
        temporary.unlink(missing_ok=True);clone(body,temporary)
        temporary.chmod(stat.S_IMODE(working.stat().st_mode));os.replace(temporary,view)
        working.unlink();working.symlink_to(view)
        blob=git(root,'hash-object','-w','--stdin',input=os.fsencode(str(view))).decode().strip()
        git(root,'update-index','--add','--cacheinfo','120000,'+blob+','+name)
        parent=Path(name).parent
        updates.setdefault(parent,{})[Path(name).name]=record
    for parent,records in updates.items():
        path=root/parent/'external-artifacts.manifest.json'
        data=json.loads(path.read_text()) if path.exists() else {}
        data.update(records);path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n')
        git(root,'add','--',str(path.relative_to(root)))

def install(root,script=None):
    root=Path(root);script=Path(script or __file__).resolve()
    git(root,'config','filter.spacewars-plan-store.clean',f'python3 "{script}" clean %f')
    git(root,'config','filter.spacewars-plan-store.smudge','cat')
    git(root,'config','filter.spacewars-plan-store.required','true')
    hooks=Path(git(root,'rev-parse','--absolute-git-dir').decode().strip())/'hooks'
    path=hooks/'pre-commit';marker='# spacewars-text-planning-v1'
    if path.exists() and marker not in path.read_text():
        raise ValueError('Existing pre-commit hook must be chained explicitly')
    path.write_text(f'#!/bin/sh\n{marker}\nexec python3 "{script}" pre-commit "{root}"\n');path.chmod(0o755)

if __name__=='__main__':
    command=sys.argv[1]
    if command=='clean':clean(sys.argv[2],sys.stdin.buffer,sys.stdout.buffer)
    elif command=='pre-commit':pre_commit(sys.argv[2])
    elif command=='install':install(sys.argv[2])
    else:raise SystemExit('Unknown command')
