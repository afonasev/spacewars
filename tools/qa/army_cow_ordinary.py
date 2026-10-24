#!/usr/bin/env python3
"""One authorized ordinary Army600 witness on an explicitly pinned runtime.

No retry, build, suite expansion, authority mutation or body capture added.
Uses canonical qualified observer; explicit current-main motion mode adds QA-only readback. The inverse port audit preserves all original gameplay assertions and bounds.
"""
import argparse, difflib, hashlib, json, pathlib, subprocess, time, zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
RAW = pathlib.Path('/Users/eaafonasev/Projects/spacewars-planning/evidence/native-strategic-ai/frontier-materialization-20261018/raw.zip')
sha = lambda b: hashlib.sha256(b).hexdigest()
def git(*args): return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()
def save(p, value): p.write_text(json.dumps(value, indent=2))
def assemblies():
    p=ROOT/'unity/Library/ScriptAssemblies'
    return [dict(path=str(f.relative_to(ROOT)),sha256=sha(f.read_bytes()),bytes=f.stat().st_size) for f in sorted(p.glob('Spacewars.*')) if f.is_file()]

def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--out',required=True,type=pathlib.Path)
    ap.add_argument('--pin',required=True);ap.add_argument('--runtime',required=True)
    ap.add_argument('--reason',required=True);ap.add_argument('--accrued-credits',action='store_true');ap.add_argument('--motion-readback',action='store_true');ap.add_argument('--native-queue-metadata',action='store_true',help='read the canonical public queue action/objective/viability projection directly instead of legacy DTO substitutions');a=ap.parse_args()
    assert not a.native_queue_metadata or a.motion_readback, '--native-queue-metadata requires --motion-readback'
    assert git('rev-parse','HEAD')==a.pin, 'exact QA source pin'
    assert git('status','--porcelain')=='', 'clean committed source required'
    assert not git('diff',a.runtime,a.pin,'--','unity/Assets/Spacewars/Runtime','unity/Assets/Spacewars/Simulation'), 'exact runtime source'
    out=a.out.resolve();out.relative_to(ROOT/'.local');out.mkdir(parents=True,exist_ok=False)
    assert sha(RAW.read_bytes())=='432b575c3bb20bc19bdb7a485f9383d8d4c59c5a5bf66679f26274edea8ae9a1'
    with zipfile.ZipFile(RAW) as z:
        assert z.testzip() is None
        original=z.read('army600/observer.cs');meta=z.read('army600/observer.cs.meta')
    assert sha(original)=='362f180867f8f638e5cdb5caaec3f49fafe801aa69cd2b8814af6ca3fc5fd3e7'
    before='../../.local/frontier-materialization-01a14d59/army600/game'
    after='../../'+str(out.relative_to(ROOT))+'/game'
    assert original.decode().count(before)==1
    observer=original.decode().replace(before,after).encode()
    assert observer.decode().replace(after,before).encode()==original
    if a.accrued_credits:
        helper="""    static readonly MethodInfo CreditBankPin=typeof(NavigationSession).GetMethod("PinCreditBank",Private);static object CreditScalars(NavigationSession session){var owner=(NavOwnedStorage)CreditBankPin.Invoke(session,null);if(owner==null)return null;var b=(IReadOnlyList<long>)owner;try{return new{generation=b[1],lastAccountedTick=b[2],cap=b[3],queued=b[5],earned=b[6],promoted=b[7],discarded=b[8],saturationDrops=b[9],activeRevision=b[10]};}finally{Release.Invoke(owner,null);}}
"""
        needle='    static object CaptureRootUnderGate('
        assert observer.decode().count(needle)==1
        text=observer.decode().replace(needle,helper+needle)
        needle='return new{available=true,root.Progress,root.Epoch,frontier='
        assert text.count(needle)==1
        text=text.replace(needle,'return new{available=true,credits=CreditScalars(session),root.Progress,root.Epoch,frontier=')
        observer=text.encode()
    dto_edits=[('static object Queue(PlayableQueuedOrderSnapshot q)=>', 'static object Queue(PlayableQueuedOrderSnapshot q,PlayableEntitySnapshot unit)=>'), ('q.AiActionId,kind=q.Kind.ToString(),q.GroupId,q.CommandSequence,q.ActivationRevision,q.PublicObjectiveId,', 'AiActionId=ActionFor(unit,q),kind=q.Kind.ToString(),q.GroupId,q.CommandSequence,q.ActivationRevision,PublicObjectiveId=(int?)null,publicObjectiveStatus="NotExposedInCurrentPublicQueue",'), ('pending=Queue(u.Queue?.Pending),active=Queue(u.Queue?.Active),viable=u.Queue?.PendingAiRouteViable', 'pending=Queue(u.Queue?.Pending,u),active=Queue(u.Queue?.Active,u),viable=(bool?)null,viabilityStatus="NotExposedInCurrentPublicQueue; never an active execution check"'), ('(u.Queue.Pending??u.Queue.Active).AiActionId>0', 'ActionFor(u,u.Queue.Pending??u.Queue.Active)>0'), ('firstAction=firstOrder.AiActionId;', 'firstAction=ActionFor(candidate,firstOrder).Value;'), ('u.Queue.Active.AiActionId!=firstAction', 'ActionFor(u,u.Queue.Active)!=firstAction'), ('u.Queue.Pending.AiActionId!=firstAction', 'ActionFor(u,u.Queue.Pending)!=firstAction')]
    if a.motion_readback:
        helper=(ROOT/'tools/qa/army_motion_observation.cs').read_text()
        text=observer.decode()
        if not a.native_queue_metadata:
            for old,new in dto_edits:
                assert text.count(old)==1,old
                text=text.replace(old,new)
        needle='    static object CaptureRootUnderGate('
        assert text.count(needle)==1;text=text.replace(needle,'    // BEGIN QA MOTION READBACK\n'+helper+'    // END QA MOTION READBACK\n'+needle)
        optional='owner.GetType().GetField(n,Private|BindingFlags.Public).GetValue(owner)'
        assert text.count(optional)==1;text=text.replace(optional,'ReadPolicyMember(owner,n)')
        needle='return new{available=true,credits=' if a.accrued_credits else 'return new{available=true,root.Progress'
        assert text.count(needle)==1;text=text.replace(needle,'return new{available=true,motion=MotionScalars(session,core),credits=' if a.accrued_credits else 'return new{available=true,motion=MotionScalars(session,core),root.Progress')
        needle='                var units=view.Entities.Where(u=>core.Contains(u.Id)).ToArray();'
        assert text.count(needle)==1;text=text.replace(needle,'                EnableMotionObserver(runtime);\n'+needle)
        needle='            DecisionHook.SetValue(null,savedHook);DecisionFilter.SetValue(null,savedFilter);'
        assert text.count(needle)==1;text=text.replace(needle,'            DisableMotionObserver();\n'+needle)
        observer=text.encode()
    # Invert every explicitly allowed edit and require the complete original
    # observer back, including every assertion, deadline, seed and row bound.
    audit=observer.decode()
    if a.motion_readback:
        audit=audit.replace('    // BEGIN QA MOTION READBACK\n'+helper+'    // END QA MOTION READBACK\n','').replace('ReadPolicyMember(owner,n)','owner.GetType().GetField(n,Private|BindingFlags.Public).GetValue(owner)').replace('motion=MotionScalars(session,core),','').replace('                EnableMotionObserver(runtime);\n','').replace('            DisableMotionObserver();\n','')
    if a.motion_readback and not a.native_queue_metadata:
        for old,new in reversed(dto_edits):
            assert audit.count(new)==1,new
            audit=audit.replace(new,old)
    if a.accrued_credits:
        credit_helper="""    static readonly MethodInfo CreditBankPin=typeof(NavigationSession).GetMethod("PinCreditBank",Private);static object CreditScalars(NavigationSession session){var owner=(NavOwnedStorage)CreditBankPin.Invoke(session,null);if(owner==null)return null;var b=(IReadOnlyList<long>)owner;try{return new{generation=b[1],lastAccountedTick=b[2],cap=b[3],queued=b[5],earned=b[6],promoted=b[7],discarded=b[8],saturationDrops=b[9],activeRevision=b[10]};}finally{Release.Invoke(owner,null);}}
"""
        audit=audit.replace(credit_helper,'').replace('credits=CreditScalars(session),','')
    assert audit.replace(after,before).encode()==original,'observer changes exceeded allowed QA edits'
    save(out/'observer-port-audit.json',dict(originalSHA256=sha(original),portedSHA256=sha(observer),inverseEqualsOriginal=True,originalAssertionsSeedsDurationsWatchdogsPreserved=True,allowed=['output directory','bounded credit scalar readback','QA-only installed route/cursor/motion/quantum/refusal readback from actual core','explicit unavailable Page0-policy fields in current TerminalCost policy',('canonical owner-only queue AiActionId/PublicObjectiveId/PendingAiRouteViable projection' if a.native_queue_metadata else 'current public queue action derived from matching published CommandSequence stamp, cached by issuance/group/sequence; unavailable objective/viability explicit')],nativeQueueMetadata=a.native_queue_metadata,runtimeMutation=False))
    (out/'observer-port.diff').write_text(''.join(difflib.unified_diff(original.decode().splitlines(True),observer.decode().splitlines(True),fromfile='canonical-observer362f1808',tofile='currentmain-motion-observer')))
    target=ROOT/'unity/Assets/Spacewars/Tests/PlayMode/NativeFirstArmyRoutingPrefixTests.cs'
    targetmeta=target.with_suffix('.cs.meta');assert not target.exists() and not targetmeta.exists()
    paths=git('ls-tree','-r','--name-only',a.pin,'--','unity/Assets','unity/Packages','unity/ProjectSettings').splitlines()
    source=[]
    for name in paths:
        p=ROOT/name
        if p.is_file():
            b=p.read_bytes();expected=subprocess.check_output(['git','show',a.pin+':'+name],cwd=ROOT)
            assert b==expected, 'pinned input changed: '+name
            source.append(dict(path=name,sha256=sha(b),bytes=len(b)))
    command=['tools/unity.sh','shared','-batchmode','-noaudio','-muted','-runTests','-testPlatform','PlayMode','-testFilter','NativeFirstArmyRoutingPrefixTests.FoundryFirstArmyThroughOrdinaryLobby','-testResults',str(out/'playmode.xml'),'-logFile',str(out/'playmode.log')]
    save(out/'source-before.json',dict(qa=a.pin,runtime=a.runtime,branch=git('branch','--show-current'),reason=a.reason,source=source,observerOriginalSHA256=sha(original),observerSHA256=sha(observer),observerOnlyOutputPath=not(a.accrued_credits or a.motion_readback),accruedScalarCounts=a.accrued_credits,motionReadback=a.motion_readback,nativeQueueMetadata=a.native_queue_metadata,assembliesBefore=assemblies()))
    save(out/'command.json',command);(out/'observer.cs').write_bytes(observer);(out/'observer.cs.meta').write_bytes(meta);(out/'runner.py').write_bytes(pathlib.Path(__file__).read_bytes())
    queue_mode='canonical owner-only AiActionId/PublicObjectiveId/PendingAiRouteViable projection' if a.native_queue_metadata else 'legacy queue DTO substitutions with unavailable objective/viability explicit'
    (out/'qa-plan.md').write_text(f'Pinned QA{a.pin}/runtime{a.runtime}. Reason/baseline/dependencies: {a.reason}. ONE same Army600 real threaded ordinary PlayMode witness, seed-515260688,4x,two Fighter/Veteran participants,600/startup20/deadline160/Timeout180000/assertions unchanged. Source/fixture/profile/provider dependencies pinned before launch. Qualified observer362f1808; only explicit QA readback/output and {queue_mode}; observer-port-audit.json inverts all edits to exact canonical original. Missing old Page0 policy metrics remain explicitly unavailable. No solver or authority mutation. tools/unity.sh shared directly executes Unity -runTests without Player build or composite QA route; -noaudio -muted from process start. No full/original6000/performance/C9pressure/build/install/release/deploy/human acceptance. No serial game repeat. Cold BODY NotRun: unchanged observer does not emit it.\n')
    before_dir=out/'compiled-assemblies-before';before_dir.mkdir()
    before_bindings={r['path']:r for r in assemblies()}
    for name,row in before_bindings.items():
        p=ROOT/name;data=p.read_bytes();assert sha(data)==row['sha256'];(before_dir/p.name).write_bytes(data)
    save(out/'compiled-before-bindings.json',list(before_bindings.values()))
    target.write_bytes(observer);targetmeta.write_bytes(meta)
    started=time.time();result=None
    try:
        with (out/'runner.log').open('w') as log: result=subprocess.run(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT)
    finally:
        # Remove only exact source bytes installed by this invocation, after wrapper/Unity exit.
        for p,expected in [(target,observer),(targetmeta,meta)]:
            assert p.read_bytes()==expected,'foreign change in temporary observer: '+str(p)
            p.unlink()
        for row in source: assert sha((ROOT/row['path']).read_bytes())==row['sha256'], 'source changed during run: '+row['path']
        (out/'compiled-assemblies').mkdir(exist_ok=True)
        for f in (ROOT/'unity/Library/ScriptAssemblies').glob('Spacewars.*'):
            if f.is_file():(out/'compiled-assemblies'/f.name).write_bytes(f.read_bytes())
        save(out/'source-after.json',dict(qa=a.pin,runtime=a.runtime,branch=git('branch','--show-current'),source=source,assembliesAfter=assemblies(),observerRemoved=True,wallIncludingAdmissionStartupCompileExecutionShutdown=time.time()-started,exitCode=None if result is None else result.returncode))
    print('ONE witness exit',result.returncode,'output',out)
    raise SystemExit(result.returncode)
if __name__=='__main__':main()
