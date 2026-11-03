import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SPEC=importlib.util.spec_from_file_location('planning_artifacts',Path(__file__).with_name('planning_artifacts.py'))
MOD=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(MOD)

class PlanningArtifactTests(unittest.TestCase):
    def test_text_is_unchanged_and_binary_is_external(self):
        with tempfile.TemporaryDirectory() as folder:
            previous=MOD.STORE;MOD.STORE=Path(folder)
            try:
                text=b'{"status":"NotRun"}\n';out=io.BytesIO();MOD.clean('manifest.json',io.BytesIO(text),out);self.assertEqual(text,out.getvalue())
                body=b'\0binary-source\xff'*1000;out=io.BytesIO();MOD.clean('raw.zip',io.BytesIO(body),out)
                record=json.loads(out.getvalue());path=MOD.STORE/'objects'/record['sha256'][:2]/record['sha256'][2:]
                self.assertEqual(path.read_bytes(),body)
                self.assertLess(len(out.getvalue()),1000)
            finally:MOD.STORE=previous

    def test_real_git_commit_keeps_links_and_immutable_recovery(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)/'planning';store=Path(folder)/'artifacts';root.mkdir()
            env=dict(os.environ,SPACEWARS_ARTIFACT_ROOT=str(store))
            def git(*args,input=None):return subprocess.check_output(['git','-C',str(root),*args],env=env,input=input,stderr=subprocess.STDOUT)
            git('init');git('config','user.name','Artifact test');git('config','user.email','artifact-test@invalid')
            (root/'.gitattributes').write_text('* filter=spacewars-plan-store -text\n')
            script=Path(__file__).with_name('planning_artifacts.py')
            subprocess.run(['python3',str(script),'install',str(root)],env=env,check=True)
            body=b'\0immutable-source-payload'*1000
            p=root/'evidence/run/raw.zip';p.parent.mkdir(parents=True);p.write_bytes(body)
            git('add','.');record=json.loads(git('show',':evidence/run/raw.zip'))
            git('commit','-m','External artifact')
            self.assertTrue(p.is_symlink());self.assertEqual(p.read_bytes(),body)
            self.assertLess(len(git('show','HEAD:evidence/run/raw.zip')),1000)
            original=store/'objects'/record['sha256'][:2]/record['sha256'][2:]
            p.write_bytes(b'next artifact');self.assertEqual(original.read_bytes(),body)
            self.assertTrue((p.parent/'external-artifacts.manifest.json').exists())

    def test_oversized_text_is_external(self):
        with tempfile.TemporaryDirectory() as folder:
            previous=MOD.STORE;MOD.STORE=Path(folder)
            try:
                out=io.BytesIO();MOD.clean('large.json',io.BytesIO(b' '* (MOD.MAX_TEXT+1)),out)
                self.assertEqual(json.loads(out.getvalue())['bytes'],MOD.MAX_TEXT+1)
            finally:MOD.STORE=previous

    def test_raw_json_data_is_external_but_manifest_remains_text(self):
        body=json.dumps({'values':'x'*(300*1024)}).encode()
        self.assertFalse(MOD.textual('evidence/run/input.json',body))
        self.assertTrue(MOD.textual('evidence/run/manifest.json',body))

if __name__=='__main__':unittest.main()
