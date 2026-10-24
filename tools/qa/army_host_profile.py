#!/usr/bin/env python3
"""Run one pinned ordinary threaded Army600 host profile; never retries.

This is QA-only instrumentation. It installs the archived ordinary observer
and a bounded C# clock probe temporarily, then restores both source files.
"""
from __future__ import annotations
import argparse
import difflib
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import zipfile

ROOT = Path(__file__).resolve().parents[2]
BASE_QA = "7c01ffcd7d8fa8dcfd72a20cea91123e5230908c"
BASE_RUNTIME = "13cf1397f81b24f6369faab8f0fce91ab569075a"
BASE_CODE = "fd1e6c5fb0b81eefd56986f3b45820baf8c74f9a"
RAW = Path("/Users/eaafonasev/Projects/spacewars-planning/evidence/native-strategic-ai/frontier-materialization-20261018/raw.zip")
RAW_SHA = "432b575c3bb20bc19bdb7a485f9383d8d4c59c5a5bf66679f26274edea8ae9a1"
OBSERVER_SHA = "362f180867f8f638e5cdb5caaec3f49fafe801aa69cd2b8814af6ca3fc5fd3e7"
TEST = ROOT / "unity/Assets/Spacewars/Tests/PlayMode/NativeFirstArmyRoutingPrefixTests.cs"
PROBE = ROOT / "unity/Assets/Spacewars/Tests/PlayMode/OrdinaryHostProfileProbe.cs"
PROBE_META = PROBE.with_suffix(".cs.meta")
TEST_META = TEST.with_suffix(".cs.meta")

def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()

def git(*args: str) -> str:
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()

def write_json(path: Path, value) -> None:
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n")

def tracked_tree(pin: str, roots: tuple[str, ...]) -> list[dict]:
    names = git("ls-tree", "-r", "--name-only", pin, "--", *roots).splitlines()
    rows=[]
    for name in names:
        current=(ROOT/name).read_bytes()
        expected=subprocess.check_output(["git","show",f"{pin}:{name}"],cwd=ROOT)
        if current!=expected:
            raise RuntimeError("checkout source changed from immutable candidate: "+name)
        rows.append({"path":name,"sha256":sha(current),"bytes":len(current),"gitBlob":git("hash-object",name)})
    return rows

def source_guards(source_pin: str, qa_pin: str, out: Path | None) -> dict:
    if source_pin!=BASE_RUNTIME: raise RuntimeError("only the reviewed 13cf source pin is accepted")
    if qa_pin!=BASE_QA: raise RuntimeError("only the reviewed legacy QA pin is accepted")
    head=git("rev-parse","HEAD")
    if subprocess.run(["git","merge-base","--is-ancestor",BASE_CODE,head],cwd=ROOT).returncode!=0:
        raise RuntimeError("candidate must descend from reviewed main fd1e6c5")
    changed_checkout=git("diff","--name-only",BASE_CODE,head).splitlines()
    if sorted(changed_checkout)!=["tools/qa/army_host_profile.cs","tools/qa/army_host_profile.py"]:
        raise RuntimeError("candidate commit must contain only the two owned QA profile files")
    if git("status","--porcelain"):
        raise RuntimeError("checkout must be clean before temporary test installation")
    runtime=[r for r in tracked_tree(source_pin,("unity/Assets/Spacewars/Runtime","unity/Assets/Spacewars/Simulation")) if r["path"].endswith(".cs")]
    if len(runtime)!=316: raise RuntimeError(f"expected 316 runtime sources, got {len(runtime)}")
    unity=tracked_tree(BASE_CODE,("unity/Assets","unity/Packages","unity/ProjectSettings"))
    if TEST.exists() or TEST_META.exists() or PROBE.exists() or PROBE_META.exists():
        raise RuntimeError("temporary QA source destination already exists")
    # Bind the full runtime candidate delta to the original verified base.
    base_runtime=[]
    for row in runtime:
        old=subprocess.check_output(["git","show",f"83fff80bbdb50312238fda558bdef01f3c631e64:{row['path']}"],cwd=ROOT)
        base_runtime.append({"path":row["path"],"sha256":sha(old),"candidateSha256":row["sha256"],"gitBlob":row["gitBlob"]})
    changed=[r["path"] for r in base_runtime if r["sha256"]!=r["candidateSha256"]]
    expected=["unity/Assets/Spacewars/Runtime/NavigationCrowdNameCatalog.cs",
        "unity/Assets/Spacewars/Runtime/NavigationCrowdStorage.cs",
        "unity/Assets/Spacewars/Runtime/PlayableDomain.cs",
        "unity/Assets/Spacewars/Simulation/NavigationCrowd.cs",
        "unity/Assets/Spacewars/Simulation/NavigationLiveCrowdStorage.cs"]
    if sorted(changed)!=sorted(expected):
        raise RuntimeError("unexpected production source delta from 83fff80: "+repr(changed))
    if out is not None:
        if out.exists(): raise RuntimeError("output path already exists; refusing overwrite")
        out.relative_to(ROOT/".local")
        out.mkdir(parents=True,exist_ok=False)
    return {"runtime316":runtime,"runtimeDeltaFrom83":base_runtime,"changedRuntimePaths":changed,"unityTracked":unity}

