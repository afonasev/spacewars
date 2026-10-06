#!/usr/bin/env python3
"""One-time GitHub discovery landing deployment; never uploads update content to VPS."""
import hashlib,json,pathlib,shlex,subprocess,tarfile,tempfile,urllib.request
ROOT=pathlib.Path(__file__).resolve().parents[2]
def run(args,**kw):return subprocess.check_output([str(x) for x in args],text=True,**kw).strip()
def main():
 head=run(['git','rev-parse','HEAD'],cwd=ROOT)
 if run(['git','status','--porcelain'],cwd=ROOT):raise ValueError('clean committed landing required')
 dest='/opt/spacewars/native-generations/github-landing-'+head[:12];old=run(['ssh','gfe','readlink /opt/spacewars/current'])
 if run(['ssh','gfe','test -e '+shlex.quote(dest)+' && echo exists || echo absent'])!='absent':raise ValueError('immutable landing generation exists')
 with tempfile.TemporaryDirectory(prefix='spacewars-github-landing-') as temp:
  archive=pathlib.Path(temp)/'landing.tar'
  with tarfile.open(archive,'w') as tar:tar.add(ROOT/'landing',arcname='site')
  run(['ssh','gfe','mkdir -p '+shlex.quote(dest)]);run(['scp',archive,'gfe:'+dest+'/landing.tar']);run(['ssh','gfe','tar -xf '+shlex.quote(dest+'/landing.tar')+' -C '+shlex.quote(dest)+' && rm '+shlex.quote(dest+'/landing.tar')])
 run(['ssh','gfe','ln -s '+shlex.quote(dest+'/site')+' /opt/spacewars/current.github-next && mv -Tf /opt/spacewars/current.github-next /opt/spacewars/current'])
 try:
  raw=urllib.request.urlopen('https://spacewars.afonasev.tech/downloads.js').read()
  if raw!=(ROOT/'landing/downloads.js').read_bytes():raise ValueError('landing readback differs')
 except Exception:
  run(['ssh','gfe','ln -s '+shlex.quote(old)+' /opt/spacewars/current.github-rollback && mv -Tf /opt/spacewars/current.github-rollback /opt/spacewars/current']);raise
 print(json.dumps({'generation':dest,'commit':head,'previous':old,'sha256':hashlib.sha256(raw).hexdigest(),'legacy_feed':'unchanged'}))
if __name__=='__main__':main()
