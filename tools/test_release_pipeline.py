"""Release routing, crash recovery and source guards; never starts Unity or GitHub."""
import copy
import importlib.util
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch


def load(name):
    spec=importlib.util.spec_from_file_location(name,pathlib.Path(__file__).parent/'native-release'/(name+'.py'))
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module

pub=load('publish');bs=load('build_state');ps=pub.state_module


class Checkpoints(unittest.TestCase):
    def test_resume_binds_every_identity_field_and_bytes(self):
        with tempfile.TemporaryDirectory() as t:
            root=pathlib.Path(t)/'release';identity={'commit':'one','version':'1','channel':'production','sequence':1,'notes':None,'legacy':False}
            state=ps.State(root,identity);f=root/'package';f.write_bytes(b'original');state.record('package',[f])
            self.assertIsNotNone(ps.State(root,identity,True).get('package'))
            for key in identity:
                with self.assertRaises(ValueError):ps.State(root,dict(identity,**{key:'different'}),True)
            f.write_bytes(b'changed')
            with self.assertRaises(ValueError):ps.State(root,identity,True).get('package')
            with self.assertRaises(ValueError):ps.State(root,identity)
    def test_failed_phase_is_not_marked_complete(self):
        with tempfile.TemporaryDirectory() as t:
            state=ps.State(pathlib.Path(t)/'out',{})
            with self.assertRaises(RuntimeError):
                with state.phase('upload'):raise RuntimeError('connection lost')
            self.assertEqual(state.data['attempts'][-1]['status'],'FAIL');self.assertIsNone(state.get('upload'))
    def test_metadata_checks_names_sizes_state_channel_and_optional_digest(self):
        expected={'client.exe':{'size':4,'sha256':'abcd'}}
        release={'prerelease':False,'assets':[{'name':'client.exe','size':4,'state':'uploaded','digest':'sha256:abcd'}]}
        ps.check_upload(release,expected,'production')
        for key,value in [('name','other'),('size',5),('state','starter'),('digest','sha256:wrong')]:
            bad=copy.deepcopy(release);bad['assets'][0][key]=value
            with self.assertRaises(ValueError):ps.check_upload(bad,expected,'production')
        with self.assertRaises(ValueError):ps.check_upload(release,expected,'test')
        bad=copy.deepcopy(release);bad['assets']*=2
        with self.assertRaises(ValueError):ps.check_upload(bad,expected,'production')
        del release['assets'][0]['digest'];ps.check_upload(release,expected,'production')
    def test_channel_version_and_sequence_must_both_increase(self):
        pub.validate_order('1.2.4',9,'v1.2.3',{'sequence':8})
        for version,sequence in [('1.2.3',9),('1.2.2',9),('1.2.4',8),('1.2.4',7)]:
            with self.assertRaises(ValueError):pub.validate_order(version,sequence,'v1.2.3',{'sequence':8})
    def test_disk_guard_preserves_outputs(self):
        with patch.object(ps.shutil,'disk_usage',return_value=type('Disk',(),{'free':20})()):
            ps.space_guard('.',10,10)
            with self.assertRaises(ValueError):ps.space_guard('.',11,10)


class GeneratedSource(unittest.TestCase):
    def repo(self,t):
        root=pathlib.Path(t);subprocess.run(['git','init','-q'],cwd=root,check=True)
        subprocess.run(['git','config','user.name','Fixture'],cwd=root,check=True);subprocess.run(['git','config','user.email','fixture@example.invalid'],cwd=root,check=True)
        return root
    def write(self,root,name,content):
        p=root/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(content);return p
    def commit(self,root):
        subprocess.run(['git','add','.'],cwd=root,check=True);subprocess.run(['git','commit','-qm','fixture'],cwd=root,check=True)
    def test_known_outputs_restored_to_exact_dirty_baseline(self):
        with tempfile.TemporaryDirectory() as t:
            root=self.repo(t);p=self.write(root,bs.LAB,b'original');other=self.write(root,'runtime.cs',b'unchanged');self.commit(root)
            p.write_bytes(b'preexisting edit');other.write_bytes(b'preexisting code edit');baseline=bs.capture(root);p.write_bytes(b'generated')
            bs.restore(root,baseline,root/'.git/evidence')
            self.assertEqual(p.read_bytes(),b'preexisting edit');self.assertEqual(other.read_bytes(),b'preexisting code edit')
    def test_unexpected_code_or_quality_preserves_all_changes(self):
        for path,before,after in [('runtime.cs',b'code',b'changed'),(bs.PIPELINE,b'  m_ShadowDistance: 50\n',b'  m_ShadowDistance: 0\n')]:
            with self.subTest(path=path),tempfile.TemporaryDirectory() as t:
                root=self.repo(t);p=self.write(root,path,before);lab=self.write(root,bs.LAB,b'old');self.commit(root);baseline=bs.capture(root);p.write_bytes(after);lab.write_bytes(b'new')
                with self.assertRaises(ValueError):bs.restore(root,baseline,root/'.git/evidence')
                self.assertEqual(p.read_bytes(),after);self.assertEqual(lab.read_bytes(),b'new')
    def test_shader_prefilter_and_generated_map_only(self):
        bs.validate_change(bs.PIPELINE,b'  m_PrefilterFoo: 1\nquality: 5\n',b'  m_PrefilterFoo: 0\nquality: 5\n')
        bs.validate_change(bs.GLOBAL,b'x\n      - rid: 1\n',b'x\n      - rid: 1\n      - rid: 2\n')
        self.assertTrue(bs.generated(bs.MAPS+'World-CrimsonGrove.bytes'))
        self.assertFalse(bs.generated(bs.MAPS+'Unexpected.cs'))