def patch_observer(original: bytes, probe: bytes, output_relative: str) -> tuple[bytes,bytes]:
    if sha(original)!=OBSERVER_SHA: raise RuntimeError("legacy ordinary observer SHA differs")
    text=original.decode("utf-8")
    output_from="../../.local/frontier-materialization-01a14d59/army600/game"
    if text.count(output_from)!=1: raise RuntimeError("legacy output location did not match exactly once")
    patched=text.replace(output_from,"../../"+output_relative)
    # Keep every historical assertion, seed, timeout, preparation and sampling
    # statement. Only add bounded diagnostic hooks at explicit observer edges.
    edits=[]
    def replace(old: str,new: str):
        nonlocal patched
        if patched.count(old)!=1: raise RuntimeError("instrumentation anchor count != 1: "+old[:100])
        edits.append((old,new));patched=patched.replace(old,new)
    replace('    static readonly FieldInfo PortSession=typeof(NavigationRoutePort).GetField("session",Private);',
        '    static readonly FieldInfo PortSession=typeof(NavigationRoutePort).GetField("session",Private);\n    static readonly FieldInfo CreditBankField=typeof(NavigationSession).GetField("creditBank",Private);')
    bank_helper='''    static object ProfileCreditBankScalars(NavigationSession session,NavigationHostRoot root){
        var bank=(IReadOnlyList<long>)CreditBankField.GetValue(session);if(bank==null)return new{available=false,reason="null-bank"};
        int storageCount=bank.Count;if(storageCount<12)return new{available=false,reason="short-bank",storageCount};
        long generation=bank[1],watermark=bank[2],cap=bank[3],head=bank[4],queued=bank[5],accepted=bank[6],promoted=bank[7],discarded=bank[8],drops=bank[9];
        if(head<0||head>=600||queued<0||queued>cap||cap<1||cap>600)return new{available=false,reason="invalid-scalar-bounds",storageCount,generation,watermark,cap,head,queued,accepted,promoted,discarded,drops};
        long? headTick=null,headPrefix=null,headBudget=null,headRevision=null;
        if(queued>0){int slot=checked(12+4*(int)head);if(slot+3>=storageCount)return new{available=false,reason="head-outside-bank",storageCount,generation,watermark,cap,head,queued,accepted,promoted,discarded,drops};headTick=bank[slot];headPrefix=bank[slot+1];headBudget=bank[slot+2];headRevision=bank[slot+3];}
        return new{available=true,storageCount,generation,watermark,cap,head,queueCount=queued,accepted,promoted,discarded,saturationDrops=drops,headTick,headPrefix,headBudget,headRevision,identityConsistent=accepted==promoted+discarded+queued&&promoted==root.QuantumSerial};
    }
'''
    replace('    static object CaptureLiveGate(',bank_helper+'    static object CaptureLiveGate(')
    replace('        var budget=session.PhysicalHostBudget;long? retained=budget?.RetainedBytes;',
        '        var budget=session.PhysicalHostBudget;long? retained=budget?.RetainedBytes;object creditBank=ProfileCreditBankScalars(session,root);')
    replace('root.FairArrivals,root.LastServedArrival,root.ArrivalRoundEnd,fairCreationPhaseAllowed=',
        'root.FairArrivals,root.LastServedArrival,root.ArrivalRoundEnd,creditBank,fairCreationPhaseAllowed=')
    replace('        Directory.CreateDirectory(output);string lab=',
        '        Directory.CreateDirectory(output);var clockPreflight=OrdinaryHostProfileProbe.Preflight();Assert.IsTrue(OrdinaryHostProfileProbe.PreflightPassed,"Clock preflight failed before solver attachment");string lab=')
    replace('            Assert.True(freshV8,"Fresh ordinary genesis must create Arrival V8 exact-Astar and normal metric work before the army observation window.");',
        '            Assert.True(freshV8,"Fresh ordinary genesis must create Arrival V8 exact-Astar and normal metric work before the army observation window.");\n            OrdinaryHostProfileProbe.Attach(runtime.NavigationBinding.RoutePort);')
    replace('                var rootRow=CaptureRoot(runtime,core,seenAttempts,originals,firstGroup,firstSequence,out string arrivalKey); // Scalars only; pin already released.',
        '                object rootRow;string arrivalKey;OrdinaryHostProfileProbe.BeginCapture();try{rootRow=CaptureRoot(runtime,core,seenAttempts,originals,firstGroup,firstSequence,out arrivalKey);}finally{OrdinaryHostProfileProbe.EndCapture();} // Scalars only; pin already released.')
    replace('var dto=DecisionDto(live,o,r,p);lock(causalGate)',
        'object dto;OrdinaryHostProfileProbe.BeginDecisionDto();try{dto=DecisionDto(live,o,r,p);}finally{OrdinaryHostProfileProbe.EndDecisionDto();}lock(causalGate)')
    replace('long requested=System.Diagnostics.Stopwatch.GetTimestamp();',
        'OrdinaryHostProfileProbe.GateRequested();long requested=System.Diagnostics.Stopwatch.GetTimestamp();')
    replace('port.RunHostStep(()=>{long entered=System.Diagnostics.Stopwatch.GetTimestamp();',
        'port.RunHostStep(()=>{OrdinaryHostProfileProbe.GateEntered();long entered=System.Diagnostics.Stopwatch.GetTimestamp();')
    replace('finally{GateHoldTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-entered;}',
        'finally{GateHoldTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-entered;OrdinaryHostProfileProbe.GateExited();}')
    replace('foreach(var dto in batch)causalWriter.WriteLine(TimedJson(dto));causalWriter.Flush();Assert.IsNull(error,error);',
        'foreach(var dto in batch){string json=TimedJson(dto);OrdinaryHostProfileProbe.BeginObserverIo();try{causalWriter.WriteLine(json);}finally{OrdinaryHostProfileProbe.EndObserverIo();}}OrdinaryHostProfileProbe.BeginObserverIo();try{causalWriter.Flush();}finally{OrdinaryHostProfileProbe.EndObserverIo();}Assert.IsNull(error,error);')
    replace('if(signature!=previous||headers.Length>0||metadata.Length>0){writer.WriteLine(TimedJson(row));previous=signature;',
        'if(signature!=previous||headers.Length>0||metadata.Length>0){string json=TimedJson(row);OrdinaryHostProfileProbe.BeginObserverIo();try{writer.WriteLine(json);}finally{OrdinaryHostProfileProbe.EndObserverIo();}previous=signature;')
    replace('writer.Flush();TestContext.WriteLine("PREFIX_RESULT "',
        'OrdinaryHostProfileProbe.BeginObserverIo();try{writer.Flush();}finally{OrdinaryHostProfileProbe.EndObserverIo();}TestContext.WriteLine("PREFIX_RESULT "')
    replace('            DecisionHook.SetValue(null,savedHook);DecisionFilter.SetValue(null,savedFilter);\n            DrainCausal();',
        '            DecisionHook.SetValue(null,savedHook);DecisionFilter.SetValue(null,savedFilter);\n            DrainCausal();\n            if(runtime!=null){OrdinaryHostProfileProbe.Detach(runtime.NavigationBinding.RoutePort);File.WriteAllText(Path.Combine(output,"host-profile-events.json"),JsonConvert.SerializeObject(OrdinaryHostProfileProbe.Snapshot(),Formatting.None));}')
    replace('        finally{\n            if(!File.Exists(Path.Combine(output,"game-result.json")))',
        '        finally{\n            OrdinaryHostProfileProbe.ObservationEnd(); // exact end of the unchanged 600-tick observer window; cleanup follows.\n            if(!File.Exists(Path.Combine(output,"game-result.json")))')
    # Invert only output location and exact QA timing/scalar insertions.
    inverse=patched
    for old,new in reversed(edits):
        if inverse.count(new)!=1: raise RuntimeError("inverse edit anchor mismatch")
        inverse=inverse.replace(new,old)
    inverse=inverse.replace("../../"+output_relative,output_from)
    if inverse.encode()!=original: raise RuntimeError("instrumentation inverse differs from exact historical observer")
    return patched.encode(),probe

