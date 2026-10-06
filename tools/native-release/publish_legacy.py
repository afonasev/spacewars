#!/usr/bin/env python3
"""Publish verified native files and landing in one atomic website generation.
Run only from a clean integrated main checkout after package and native QA.
"""
import re
import argparse,hashlib,json,pathlib,shlex,subprocess,tarfile,tempfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
def run(args,**kw):return subprocess.check_output([str(x) for x in args],text=True,**kw).strip()
def main():
 p=argparse.ArgumentParser();p.add_argument('--distribution',type=pathlib.Path,required=True);p.add_argument('--publish',action='store_true');a=p.parse_args()
 source=(a.distribution/'public/native/v1').resolve();catalog=json.loads((source/'downloads.json').read_text());artifacts=catalog['artifacts']
 if len({x['version'] for x in artifacts})!=1:raise ValueError('platform versions differ')
 if {x['platform'] for x in artifacts}!={'windows-x64','macos-universal'} or len(artifacts)!=2:raise ValueError('both verified platform installers required')
 if catalog.get('channel')!='production':raise ValueError('legacy production bridge cannot advertise test assets')
 head=run(['git','rev-parse','HEAD'],cwd=ROOT)
 if run(['git','branch','--show-current'],cwd=ROOT)!='main' or run(['git','status','--porcelain'],cwd=ROOT):raise ValueError('publish from clean main only')
 for item in artifacts:
  if item.get('dirty_build',True) or item.get('test_origin') or item['commit']!=head:raise ValueError('installer is not built from integrated main revision')
  if not re.fullmatch(r'https://github\.com/afonasev/spacewars/releases/download/v'+re.escape(item['version'])+r'/Spacewars-[A-Za-z0-9_.-]+',item['url']):raise ValueError('installer must be a versioned GitHub Release asset')
  artifact=source/'installers'/pathlib.Path(item['url']).name
  with artifact.open('rb') as f:sha=hashlib.file_digest(f,'sha256').hexdigest()
  if artifact.stat().st_size!=item['size'] or sha!=item['sha256']:raise ValueError('installer inventory mismatch')
 generation='native-'+head[:12]+'-'+str(artifacts[0]['version'])
 if not a.publish:print(json.dumps({'generation':generation,'artifacts':artifacts,'status':'preflight-only'}));return
 # Only the dedicated Spacewars routes/files are touched. Legacy desktop remains intact.
 host='gfe';base='/opt/spacewars';dest=base+'/native-generations/'+generation
 previous_site=run(['ssh',host,'readlink /opt/spacewars/current'])
 previous_feed=run(['ssh',host,'readlink /opt/spacewars/native/v1 || true'])
 if run(['ssh',host,'test -e '+shlex.quote(dest)+' && echo exists || echo absent'])!='absent':raise ValueError('immutable generation already exists')
 run(['ssh',host,'mkdir -p '+shlex.quote(dest+'/site')+' '+shlex.quote(dest+'/v1')])
 with tempfile.TemporaryDirectory(prefix='spacewars-native-publish-') as temp:
  archive=pathlib.Path(temp)/'release.tar'
  with tarfile.open(archive,'w') as tar:
   tar.add(source,arcname='v1',filter=lambda entry: None if entry.name=='v1/installers' or entry.name.startswith('v1/installers/') else entry);tar.add(ROOT/'landing',arcname='site')
  subprocess.check_call(['scp',archive,host+':'+dest+'/release.tar'])
  run(['ssh',host,'tar -xf '+shlex.quote(dest+'/release.tar')+' -C '+shlex.quote(dest)+' && rm '+shlex.quote(dest+'/release.tar')])
 # Retain prior content objects for installed clients. Only immutable hashes are linked.
 run(['ssh',host,'if test -d /opt/spacewars/native/v1/objects; then cp -anl /opt/spacewars/native/v1/objects/. '+shlex.quote(dest+'/v1/objects/')+'; fi'])
 # Installers are served by GitHub. Verify public bytes before advertising.
 import urllib.request
 for item in artifacts:
  h=hashlib.sha256();n=0
  with urllib.request.urlopen(item['url']) as response:
   while block:=response.read(1024*1024):h.update(block);n+=len(block)
  if h.hexdigest()!=item['sha256'] or n!=item['size']:raise ValueError('GitHub installer readback mismatch')
 # Caddy insert dedicated route above old desktop routing; no services/neighbors changed.
 config=run(['ssh',host,'cat /etc/caddy/Caddyfile'])
 marker='\t@desktopMetadata path /desktop/latest.json /desktop/downloads.json'
 insert='''\t@native path /native/*
\t@nativeObjects path /native/v1/objects/* /native/v1/installers/*
'''
 if '\t@native path /native/*' not in config:
  if marker not in config:raise ValueError('unexpected Spacewars Caddy configuration')
  config=config.replace(marker,insert+marker,1)
  route='\t\thandle @desktopMetadata {'
  config=config.replace(route,'''\t\thandle @native {
\t\t\theader Cache-Control "no-store"
\t\t\theader @nativeObjects Cache-Control "public, max-age=31536000, immutable"
\t\t\tfile_server
\t\t}
'''+route,1)
  run(['ssh',host,'cp /etc/caddy/Caddyfile /etc/caddy/Caddyfile.spacewars-native-backup; cat > /etc/caddy/Caddyfile.spacewars-native-candidate'],input=config)
  run(['ssh',host,'caddy validate --config /etc/caddy/Caddyfile.spacewars-native-candidate'])
  run(['ssh',host,'mv /etc/caddy/Caddyfile.spacewars-native-candidate /etc/caddy/Caddyfile && systemctl reload caddy'])
 try:
  run(['ssh',host,'mkdir -p /opt/spacewars/native; ln -s '+shlex.quote(dest+'/v1')+' /opt/spacewars/native/v1.next && mv -Tf /opt/spacewars/native/v1.next /opt/spacewars/native/v1; ln -s '+shlex.quote(dest+'/site')+' /opt/spacewars/current.native-next && mv -Tf /opt/spacewars/current.native-next /opt/spacewars/current'])
  # Independent public readback: stream both large artifacts rather than whole-file buffers.
  import urllib.request
  public=json.loads(urllib.request.urlopen('https://spacewars.afonasev.tech/native/v1/downloads.json').read())
  if public!=catalog:raise ValueError('public catalog differs')
  for item in artifacts:
   h=hashlib.sha256();n=0
   with urllib.request.urlopen(item['url']) as response:
    while block:=response.read(1024*1024):h.update(block);n+=len(block)
   if h.hexdigest()!=item['sha256'] or n!=item['size']:raise ValueError('public installer readback mismatch')
 except Exception:
  # Restore the previously advertised pointers; transferred immutable files remain for diagnosis.
  restore='ln -s '+shlex.quote(previous_site)+' /opt/spacewars/current.native-rollback && mv -Tf /opt/spacewars/current.native-rollback /opt/spacewars/current; '
  if previous_feed:
   restore+='ln -s '+shlex.quote(previous_feed)+' /opt/spacewars/native/v1.rollback && mv -Tf /opt/spacewars/native/v1.rollback /opt/spacewars/native/v1'
  else:restore+='rm /opt/spacewars/native/v1'
  run(['ssh',host,restore])
  raise
 # Remove only full native installers after both public assets and catalog pass.
 run(['ssh',host,r"find /opt/spacewars/native-generations -type f \( -path '*/v1/installers/Spacewars-*-Windows-x64.exe' -o -path '*/v1/installers/Spacewars-*-macOS-Universal.dmg' \) -delete"])
 print(json.dumps({'generation':generation,'commit':head,'artifacts':artifacts,'public_readback':'passed'}))
if __name__=='__main__':main()