class Pipeline(unittest.TestCase):
    def setup_fixture(self,t):
        self.root=pathlib.Path(t)/'repo';self.root.mkdir();self.out=pathlib.Path(t)/'out';self.calls=[];self.releases=[];self.fail_upload=False;self.fail_create=False;self.fail_package=False
        self.head='a'*40;self.public='b'*40;self.version='9.8.7';self.channel='production'
    def api(self,route):
        if route.startswith('releases?'):return copy.deepcopy(self.releases)
        if route=='releases/71' or route=='releases/tags/v9.8.7':return copy.deepcopy(self.releases[0])
        raise AssertionError(route)
    def fake_run(self,args,**kwargs):
        args=[str(x) for x in args];self.calls.append(args)
        if args[:3]==['git','rev-parse','HEAD']:return self.head
        if args[:2]==['git','status'] or args[:2]==['git','ls-remote']:return ''
        if args[:2]==['go','run']:
            if '--sign-document' in args:
                pathlib.Path(args[-1]).write_text('signed fixture')
            return '{}'
        if args==['gh','workflow','run','installer-downloads.yml','--repo','afonasev/spacewars','--ref','main','-f','apply=true']:
            self.assertFalse(self.releases[0]['draft'])
            return ''
        if args[:3]==['gh','release','create']:
            notes=pathlib.Path(args[args.index('--notes-file')+1]).read_text()
            self.releases=[{'id':71,'tag_name':'v9.8.7','draft':True,'prerelease':'--prerelease' in args,'assets':[],'html_url':'https://example.invalid/release','target_commitish':self.public,'body':notes}]
            if self.fail_create:self.fail_create=False;raise RuntimeError('lost create response')
            return ''
        if args[:3]==['gh','release','edit']:
            self.releases[0]['body']=pathlib.Path(args[args.index('--notes-file')+1]).read_text();return ''
        if args[:3]==['gh','release','upload']:
            names=args[4:args.index('--repo')]
            for n in names:
                p=pathlib.Path(n);self.releases[0]['assets'].append({'id':len(self.releases[0]['assets'])+1,'name':p.name,'size':p.stat().st_size,'state':'uploaded','digest':'sha256:'+ps.hashfile(p)})
                if self.fail_upload:self.fail_upload=False;raise RuntimeError('interrupted upload')
            return ''
        if args[:4]==['gh','api','--method','DELETE']:
            aid=int(args[-1].split('/')[-1]);self.releases[0]['assets']=[a for a in self.releases[0]['assets'] if a['id']!=aid];return ''
        if args[:4]==['gh','api','--method','PATCH']:
            self.releases[0]['draft']=False;return ''
        raise AssertionError('unexpected external command '+repr(args))
    def subprocess_call(self,args,**kwargs):
        args=[str(x) for x in args];self.calls.append(args);platform=args[args.index('--platform')+1]
        if args[1].endswith('build.py'):
            folder=self.root/'unity/Builds/release'/('windows' if platform=='windows-x64' else 'macOS');folder.mkdir(parents=True,exist_ok=True);(folder/'native-build.json').write_text(platform);return 0
        self.assertTrue(args[1].endswith('package.py'))
        if self.fail_package and platform=='macos-universal':self.fail_package=False;raise RuntimeError('package compiler offline')
        feed=self.out/'public/native/v1';assets=feed/'github';installers=feed/'installers';assets.mkdir(parents=True,exist_ok=True);installers.mkdir(exist_ok=True)
        suffix='Windows-x64.exe' if platform=='windows-x64' else 'macOS-Universal.dmg';name='Spacewars-'+self.version+'-'+suffix
        (installers/name).write_bytes(b'fixture installer '+platform.encode())
        for ext in ['json','pack']:(assets/('Spacewars-'+self.version+'-'+platform+'.'+ext)).write_text(platform+' '+ext)
        item={'channel':self.channel,'platform':platform,'version':self.version,'commit':self.head,'dirty_build':False,'test_origin':None,'url':'https://github.com/afonasev/spacewars/releases/download/v'+self.version+'/'+name}
        if '--prepare-legacy-bridge' in args:
            legacy=self.out/'legacy-bridge/v1'/platform;legacy.mkdir(parents=True,exist_ok=True);(legacy/'latest.json').write_text('legacy')
            (self.out/('legacy-bridge-'+platform+'.json')).write_text('{}')
        (feed/'downloads.json').write_text(json.dumps({'channel':self.channel,'artifacts':[item]}));return 0
    def export(self,clone,head):
        self.calls.append(['export-source']);p=clone/'source-snapshot.json';p.write_text(json.dumps({'build_source_commit':head}));return self.public,p
    def invoke(self,resume=False,extra=()):
        argv=['publish.py','--version',self.version,'--sequence','18','--channel',self.channel,'--production-approved','--publish','--git-protocol','ssh','--output',str(self.out)]+(['--resume'] if resume else [])+list(extra)
        with patch.object(pub,'ROOT',self.root),patch.object(pub,'run',side_effect=self.fake_run),patch.object(pub,'api',side_effect=self.api),patch.object(pub.subprocess,'check_call',side_effect=self.subprocess_call),patch.object(pub,'clone_public',side_effect=lambda clone,url:clone.mkdir()),patch.object(pub,'export_source',side_effect=self.export),patch.object(pub.release_notes,'previous_release',return_value=None),patch.object(pub.release_notes,'generate',return_value=('Notes',{})),patch.object(ps,'space_guard'),patch.object(sys,'argv',argv):
            trust=self.root/'tools/native-release/trust.json';trust.parent.mkdir(parents=True,exist_ok=True);trust.write_text('{"public_key":"fixture"}')
            return pub.main()
    def test_interrupted_upload_resumes_without_build_package_source_or_payload_readback(self):
        with tempfile.TemporaryDirectory() as t:
            self.setup_fixture(t);self.fail_upload=True
            with self.assertRaisesRegex(RuntimeError,'interrupted upload'):self.invoke()
            self.assertTrue(self.releases[0]['draft'])
            self.assertFalse(any(c[:2]==['gh','workflow'] for c in self.calls))
            first=copy.deepcopy(self.calls);self.invoke(True)
            self.assertEqual(sum(c[:2]==['gh','workflow'] for c in self.calls),1)
            self.assertFalse(self.releases[0]['draft']);self.assertNotIn('spacewars-publisher',self.releases[0]['body'])
            for token in ['build.py','package.py']:
                self.assertEqual(sum(len(c)>1 and c[1].endswith(token) for c in self.calls),2)
            self.assertEqual(self.calls.count(['export-source']),1)
            self.assertEqual(sum('--sign-document' in c for c in self.calls),1)
            self.assertFalse(any('application/octet-stream' in ' '.join(c) or c[:3]==['gh','release','download'] for c in self.calls))
            self.assertFalse(any('--prepare-legacy-bridge' in c for c in self.calls))
            receipt=json.loads((self.out/'publication.json').read_text());self.assertIn('NotRun',receipt['client_download_readback'])
            before=len(self.calls);self.invoke(True)
            self.assertEqual(sum(c[:2]==['gh','workflow'] for c in self.calls),2)
            self.assertFalse(any(c[:3]==['gh','release','upload'] or c[:4]==['gh','api','--method','PATCH'] for c in self.calls[before:]))
    def test_test_channel_is_prerelease_and_never_latest(self):
        with tempfile.TemporaryDirectory() as t:
            self.setup_fixture(t);self.channel='test';self.invoke()
            self.assertTrue(self.releases[0]['prerelease'])
            self.assertFalse(any(c[:2]==['gh','workflow'] for c in self.calls))
            self.assertTrue(any('--prerelease' in c and '--latest=false' in c for c in self.calls))
            self.assertTrue(any('make_latest=false' in c for c in self.calls))
    def test_legacy_archive_is_built_only_with_explicit_flag(self):
        with tempfile.TemporaryDirectory() as t:
            self.setup_fixture(t);self.invoke(extra=['--prepare-legacy-bridge'])
            self.assertEqual(sum('--prepare-legacy-bridge' in c for c in self.calls),2)
            self.assertIn('Spacewars-9.8.7-Legacy-Bridge.zip',[a['name'] for a in self.releases[0]['assets']])
    def test_package_failure_reuses_completed_platform_and_both_bound_builds(self):
        with tempfile.TemporaryDirectory() as t:
            self.setup_fixture(t);self.fail_package=True
            with self.assertRaisesRegex(RuntimeError,'package compiler offline'):self.invoke()
            self.invoke(True)
            self.assertEqual(sum(len(c)>1 and c[1].endswith('build.py') for c in self.calls),2)
            self.assertEqual(sum(len(c)>1 and c[1].endswith('package.py') for c in self.calls),3)
            self.assertFalse(self.releases[0]['draft'])
    def test_lost_create_response_recovers_only_nonce_owned_draft(self):
        with tempfile.TemporaryDirectory() as t:
            self.setup_fixture(t);self.fail_create=True
            with self.assertRaisesRegex(RuntimeError,'lost create'):self.invoke()
            self.invoke(True);self.assertEqual(sum(c[:3]==['gh','release','create'] for c in self.calls),1)
    def test_changed_checkpoint_or_remote_source_fails_without_publication(self):
        for mode in ['bytes','source','nonce']:
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as t:
                self.setup_fixture(t);self.fail_upload=True
                with self.assertRaises(RuntimeError):self.invoke()
                if mode=='bytes':next((self.out/'public/native/v1/github').glob('*.pack')).write_text('tampered')
                elif mode=='source':self.releases[0]['target_commitish']='other'
                else:
                    state=json.loads((self.out/'release-state.json').read_text());state.pop('release_id');(self.out/'release-state.json').write_text(json.dumps(state));self.releases[0]['body']='not our draft'
                with self.assertRaises(ValueError):self.invoke(True)
                self.assertTrue(self.releases[0]['draft'])
    def test_zero_byte_owned_starter_is_reuploaded(self):
        with tempfile.TemporaryDirectory() as t:
            self.setup_fixture(t);self.fail_upload=True
            with self.assertRaises(RuntimeError):self.invoke()
            self.releases[0]['assets'][0].update(state='starter',size=0);self.invoke(True)
            self.assertTrue(any(c[:4]==['gh','api','--method','DELETE'] for c in self.calls))