def pair_summary(event_doc: dict) -> dict:
    events=event_doc.get("events",[])
    names={1:"advance-start",2:"scope-refused",3:"authority-bytes-refused",4:"advance-retention-blocked",5:"frontier-start",6:"frontier-end",7:"publish-start",8:"committed",9:"release-start",10:"release-end",11:"source-owner-refused",12:"closed-without-advance",13:"no-eligible",14:"prepared-admission-refused",15:"before-commit",101:"capture-start",102:"capture-end",103:"decision-dto-start",104:"decision-dto-end",105:"io-start",106:"io-end",107:"gate-requested",108:"gate-entered",109:"gate-exited",110:"observation-end"}
    by_thread={}
    for row in events: by_thread.setdefault(row["managedThread"],[]).append(row)
    negative=[];pairs=[];unmatched=[]
    for tid,rows in by_thread.items():
        for a,b in zip(rows,rows[1:]):
            for key in ("monotonicNanoseconds","processCpuNanoseconds","threadCpuNanoseconds"):
                if b[key]<a[key]: negative.append({"thread":tid,"from":a["sequence"],"to":b["sequence"],"clock":key})
        stack=[]
        for row in rows:
            k=row["kind"]
            if k in (101,103,105,107): stack.append(row)
            elif k in (102,104,106,108,109):
                startkind={102:101,104:103,106:105,108:107,109:108}[k]
                found=next((i for i in range(len(stack)-1,-1,-1) if stack[i]["kind"]==startkind),None)
                if found is None: unmatched.append({"thread":tid,"sequence":row["sequence"],"kind":names.get(k,str(k))});continue
                start=stack.pop(found)
                deltas={clock:row[clock]-start[clock] for clock in ("monotonicNanoseconds","processCpuNanoseconds","threadCpuNanoseconds")}
                if any(v<0 for v in deltas.values()): negative.append({"thread":tid,"from":start["sequence"],"to":row["sequence"],"deltas":deltas})
                pairs.append({"thread":tid,"start":names[startkind],"end":names[k],"sequenceStart":start["sequence"],"sequenceEnd":row["sequence"],"deltas":deltas})
                if k==108: stack.append(row)
            if row["clockError"]: negative.append({"thread":tid,"sequence":row["sequence"],"clockError":row["clockError"]})
        unmatched.extend({"thread":tid,"sequence":r["sequence"],"kind":names.get(r["kind"],str(r["kind"])),"classification":"censored-at-window-end"} for r in stack)
    host_counts={name:sum(1 for r in events if names.get(r["kind"])==name) for name in names.values() if r_has_kind(events,names,name)}
    host_intervals=[];host_unpaired=[];host_gaps=[];host_left_censored=[];host_cleanup_censored=[]
    boundary_names={1:"advance-start",15:"before-commit",5:"frontier-start",6:"frontier-end",7:"publish-start",8:"committed",9:"release-start",10:"release-end"}
    reason_names={2:"scope-refused",3:"authority-bytes-refused",4:"advance-retention-blocked",14:"prepared-admission-refused"}
    allowed={1:{15,2,3,4,14,9},15:{5,14,9},5:{6,14,9},6:{7,14,9},7:{8,14,9},8:{9},9:{10},2:{9},3:{9},4:{9},14:{9}}
    for tid in by_thread:
        rows=[r for r in events if r["managedThread"]==tid and (r.get("kind") in boundary_names or r.get("kind") in reason_names or r.get("kind")==11)]
        active=[];closed=False;seen_advance=False;leading=[]
        for row in rows:
            k=row["kind"]
            if k==1:
                if active: host_gaps.append({"thread":tid,"sequence":row["sequence"],"reason":"new advance-start before previous service prefix closed"})
                if leading: host_left_censored.append({"thread":tid,"firstAdvanceSequence":row["sequence"],"events":[{"sequence":r["sequence"],"kind":names.get(r["kind"],str(r["kind"]))} for r in leading]});leading=[]
                active=[row];closed=False;seen_advance=True;continue
            if k==11:
                if not seen_advance:
                    leading.append(row);continue
                if closed: continue
                if active and active[-1]["kind"]==9:
                    host_cleanup_censored.append({"thread":tid,"sequence":row["sequence"],"classification":"source-owner-refused-after-release-start; cleanup interval right-censored"});active=[];closed=True;continue
                host_gaps.append({"thread":tid,"sequence":row["sequence"],"reason":"source-owner-refused was not after release-end or interrupted cleanup"})
                continue
            if not active:
                if not seen_advance: leading.append(row)
                else: host_gaps.append({"thread":tid,"sequence":row["sequence"],"reason":"service event without active advance prefix"})
                continue
            left=active[-1]; leftname=boundary_names.get(left["kind"],reason_names.get(left["kind"],str(left["kind"])))
            rightname=boundary_names.get(k,reason_names.get(k,str(k)))
            if k not in allowed.get(left["kind"],set()):
                host_gaps.append({"thread":tid,"from":leftname,"next":rightname,"sequenceStart":left["sequence"],"sequenceEnd":row["sequence"],"reason":"unexplained service hook gap or illegal transition"});active=[];continue
            host_intervals.append({"thread":tid,"from":leftname,"to":rightname,"sequenceStart":left["sequence"],"sequenceEnd":row["sequence"],"deltas":{clock:row[clock]-left[clock] for clock in ("monotonicNanoseconds","processCpuNanoseconds","threadCpuNanoseconds")},"complete":True,"classification":"bounded-stage" if k in boundary_names else "bounded-refusal-prefix"})
            if k==10: active=[];closed=True
            else: active.append(row)
        if active: host_unpaired.append({"thread":tid,"from":boundary_names.get(active[-1]["kind"],str(active[-1]["kind"])),"sequence":active[-1]["sequence"],"classification":"right-censored-at-observer-window-end"})
    host_negative=[r for r in host_intervals if any(v<0 for v in r["deltas"].values())];negative.extend(host_negative)
    buckets={}
    for row in pairs+host_intervals:
        label=(row.get("start")+"->"+row.get("end")) if "start" in row else row.get("from")+"->"+row.get("to")
        bucket=buckets.setdefault(label,{clock:[] for clock in ("monotonicNanoseconds","processCpuNanoseconds","threadCpuNanoseconds")})
        for clock,value in row["deltas"].items(): bucket[clock].append(value)
    distributions={}
    for label,clockmap in buckets.items():
        distributions[label]={}
        for clock,values in clockmap.items():
            ordered=sorted(values)
            if ordered:
                mid=len(ordered)//2;median=ordered[mid] if len(ordered)%2 else (ordered[mid-1]+ordered[mid])/2
                distributions[label][clock]={"count":len(ordered),"sum":sum(ordered),"min":ordered[0],"median":median,"max":ordered[-1]}
    status="NotQualified" if event_doc.get("status")!="PASS" or event_doc.get("preflight",{}).get("passed") is not True or negative or host_gaps or unmatched else "PARTIAL_TIMING"
    return {"status":status,"eventCount":len(events),"hostStageCounts":host_counts,"hostBoundaryIntervals":host_intervals,"hostUnpairedPrefixes":host_unpaired,"hostLeftCensoredPrefixes":host_left_censored,"hostInterruptedCleanup":host_cleanup_censored,"hostAutomatonGaps":host_gaps,"sameThreadObserverIntervals":pairs,"censoredOrUnpairedObserverIntervals":unmatched,"phaseDistributions":distributions,"negativeClockEvidence":negative,"attemptIdentity":"Unavailable from existing hooks; no nearest-snapshot association made.","legitimateRefusalPrefixes":"Preserved as partial/closed event sequences; not forced to include every stage."}

