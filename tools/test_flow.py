import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT=Path(os.environ.get('FLOW_SCRIPT',Path(__file__).with_name('flow.py')))
spec=importlib.util.spec_from_file_location('flow',SCRIPT); m=importlib.util.module_from_spec(spec); spec.loader.exec_module(m)

class Lifecycle(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.root=Path(self.tmp.name)
        (self.root/'openspec/changes').mkdir(parents=True); self.f=m.Flow(self.root)
    def tearDown(self):self.tmp.cleanup()
    def new(self,cid='example',kind='report',route='quick',parent=None):
        p=self.root/'openspec/changes'/cid;p.mkdir();(p/'.openspec.yaml').write_text('schema: flow-quick\n')
        return self.f.init(cid,route,kind,parent)
    def ready(self,cid='example'):
        self.f.evidence(cid,'scope',{'approved':True});self.f.transition(cid,'ready')
    def report_delivered(self,cid='example'):
        self.ready(cid);self.f.claim(cid,'worker');self.f.transition(cid,'implementing','worker')
        self.f.evidence(cid,'verification',{'result':'pass'});self.f.transition(cid,'verified','worker')
        self.f.evidence(cid,'publication',{'path':'durable/report.md'});self.f.transition(cid,'published','worker')
        self.f.transition(cid,'finalizing','worker')
    def finalize(self,cid='example'):
        for k in ['spec_sync','cleanup']:self.f.evidence(cid,k,{'result':'verified'})
        (self.f.get(cid)[0].parent/'acceptance.md').write_text('Review the published report.')
        self.f.evidence(cid,'acceptance_guide','acceptance.md');self.f.transition(cid,'awaiting-acceptance','worker')
    def test_report_acceptance_and_archive(self):
        self.new();self.report_delivered();self.finalize()
        with self.assertRaises(ValueError):self.f.transition('example','accepted')
        self.f.transition('example','accepted',decision='Все устраивает',source='user message 42')
        self.f.evidence('example','archive',{'commit':'recorded'})
        with self.assertRaises(ValueError):self.f.transition('example','archived')
        dest=self.root/'openspec/changes/archive/2026-09-20-example';dest.parent.mkdir();self.f.get('example')[0].parent.rename(dest)
        self.f.transition('example','archived')
    def accept_report(self,cid='example'):
        return self.f.accept(cid,'Reviewed and accepted','user message 42','durable/report.md','Published report')
    def finalization_evidence(self,cid='example'):
        for k in ['spec_sync','cleanup']:self.f.evidence(cid,k,{'result':'verified'})
        (self.f.get(cid)[0].parent/'acceptance.md').write_text('Review the published report.')
        self.f.evidence(cid,'acceptance_guide','acceptance.md')
    def test_early_acceptance_survives_restart_and_cleanup(self):
        self.new();self.report_delivered()
        self.f.question('example','Remove attached worktree later?',['awaiting-acceptance'])
        d=self.accept_report()
        self.assertEqual((d['stage'],d['owner']),('finalizing','worker'))
        with self.assertRaises(ValueError):self.f.finalize('example','worker')
        # A new session can read the decision without taking over the technical worker.
        self.f=m.Flow(self.root)
        self.assertEqual(self.f.inbox()[0]['acceptance_status'],'accepted')
        self.assertFalse(self.f.inbox()[0]['acceptance'])
        self.f.answer('example','Q1','Cleanup completed','technical readback','Owned worktree removed')
        self.finalization_evidence()
        self.f.finalize('example','worker')
        self.assertEqual(self.f.get('example')[1]['stage'],'accepted')
        self.assertIsNone(self.f.get('example')[1]['owner'])
    def test_late_acceptance_and_deferred_inbox(self):
        self.new();self.report_delivered()
        self.assertTrue(self.f.inbox()[0]['acceptance'])
        self.f.defer_acceptance('example','user message','Will review in another session')
        self.assertFalse(self.f.inbox()[0]['acceptance'])
        self.assertEqual(self.f.inbox()[0]['acceptance_status'],'deferred')
        self.assertTrue(self.f.inbox()[0]['finalization_debt'])
        self.finalization_evidence();self.f.finalize('example','worker')
        self.assertEqual(self.f.get('example')[1]['stage'],'awaiting-acceptance')
        self.accept_report();self.f.finalize('example')
        self.assertEqual(self.f.get('example')[1]['stage'],'accepted')
    def test_reference_identity_and_metadata(self):
        self.new();self.report_delivered()
        with self.assertRaises(ValueError):self.f.accept('example','Yes','user','other-report','Report')
        self.f.evidence('example','publication',{'identity':'report-v1','revision':'v1','path':'durable/report.md'})
        self.f.accept('example','Yes','user','report-v1','Report')
        self.f.evidence('example','publication',{'identity':'report-v1','revision':'v1','path':'moved/report.md','note':'cleanup metadata'})
        self.assertEqual(self.f.acceptance_status(self.f.get('example')[1]),'accepted')
        self.f.evidence('example','publication',{'identity':'report-v2','revision':'v2','path':'moved/report.md'})
        self.assertEqual(self.f.acceptance_status(self.f.get('example')[1]),'stale')
        self.assertTrue(self.f.inbox()[0]['acceptance'])
        self.finalization_evidence();self.f.finalize('example','worker')
        self.assertEqual(self.f.get('example')[1]['stage'],'awaiting-acceptance')
    def test_no_acceptance_or_archive_gate_bypass(self):
        self.new()
        with self.assertRaises(ValueError):self.accept_report()
        self.report_delivered();self.accept_report();self.finalization_evidence()
        self.f.evidence('example','cleanup_pending',{'completed':False})
        with self.assertRaises(ValueError):self.f.finalize('example','worker')
        self.f.evidence('example','cleanup_pending',{'completed':True});self.f.finalize('example','worker')
        self.f.evidence('example','archive',{'commit':'recorded'})
        self.f.evidence('example','publication',{'identity':'different-result'})
        with self.assertRaises(ValueError):self.f.transition('example','archived')
    def test_rework_preserves_decision_history(self):
        self.new();self.report_delivered();self.accept_report()
        self.f.transition('example','rework-required','worker',decision='Fix same-scope defect',source='user feedback')
        d=self.f.get('example')[1]
        self.assertEqual(self.f.acceptance_status(d),'pending')
        old=next(h for h in d['history'] if h['event']=='previous-delivery')
        self.assertEqual(old['evidence']['acceptance']['ref'],'durable/report.md')
    def test_child_acceptance_and_finalization_are_separate(self):
        self.new('campaign','initiative','initiative');self.new('mission',parent='campaign')
        self.report_delivered('campaign');self.report_delivered('mission')
        with self.assertRaises(ValueError):self.accept_report('campaign')
        self.accept_report('mission');self.accept_report('campaign')
        self.finalization_evidence('campaign')
        with self.assertRaises(ValueError):self.f.finalize('campaign','worker')
        self.finalization_evidence('mission');self.f.finalize('mission','worker')
        self.f.finalize('campaign','worker')
        self.assertEqual(self.f.get('campaign')[1]['stage'],'accepted')
    def test_accepted_dependency_does_not_wait_for_cleanup(self):
        self.new('first');self.new('second');self.f.dependency('second','first','accepted')
        self.report_delivered('first');self.f.evidence('second','scope','agreed')
        with self.assertRaises(ValueError):self.f.transition('second','ready')
        self.accept_report('first');self.f.transition('second','ready')
        self.assertEqual(self.f.get('first')[1]['stage'],'finalizing')
    def test_added_identity_metadata_and_deferred_stale_review(self):
        self.new();self.report_delivered()
        self.f.evidence('example','publication',{'identity':'report-v1','path':'durable/report.md'})
        self.f.accept('example','Yes','user','report-v1','Report')
        self.f.evidence('example','publication',{'identity':'report-v1','sha256':'original-content-hash','path':'moved/report.md'})
        self.assertEqual(self.f.acceptance_status(self.f.get('example')[1]),'accepted')
        self.f.evidence('example','publication',{'identity':'report-v2','path':'moved/report.md'})
        self.f.defer_acceptance('example','user message','Review the revised report later')
        self.assertEqual(self.f.acceptance_status(self.f.get('example')[1]),'deferred')
        self.assertFalse(self.f.inbox()[0]['acceptance'])

    def test_legacy_publication_commits_are_reviewable(self):
        self.new();self.report_delivered()
        self.f.evidence('example','publication',{'main_commit':'config-revision','planning_import_commit':'spec-import-revision','application_deployed':False})
        self.f.accept('example','Accepted configuration','user message','config-revision','Process report')
        self.assertEqual(self.f.acceptance_status(self.f.get('example')[1]),'accepted')
        self.finalization_evidence();self.f.finalize('example','worker')
        self.assertEqual(self.f.get('example')[1]['stage'],'accepted')
    def test_legacy_release_version_and_commit_bindings(self):
        self.new(kind='software');self.ready();self.f.claim('example','worker')
        self.f.transition('example','implementing','worker');self.f.evidence('example','commit','implementation-revision')
        self.f.evidence('example','verification','verified');self.f.transition('example','verified','worker')
        for key in ['release_identity','version','code_commit','source_commit','release_commit','commit']:
            with self.subTest(key=key):
                self.f.evidence('example','release',{key:'release-v1','url':'https://example.invalid/'})
                self.f.accept('example','Reviewed release','user message','release-v1','Player result')
                self.f.evidence('example','release',{key:'release-v1','url':'https://moved.example.invalid/','note':'metadata'})
                self.assertEqual(self.f.acceptance_status(self.f.get('example')[1]),'accepted')
                self.f.evidence('example','release',{key:'release-v2','url':'https://moved.example.invalid/'})
                self.assertEqual(self.f.acceptance_status(self.f.get('example')[1]),'stale')

    def test_legacy_acceptance_stays_valid(self):
        self.new();self.report_delivered();self.finalize()
        p,d=self.f.get('example');d['stage']='accepted';d['owner']=None
        d['evidence']['acceptance']={'decision':'Yes','source':'original user message','at':'2026-09-20'}
        self.f.save(p,d,'legacy-fixture')
        self.assertEqual(self.f.inbox()[0]['acceptance_status'],'accepted')
    def test_cli_human_decision_does_not_require_worker_claim(self):
        self.new();self.report_delivered()
        result=subprocess.run(['python3',str(SCRIPT),'--root',str(self.root),'accept','example',
            '--decision','Yes','--source','another session user message','--ref','durable/report.md','--scope','Report'],capture_output=True,text=True)
        self.assertEqual(result.returncode,0,result.stderr)
        self.assertEqual(json.loads(result.stdout)['owner'],'worker')

    def test_unresolved_question_and_inline_answer(self):
        self.new();self.f.question('example','What behavior?',['ready']);self.f.evidence('example','scope','agreed')
        with self.assertRaises(ValueError):self.f.transition('example','ready')
        self.f.answer('example','Q1','A','user message')
        with self.assertRaises(ValueError):self.f.transition('example','ready')
        self.f.answer('example','Q1','A','user message','Spec updated');self.f.transition('example','ready')
        self.assertFalse(self.f.inbox()[0]['questions'])
    def test_no_double_claim_concurrent_processes(self):
        self.new();self.ready()
        cmds=[[ 'python3',str(SCRIPT),'--root',str(self.root),'claim','example','--owner',o] for o in ['one','two']]
        ps=[subprocess.Popen(c,stdout=subprocess.PIPE,stderr=subprocess.PIPE) for c in cmds]
        results=[(p.communicate(),p.returncode)[1] for p in ps]
        self.assertEqual(sorted(results),[0,2])
    def test_delivery_debt_and_rework_reset(self):
        self.new();self.report_delivered()
        self.assertTrue(self.f.inbox()[0]['finalization_debt'])
        with self.assertRaises(ValueError):self.f.transition('example','awaiting-acceptance','worker')
        self.finalize();self.f.transition('example','rework-required',decision='Fix totals',source='inline feedback')
        self.assertEqual(set(self.f.get('example')[1]['evidence']),{'scope'})
        self.f.claim('example','new-worker');self.f.transition('example','implementing','new-worker')
        with self.assertRaises(ValueError):self.f.transition('example','verified','new-worker')
    def test_dependencies_and_cycles(self):
        self.new('first');self.new('second');self.f.dependency('second','first','delivered')
        self.f.evidence('second','scope','agreed')
        with self.assertRaises(ValueError):self.f.transition('second','ready')
        with self.assertRaises(ValueError):self.f.dependency('first','second','accepted')
        self.report_delivered('first');self.f.transition('second','ready')
    def test_initiative_child_gate(self):
        self.new('campaign','initiative','initiative');self.new('mission',parent='campaign')
        with self.assertRaises(ValueError):self.f.dependency('mission','campaign','accepted')
        self.report_delivered('campaign')
        for k in ['spec_sync','cleanup']:self.f.evidence('campaign',k,'checked')
        (self.f.get('campaign')[0].parent/'acceptance.md').write_text('Whole campaign')
        self.f.evidence('campaign','acceptance_guide','acceptance.md')
        with self.assertRaises(ValueError):self.f.transition('campaign','awaiting-acceptance','worker')
    def test_merge_real_ancestry(self):
        repo=self.root/'code';repo.mkdir()
        def git(*args):return subprocess.check_output(['git','-C',str(repo),*args],stderr=subprocess.DEVNULL,text=True).strip()
        git('init','-b','main');git('config','user.email','test@example.invalid');git('config','user.name','Test')
        (repo/'a').write_text('a');git('add','.');git('commit','-m','base')
        git('checkout','-b','feature');(repo/'a').write_text('b');git('commit','-am','feature');sha=git('rev-parse','HEAD')
        self.new(kind='software');self.ready();self.f.claim('example','worker');self.f.transition('example','implementing','worker')
        self.f.evidence('example','verification','pass');self.f.evidence('example','commit',sha);self.f.transition('example','verified','worker')
        self.f.evidence('example','merge',{'repo':str(repo),'commit':sha,'main_ref':'main'})
        with self.assertRaises(subprocess.CalledProcessError):self.f.transition('example','merged','worker')
        git('checkout','main');git('merge','--ff-only','feature');self.f.transition('example','merged','worker')
    def test_source_only_merge_finalizes_without_faking_deploy_or_acceptance(self):
        self.test_merge_real_ancestry()
        invalid=[None,{'deployment_deferred':True},
                 {'deployment_deferred':False,'source':'user policy','reason':'source-only'},
                 {'deployment_deferred':True,'source':'','reason':'source-only'}]
        for evidence in invalid:
            with self.subTest(evidence=evidence):
                if evidence is not None:self.f.evidence('example','source_only_delivery',evidence)
                with self.assertRaises(ValueError):self.f.transition('example','finalizing','worker')
                self.assertEqual(self.f.get('example')[1]['stage'],'merged')
        self.f.evidence('example','source_only_delivery',{'deployment_deferred':True,
            'source':'explicit source-only user policy','reason':'Build and deploy require separate commands'})
        self.f.transition('example','finalizing','worker')
        with self.assertRaises(ValueError):self.f.finalize('example','worker')
        self.finalization_evidence();self.f.finalize('example','worker')
        d=self.f.get('example')[1]
        self.assertEqual((d['stage'],d['owner']),('awaiting-acceptance',None))
        self.assertNotIn('release',d['evidence']);self.assertNotIn('smoke',d['evidence'])
        self.assertEqual(self.f.acceptance_status(d),'pending')
        with self.assertRaises(ValueError):self.f.transition('example','accepted')
        self.f.accept('example','Reviewed source','user review',d['evidence']['commit'],'Source slice')
        self.f.finalize('example');self.assertEqual(self.f.get('example')[1]['stage'],'accepted')
    def test_lease_ownership(self):
        self.f.lease('acquire','integration','a')
        with self.assertRaises(ValueError):self.f.lease('acquire','integration','b')
        with self.assertRaises(ValueError):self.f.lease('release','integration','b')
        self.f.lease('release','integration','a');self.f.lease('acquire','integration','b')
    def test_pause_is_sticky(self):
        self.new();self.ready();p,d=self.f.get('example');d['paused']=True;self.f.save(p,d,'paused')
        self.assertFalse(self.f.inbox()[0]['ready'])
        with self.assertRaises(ValueError):self.f.claim('example','worker')

if __name__=='__main__':unittest.main()
