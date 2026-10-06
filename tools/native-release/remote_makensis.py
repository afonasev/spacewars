#!/usr/bin/env python3
"""NSIS compiler fallback on the configured VPS; private temporary packaging only."""
import os,pathlib,shlex,subprocess,sys
args=sys.argv[1:];values={a[2:].split('=',1)[0]:a[2:].split('=',1)[1] for a in args if a.startswith('-D') and '=' in a}
if set(values)-{'APPNAME'}!={'PAYLOAD','OUTFILE','VERSION','RELEASE'} or values.get('APPNAME','Spacewars') not in {'Spacewars','Spacewars Test'}:raise ValueError('expected only approved packaging parameters')
source=pathlib.Path(args[-1]).resolve();payload=pathlib.Path(values['PAYLOAD']).resolve();output=pathlib.Path(values['OUTFILE']).resolve();host='gfe'
def ssh(command,**kw):return subprocess.check_output(['ssh','-o','BatchMode=yes','-o','ConnectTimeout=15',host,command],**kw)
remote=ssh('mktemp -d /tmp/spacewars-native-package-XXXXXXXX',text=True).strip()
if not remote.startswith('/tmp/spacewars-native-package-') or not all(c.isalnum() or c in '/-_' for c in remote):raise ValueError('unexpected temporary path')
try:
 ssh('mkdir '+shlex.quote(remote+'/payload'))
 tar_env=os.environ.copy();tar_env['COPYFILE_DISABLE']='1'
 tar=subprocess.Popen(['tar','--no-xattrs','-C',str(payload),'-cf','-','.'],stdout=subprocess.PIPE,env=tar_env)
 receiver=subprocess.Popen(['ssh',host,'tar -xf - -C '+shlex.quote(remote+'/payload')],stdin=tar.stdout,stdout=subprocess.PIPE,stderr=subprocess.PIPE);tar.stdout.close();stdout,stderr=receiver.communicate()
 if receiver.returncode or tar.wait():raise RuntimeError('payload transfer failed: '+stderr.decode())
 ssh('cat > '+shlex.quote(remote+'/installer.nsi'),input=source.read_bytes())
 command=['makensis','-V2','-DPAYLOAD='+remote+'/payload','-DOUTFILE='+remote+'/Spacewars.exe','-DVERSION='+values['VERSION'],'-DRELEASE='+values['RELEASE'],'-DAPPNAME='+values.get('APPNAME','Spacewars'),remote+'/installer.nsi']
 print(ssh(shlex.join(command),text=True))
 subprocess.check_call(['scp',host+':'+remote+'/Spacewars.exe',str(output)])
finally:
 ssh('rm -rf -- '+shlex.quote(remote))