def r_has_kind(events,names,name): return any(names.get(r.get("kind"))==name for r in events)

def static_check(source_pin: str,qa_pin: str) -> dict:
    bindings=source_guards(source_pin,qa_pin,None)
    if sha(RAW.read_bytes())!=RAW_SHA: raise RuntimeError("legacy ordinary archive SHA mismatch")
    with zipfile.ZipFile(RAW) as archive:
        if archive.testzip() is not None: raise RuntimeError("legacy ordinary archive CRC failure")
        observer=archive.read("army600/observer.cs")
    runner=subprocess.check_output(["git","show",f"{qa_pin}:tools/qa/army_cow_ordinary.py"],cwd=ROOT)
    patched,_=patch_observer(observer,(ROOT/"tools/qa/army_host_profile.cs").read_bytes(),".local/static-host-profile/game")
    def rows(kinds):
        return {"status":"PASS","preflight":{"passed":True},"events":[{"sequence":i,"kind":kind,"managedThread":1,"monotonicNanoseconds":i*100,"processCpuNanoseconds":i*60,"threadCpuNanoseconds":i*40,"clockError":0} for i,kind in enumerate(kinds)]}
    full=pair_summary(rows([1,15,5,6,7,8,9,10,101,102,103,104,105,106,107,108,109]))
    if full["status"]!="PARTIAL_TIMING" or len(full["hostBoundaryIntervals"])<6: raise RuntimeError("full lifecycle clock pairing check failed")
    refusal=pair_summary(rows([1,3,9,10,11]))
    if not refusal["hostBoundaryIntervals"] or refusal["status"]!="PARTIAL_TIMING": raise RuntimeError("legal refusal-prefix check failed")
    gap=pair_summary(rows([1,5,6,7,8,9,10]))
    if gap["status"]!="NotQualified" or not gap["hostAutomatonGaps"]: raise RuntimeError("unexplained service gap was not rejected")
    left=pair_summary(rows([15,5,6,7,8,9,10,1,15,5,6,7,8,9,10]))
    if left["status"]!="PARTIAL_TIMING" or not left["hostLeftCensoredPrefixes"]: raise RuntimeError("documented left-censored service prefix was rejected")
    interrupted=pair_summary(rows([1,15,9,11,1,15,5,6,7,8,9,10]))
    if interrupted["status"]!="PARTIAL_TIMING" or not interrupted["hostInterruptedCleanup"]: raise RuntimeError("interrupted-cleanup owner-refusal prefix was rejected")
    negative=rows([1,15,5,6,7,8,9,10]);negative["events"][1]["monotonicNanoseconds"]=-1
    if pair_summary(negative)["status"]!="NotQualified": raise RuntimeError("negative-clock rejection check failed")
    return {"status":"PASS","sourceBindings":{"runtimeCount":len(bindings["runtime316"]),"runtimeChanges":bindings["changedRuntimePaths"],"unityTrackedCount":len(bindings["unityTracked"])},"legacyRunnerSha256":sha(runner),"legacyObserverSha256":sha(observer),"instrumentedObserverSha256":sha(patched),"inverseObserverExact":True,"clockPairingTests":{"fullLifecycle":"PASS","legalRefusalPrefix":"PASS","unexplainedGapRejected":"PASS","leftCensoredPrefix":"PASS","interruptedCleanup":"PASS","negativeClock":"PASS"},"solverStarted":False}