class BloblessExport(unittest.TestCase):
    def test_no_checkout_export_keeps_history_and_does_not_fetch_old_payload(self):
        with tempfile.TemporaryDirectory() as t:
            base=pathlib.Path(t);old=base/'old';old.mkdir()
            def git(*args,cwd=old):return subprocess.check_output(['git',*args],cwd=cwd,text=True).strip()
            git('init','-q','-b','main');git('config','user.name','Fixture');git('config','user.email','fixture@example.invalid')
            name='unity/Assets/old.bin';(old/name).parent.mkdir(parents=True);(old/name).write_bytes(b'historical payload'*10000)
            (old/'README.md').write_text('preserve unrelated public file')
            obsolete='unity/Assets/obsolete.cs';(old/obsolete).write_text('retired')
            (old/'source-snapshot.json').write_text(json.dumps({'files':{name:'old',obsolete:'old'}}));git('add','.');git('commit','-qm','previous')
            parent=git('rev-parse','HEAD');blob=git('rev-parse','HEAD:'+name)
            bare=base/'remote.git';git('clone','--bare',str(old),str(bare));git('config','uploadpack.allowFilter','true',cwd=bare)
            source=base/'source';new='unity/Assets/Новый.cs';(source/new).parent.mkdir(parents=True);(source/new).write_text('current code');(source/name).write_bytes(b'current asset'*10000)
            clone=base/'clone';real=pub.run
            def local(args,**kwargs):
                if args[:2]==['git','clone']:args=list(args[:-2])+[bare.as_uri(),args[-1]]
                return real(args,**kwargs)
            with patch.object(pub,'run',side_effect=local),patch.object(pub,'ROOT',source),patch.object(pub,'source_names',return_value=[new,name]):
                pub.clone_public(clone,bare.as_uri());git('config','user.name','Fixture',cwd=clone);git('config','user.email','fixture@example.invalid',cwd=clone)
                commit,_=pub.export_source(clone,'c'*40)
            git('merge-base','--is-ancestor',parent,commit,cwd=clone)
            self.assertEqual(git('show','HEAD:'+new,cwd=clone),'current code')
            self.assertIn('README.md',git('ls-files',cwd=clone));self.assertNotIn(obsolete,git('ls-files',cwd=clone))
            objects=git('cat-file','--batch-all-objects','--batch-check=%(objectname)',cwd=clone).splitlines()
            self.assertNotIn(blob,objects)

if __name__=='__main__':unittest.main()
