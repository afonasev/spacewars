import pathlib,runpy,subprocess,sys,tempfile,unittest
from unittest.mock import Mock,patch

SCRIPT=pathlib.Path(__file__).parent/'native-release/remote_makensis.py'
class RemoteCompilerTests(unittest.TestCase):
 def invoke(self,remote='/var/tmp/spacewars-native-package-owned',fail=False):
  calls=[]
  def ssh(args,**kwargs):
   command=args[-1];calls.append(command)
   if command.startswith('mktemp '):return remote+'\n'
   if ' makensis ' in command and fail:raise subprocess.CalledProcessError(1,args)
   return ''
  with tempfile.TemporaryDirectory() as temp:
   source=pathlib.Path(temp)/'installer.nsi';source.write_text('installer')
   tar=Mock();tar.wait.return_value=0
   receiver=Mock();receiver.returncode=0;receiver.communicate.return_value=(b'',b'')
   args=[str(SCRIPT),'-DPAYLOAD='+temp,'-DOUTFILE='+temp+'/setup.exe','-DVERSION=0.6.14','-DRELEASE=source',str(source)]
   with patch.object(sys,'argv',args),patch('subprocess.check_output',side_effect=ssh),patch('subprocess.Popen',side_effect=[tar,receiver]),patch('subprocess.check_call') as scp:
    try:runpy.run_path(str(SCRIPT),run_name='__main__')
    except Exception:return calls,scp.call_count,sys.exc_info()[1]
  return calls,scp.call_count,None
 def test_payload_output_and_nsis_scratch_share_owned_disk_directory(self):
  calls,copies,error=self.invoke();self.assertIsNone(error);self.assertEqual(copies,1)
  self.assertEqual(calls[0],'mktemp -d /var/tmp/spacewars-native-package-XXXXXXXX')
  compiler=next(c for c in calls if ' makensis ' in c)
  self.assertIn('env TMPDIR=/var/tmp/spacewars-native-package-owned makensis ',compiler)
  self.assertIn('-DOUTFILE=/var/tmp/spacewars-native-package-owned/Spacewars.exe',compiler)
  self.assertEqual(calls[-1],'rm -rf -- /var/tmp/spacewars-native-package-owned')
 def test_failure_still_removes_only_owned_directory(self):
  calls,copies,error=self.invoke(fail=True);self.assertIsInstance(error,subprocess.CalledProcessError);self.assertEqual(copies,0)
  self.assertEqual(calls[-1],'rm -rf -- /var/tmp/spacewars-native-package-owned')
 def test_unexpected_remote_path_is_not_used_or_deleted(self):
  for path in ['/tmp/spacewars-native-package-owned','/var/tmp/unrelated','/var/tmp/spacewars-native-package-;bad']:
   calls,copies,error=self.invoke(remote=path);self.assertIsInstance(error,ValueError);self.assertEqual(len(calls),1);self.assertEqual(copies,0)