def main() -> int:
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--source-pin",required=True);ap.add_argument("--qa-pin",required=True);ap.add_argument("--out",type=Path);ap.add_argument("--static-check",action="store_true")
    a=ap.parse_args()
    if a.static_check:
        print(json.dumps(static_check(a.source_pin,a.qa_pin),sort_keys=True));return 0
    if a.out is None: ap.error("--out required for the actual one-run route")
    out=a.out.resolve()
    bindings=source_guards(a.source_pin,a.qa_pin,out)
    if sha(RAW.read_bytes())!=RAW_SHA: raise RuntimeError("legacy ordinary archive SHA mismatch")
    with zipfile.ZipFile(RAW) as archive:
        if archive.testzip() is not None: raise RuntimeError("legacy ordinary archive CRC failure")
        original=archive.read("army600/observer.cs");meta=archive.read("army600/observer.cs.meta")
    runner=subprocess.check_output(["git","show",f"{a.qa_pin}:tools/qa/army_cow_ordinary.py"],cwd=ROOT)
    probe=(ROOT/"tools/qa/army_host_profile.cs").read_bytes()
    output_relative=out.relative_to(ROOT).as_posix()+"/game"
    patched,probe=patch_observer(original,probe,output_relative)
    write_json(out/"source-manifest.json",{"candidateCodePin":BASE_CODE,"runtimePin":BASE_RUNTIME,"legacyQaPin":a.qa_pin,"source":bindings,"legacyRunner":{"path":"tools/qa/army_cow_ordinary.py","gitBlob":git("rev-parse",f"{a.qa_pin}:tools/qa/army_cow_ordinary.py"),"sha256":sha(runner)},"observer":{"archive":str(RAW),"archiveSHA256":RAW_SHA,"path":"army600/observer.cs","sha256":sha(original)},"instrumentedObserverSha256":sha(patched),"probeSourceSha256":sha(probe)})
    (out/"observer-original.cs").write_bytes(original);(out/"observer-executed.cs").write_bytes(patched);(out/"observer.meta").write_bytes(meta);(out/"probe-executed.cs").write_bytes(probe)
    diff="".join(difflib.unified_diff(original.decode().splitlines(True),patched.decode().splitlines(True),fromfile="legacy-observer",tofile="instrumented-observer"))
    (out/"observer-instrumentation.diff").write_text(diff)
    if patched==original: raise RuntimeError("observer instrumentation unexpectedly empty")
    # Persist exact working-source binding before the actual Unity invocation.
    owned_paths=["unity/Assets/Spacewars/Runtime","unity/Assets/Spacewars/Simulation","unity/Assets/Spacewars/Tests","unity/Assets/Spacewars/Resources","unity/Packages","unity/ProjectSettings"]
    source_tree=tracked_tree(BASE_CODE,tuple(owned_paths))
    write_json(out/"unity-source-before.json",source_tree)
    TEST.parent.mkdir(parents=True,exist_ok=True)
    test_meta=meta
    helper_meta=("fileFormatVersion: 2\nguid: "+sha(probe)[:32]+"\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n").encode()
    TEST.write_bytes(patched);TEST_META.write_bytes(test_meta);PROBE.write_bytes(probe);PROBE_META.write_bytes(helper_meta)
    command=["tools/unity.sh","exclusive","-batchmode","-nographics","-noaudio","-muted","-runTests","-testPlatform","PlayMode","-testFilter","NativeFirstArmyRoutingPrefixTests.FoundryFirstArmyThroughOrdinaryLobby","-testResults",str(out/"playmode.xml"),"-logFile",str(out/"playmode.log")]
    write_json(out/"command.json",{"command":command,"route":"inner exclusive unity.sh; no outer unity-run lease","audio":"-noaudio -muted before startup","scope":"single Army600 actual ordinary threaded host witness; existing assertions unchanged"})
    env=dict(os.environ);env["UNITY_RUN_MODE"]="exclusive"
    started=time.monotonic();proc=None
    try:
        with (out/"runner.log").open("wb") as log:
            proc=subprocess.run(command,cwd=ROOT,env=env,stdout=log,stderr=subprocess.STDOUT,timeout=900)
    except subprocess.TimeoutExpired:
        write_json(out/"run-status.json",{"status":"NotRun","reason":"outer runner timeout; no retry","timeoutSeconds":900})
        raise
    finally:
        for path,expected in ((TEST,patched),(TEST_META,test_meta),(PROBE,probe),(PROBE_META,helper_meta)):
            if path.exists():
                if path.read_bytes()!=expected: raise RuntimeError("temporary QA source changed externally: "+str(path))
                path.unlink()
        for row in source_tree:
            if sha((ROOT/row["path"]).read_bytes())!=row["sha256"]: raise RuntimeError("tracked source changed during profile: "+row["path"])
        runmeta={"command":command,"returncode":None if proc is None else proc.returncode,"processWallSeconds":time.monotonic()-started,"scope":"includes unity-run admission/startup/import/compile/one test/teardown; not solver-only wall"}
        write_json(out/"run-status.json",runmeta)
        assemblies=ROOT/"unity/Library/ScriptAssemblies";compiled=out/"compiled-assemblies";compiled.mkdir(exist_ok=True)
        for f in assemblies.glob("Spacewars.*"):
            if f.is_file(): shutil.copy2(f,compiled/f.name)
        write_json(out/"compiled-assemblies.sha256",[{"path":p.name,"sha256":sha(p.read_bytes()),"bytes":p.stat().st_size} for p in sorted(compiled.iterdir()) if p.is_file()])
        after=[]
        for name in ("playmode.xml","playmode.log","host-profile-events.json","game/game-result.json","game/observer-timing.json"):
            p=out/name
            if p.is_file(): after.append({"path":name,"sha256":sha(p.read_bytes()),"bytes":p.stat().st_size})
        write_json(out/"raw-result-manifest.json",after)
    events_path=out/"game/host-profile-events.json"
    if events_path.is_file():
        event_doc=json.loads(events_path.read_text())
        write_json(out/"timing-analysis.json",pair_summary(event_doc))
    else: write_json(out/"timing-analysis.json",{"status":"NotRun","reason":"probe result missing; preserve original game verdict and XML"})
    print(json.dumps({"returncode":proc.returncode,"out":str(out),"gameResult":str(out/"game/game-result.json"),"profiling":"one trajectory only; do not rerun automatically"},sort_keys=True))
    return proc.returncode

if __name__=="__main__": raise SystemExit(main())
