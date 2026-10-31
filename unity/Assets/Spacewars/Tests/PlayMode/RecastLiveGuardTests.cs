using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class RecastLiveGuardTests
    {
        private const int Seed=RecastLayerInputs.Seed,Horizon=RecastLayerInputs.Horizon;
        private const long Generation=RecastLayerInputs.Generation;
        private static readonly NavPoint Goal=RecastLayerInputs.Goal.Position;
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private sealed class Body{public int Id;public NavPoint Position,Goal;public NavLocation Location;public double Radius,Speed,Height;public bool Moving,Held,Selected,AllSlots;public NavigationOutcome Outcome;public long Installed;public RecastMemberProgress Progress;public string Spine;}
        private sealed class Frame{public long Tick;public Body[] Bodies;}
        private static readonly PropertyInfo UnitStorage=typeof(NavUnit).GetProperty("StorageOwner",Hidden);
        private static readonly FieldInfo InstalledOwner=UnitStorage.PropertyType.GetField("InstalledExecution",Hidden);
        private static NavCorridorDescriptor OwnedCorridor(NavUnit actor){var storage=UnitStorage.GetValue(actor);return (storage==null?null:InstalledOwner.GetValue(storage) as NavigationInstalledExecutionOwner)?.Corridor?.Corridor;}
        private static readonly Type NativeType=typeof(RecastNavigationMap).Assembly.GetType("Spacewars.Simulation.RecastNative",true);
        private static ulong NativeAllocations()=>(ulong)NativeType.GetMethod("sw_recast_allocation_count",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        private static long ReleaseFaults()=>(long)NativeType.GetProperty("ReleaseFaults",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        private static string SpineBinding(NavCorridorDescriptor corridor){var plan=corridor?.RecastPlan;if(plan==null)return null;var spine=plan.GetType().GetField("Spine",Hidden).GetValue(plan);return (string)spine.GetType().GetProperty("Binding",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(spine);}
        private static PlayableDomain Domain(PlayableRuntime runtime)=>(PlayableDomain)typeof(PlayableRuntime).GetField("domain",Hidden).GetValue(runtime);
        private static object Cleanup(PlayableRuntime runtime,UnityHostRouteService host,PlayableDomain domain)
        {
            if(runtime==null)return null;
            domain=domain??Domain(runtime);domain.CombatStepForQa=null;
            var worker=host==null?null:(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",Hidden).GetValue(host);
            runtime.RequestStop();host?.Dispose();runtime.Dispose();
            Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000),"actual runtime/worker stop ACK");
            var nav=domain.Navigation;nav.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();
            var budget=nav.PhysicalHostBudget;
            Assert.Zero(budget.PhysicalBytes,"shutdown physical owners");Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            return new{RuntimeStopped=runtime.IsStopped,WorkerStopped=worker==null||worker.IsStopped,budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes};
        }
        // Keep nested failures inside the child iterator until its owner resumes,
        // so Unity's nested-enumerator abort cannot skip the owner's finally.
        private sealed class GuardAwait
        {
            private readonly Action<string> service;private readonly Func<double> now;private Exception failure;
            internal GuardAwait(Action<string> service,Func<double> now){this.service=service;this.now=now;}
            internal void Rethrow(){if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();}
            internal IEnumerator Until(Func<bool> done,string name,double deadline){
                while(true){
                    bool waiting=false;try{waiting=!done()&&now()<deadline;}catch(Exception error){failure=error;}
                    if(failure!=null)yield break;if(!waiting)break;
                    try{service(name);}catch(Exception error){failure=error;}if(failure!=null)yield break;yield return null;
                }
                try{Assert.True(done(),name);}catch(Exception error){failure=error;}
            }
        }
        [TestCase(false),TestCase(true)] public void DeliberateNestedWaitFailureStillExecutesOwnerFinally(bool predicateThrows)
        {
            bool cleanup=false;var invariant=new InvalidOperationException("deliberate predicate boundary");var wait=new GuardAwait(_=>Assert.Fail("No runtime service may run in this driver."),()=>0);
            bool Done(){if(predicateThrows)throw invariant;return false;}
            IEnumerator Owner(){try{yield return wait.Until(Done,"deliberate nested boundary",0);wait.Rethrow();}finally{cleanup=true;}}
            var owner=Owner();Assert.True(owner.MoveNext());var child=(IEnumerator)owner.Current;Assert.False(child.MoveNext());
            if(predicateThrows)Assert.AreSame(invariant,Assert.Throws<InvalidOperationException>(()=>owner.MoveNext()));
            else{var original=Assert.Throws<AssertionException>(()=>owner.MoveNext());StringAssert.Contains("deliberate nested boundary",original.Message);}
            Assert.True(cleanup,"Original nested failure must execute owner's finally.");
        }
        [UnityTest,Timeout(240000)]
        public IEnumerator PendingHoldSubsetCurrentSaveNaturallyArrive()
        {
            string output=Environment.GetEnvironmentVariable("RECAST_GUARD_OUTPUT")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/recast-guard/actual"));Directory.CreateDirectory(output);
            Assert.True(NativeSilentStartup.IsSilent);TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));Assert.IsNotNull(TestMatchLogSession.CurrentDirectory);
            var profile=PlayableProfile.RecastLayerQualification;var map=(StackedRampQualificationMap)profile.AuthoredMap;
            var config=RecastLayerInputs.Create(profile,out var scenario);
            PlayableRuntime runtime=null;UnityHostRouteService host=null;PlayableDomain domain=null;var frames=new ConcurrentQueue<Frame>();var observed=new List<Frame>();var failures=new List<string>();var observerErrors=new ConcurrentQueue<string>();var cleanupErrors=new List<string>();
            object firstCensus=null,lastCensus=null;CertifiedRoutePublication firstHost=null,lastHost=null;int[] ids=Array.Empty<int>();long issued=-1,firstMotion=-1,savedTick=-1;int pauseForSave=0;long pendingTick=-1,holdStart=-1,holdEnd=-1;NavPoint heldPosition=default;NavLocation heldLocation=default;NavLocation? otherGoal=null,otherOriginalGoal=null;long otherGroup=-1,otherRevision=-1,otherRequest=-1;int pauseOtherArrival=0;bool pendingObserved=false;var entered=new ManualResetEventSlim();var release=new ManualResetEventSlim();CertifiedRouteKernel controlledKernel=null;bool arrived=false,complete=false;byte[] saved=null;string phase="startup";
            ulong nativeBefore=NativeAllocations();double deadline=Time.realtimeSinceStartupAsDouble+180;
            var wait=new GuardAwait(name=>{host.Service(runtime,8);Assert.IsNull(runtime.Latest?.Failure,name);},()=>Time.realtimeSinceStartupAsDouble);
            void ThrowAwaited()=>wait.Rethrow();
            IEnumerator Until(Func<bool> done,string name)=>wait.Until(done,name,deadline);
            void Drain(){while(frames.TryDequeue(out var frame))observed.Add(frame);}
            Body[] Read(){var nav=domain.Navigation;return nav.Crowd.Units.Select(a=>{var group=nav.GroupFor(a.Id);var member=group?.Members.SingleOrDefault(m=>m.Entity==a.Id);return new Body{Id=a.Id,Position=a.Position,Goal=a.Goal,Location=a.Location,Radius=a.Radius,Speed=a.Speed,Height=map.Height(a.Location),Moving=a.Moving,Held=a.Held,Selected=ids.Contains(a.Id),AllSlots=group!=null&&member!=null&&member.HasGoal&&member.GoalLocation.HasValue&&(!a.Moving||member.Phase!=GroupMemberPhase.Held&&member.Phase!=GroupMemberPhase.Stopped)&&group.Members.Where(m=>m.Phase!=GroupMemberPhase.Held&&m.Phase!=GroupMemberPhase.Stopped).All(m=>m.HasGoal&&m.GoalLocation.HasValue),Outcome=a.Outcome,Installed=a.InstalledRequest,Progress=member?.Recast??default,Spine=SpineBinding(OwnedCorridor(a))};}).OrderBy(x=>x.Id).ToArray();}
            void Observe(){try{if(Volatile.Read(ref pauseOtherArrival)!=0&&Read().Any(b=>b.Id==ids[1]&&b.Outcome==NavigationOutcome.Arrived&&b.Location.SurfaceId=="upper"&&b.Goal.Equals(Goal)))runtime.RequestPause(true);if(frames.Count+observed.Count>Horizon+64){observerErrors.Enqueue("bounded frame capacity exceeded");runtime.RequestPause(true);return;}frames.Enqueue(new Frame{Tick=domain.Tick,Bodies=Read()});long start=Interlocked.Read(ref issued);if(start>=0&&(domain.Tick-start>=Horizon||Volatile.Read(ref pauseForSave)!=0&&Read().Any(b=>b.Id==ids[0]&&b.Moving&&b.Installed>0&&b.Goal.Equals(RecastLayerInputs.GuardGoal.Position))))runtime.RequestPause(true);}catch(Exception error){observerErrors.Enqueue(error.ToString());runtime.RequestPause(true);}}
            try{
                runtime=new PlayableRuntime(config,Generation,startPaused:true);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest.Paused&&host.CertifiedPublication?.RecastFoundationBuilds==1,"startup public native foundation");ThrowAwaited();
                Assert.Zero(runtime.Latest.Tick,"preparation did not advance live clock");Assert.True(runtime.Latest.Paused);
                Assert.AreEqual(2,host.CertifiedPublication.RecastColdBuilds,"two distinct public ground clearances");
                domain=Domain(runtime);ids=runtime.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player&&scenario.Any(s=>s.Kind==e.Kind&&s.Position.Equals(e.Position))).Select(e=>e.Id).OrderBy(x=>x).ToArray();Assert.AreEqual(2,ids.Length);
                Assert.True(ids.All(id=>runtime.Latest.Entities.Single(e=>e.Id==id).Kind==PlayableEntityKind.Explorer),"two source-validated small ground actors");
                Assert.AreEqual(4,domain.Navigation.Crowd.Units.Count,"two movers plus two default parked Explorers");
                foreach(int id in ids){var actor=domain.Navigation.Crowd.Units.Single(a=>a.Id==id);Assert.AreEqual("lower",actor.Location.SurfaceId);Assert.True(runtime.NavigationGeometry.IsFree(actor.Position,actor.Radius));}
                Assert.False(map.TryLocate(Goal,0,out _),"overlapping upper goal requires explicit typed command; no first-floor fallback");
                domain.CombatStepForQa=Observe;runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume ordinary command");ThrowAwaited();
                var worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",Hidden).GetValue(host);controlledKernel=(CertifiedRouteKernel)typeof(CertifiedRouteWorker).GetField("kernel",Hidden).GetValue(worker);int prepares=0;
                controlledKernel.BeforePrepare=()=>{if(Interlocked.Increment(ref prepares)!=1)return;entered.Set();if(!release.Wait(TimeSpan.FromSeconds(RecastLayerInputs.GuardPreparationGateSeconds)))throw new TimeoutException("bounded real pending preparation gate");};
                Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,1,"player-1",PlayableCommandKind.Move,ids,Goal).WithResolvedLocation(RecastLayerInputs.Goal)).Accepted);
                yield return Until(()=>entered.IsSet,"actual worker pending request gate");ThrowAwaited();pendingTick=domain.Tick;pendingObserved=domain.Navigation.PendingCount>0;Assert.True(pendingObserved);Assert.Zero(domain.Navigation.AppliedResults);
                phase="pending and queued HOLD";otherGoal=domain.Navigation.GroupFor(ids[1]).Members.Single(m=>m.Entity==ids[1]).GoalLocation;otherOriginalGoal=otherGoal;Assert.True(otherGoal.HasValue);
                Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,2,"player-1",PlayableCommandKind.Hold,new[]{ids[0]},default)).Accepted);release.Set();
                yield return Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1&&r.Status!=PlayableCommandStatus.Accepted),"initial typed Move receipt");ThrowAwaited();
                var receipt=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==1&&r.Status!=PlayableCommandStatus.Accepted);Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);Interlocked.Exchange(ref issued,receipt.AppliedTick);
                yield return Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==2&&r.Status!=PlayableCommandStatus.Accepted),"queued ordinary HOLD receipt");ThrowAwaited();var hold=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==2&&r.Status!=PlayableCommandStatus.Accepted);Assert.AreEqual(PlayableCommandStatus.Applied,hold.Status,hold.Message);
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"held proof prefix");ThrowAwaited();var held=Read().Single(b=>b.Id==ids[0]);Assert.True(held.Held);heldPosition=held.Position;heldLocation=held.Location;holdStart=domain.Tick;
                Assert.AreEqual(otherGoal,domain.Navigation.GroupFor(ids[1]).Members.Single(m=>m.Entity==ids[1]).GoalLocation,"HOLD subset changed remaining actor slot");
                runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused&&domain.Tick-holdStart>=RecastLayerInputs.GuardHoldTicks,"twenty real held ticks");ThrowAwaited();runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"held proof suffix");ThrowAwaited();holdEnd=domain.Tick;held=Read().Single(b=>b.Id==ids[0]);Assert.True(held.Held);Assert.AreEqual(heldPosition,held.Position);Assert.AreEqual(heldLocation,held.Location);
                phase="remaining original intent reissue and physical upper clear";runtime.RequestPause(false);yield return Until(()=>Read().Any(b=>b.Id==ids[1]&&b.Moving&&b.Installed>0),"unselected typed intent really reissued after HOLD");ThrowAwaited();
                Volatile.Write(ref pauseOtherArrival,1);Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,3,"player-1",PlayableCommandKind.Move,new[]{ids[1]},Goal).WithResolvedLocation(RecastLayerInputs.Goal)).Accepted);
                yield return Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==3&&r.Status!=PlayableCommandStatus.Accepted),"same requested upper intent one-member reissue receipt");ThrowAwaited();var reissue=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==3&&r.Status!=PlayableCommandStatus.Accepted);Assert.AreEqual(PlayableCommandStatus.Applied,reissue.Status,reissue.Message);
                yield return Until(()=>runtime.Latest.Paused&&Read().Any(b=>b.Id==ids[1]&&b.Outcome==NavigationOutcome.Arrived&&b.Location.SurfaceId=="upper"&&b.Goal.Equals(Goal)),"remaining actor naturally clears narrow ramp and reaches its upper slot");ThrowAwaited();Volatile.Write(ref pauseOtherArrival,0);
                held=Read().Single(b=>b.Id==ids[0]);Assert.True(held.Held);Assert.AreEqual(heldPosition,held.Position);Assert.AreEqual(heldLocation,held.Location,"HOLD preserved throughout additional upper-clear wait");
                var remaining=domain.Navigation.GroupFor(ids[1]);otherGoal=remaining.Members.Single(m=>m.Entity==ids[1]).GoalLocation;Assert.AreEqual(RecastLayerInputs.Goal,otherGoal);otherGroup=remaining.GroupId;otherRevision=remaining.OrderRevision;otherRequest=Read().Single(b=>b.Id==ids[1]).Installed;
                phase="exact entry subset";Volatile.Write(ref pauseForSave,1);runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume typed subset command");ThrowAwaited();
                Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,4,"player-1",PlayableCommandKind.Move,new[]{ids[0]},RecastLayerInputs.GuardGoal.Position).WithResolvedLocation(RecastLayerInputs.GuardGoal)).Accepted);
                yield return Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==4&&r.Status!=PlayableCommandStatus.Accepted),"typed subset Move receipt");ThrowAwaited();var subset=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==4&&r.Status!=PlayableCommandStatus.Accepted);Assert.AreEqual(PlayableCommandStatus.Applied,subset.Status,subset.Message);
                phase="live guard current save";yield return Until(()=>runtime.Latest.Paused&&Read().Any(b=>b.Id==ids[0]&&b.Moving&&b.Installed>0&&b.Goal.Equals(RecastLayerInputs.GuardGoal.Position)),"new subset Ready active save prefix");ThrowAwaited();Drain();Assert.IsEmpty(observerErrors);
                var prefix=Read();Assert.True(prefix.Where(b=>b.Selected).All(b=>b.Installed>0&&b.Spine!=null),"both current typed decisions installed before save");Assert.True(prefix.Any(b=>b.Selected&&b.Moving));Assert.AreEqual(otherGoal,domain.Navigation.GroupFor(ids[1]).Members.Single(m=>m.Entity==ids[1]).GoalLocation,"new subset changed remaining actor slot");
                Assert.AreEqual(otherGroup,domain.Navigation.GroupFor(ids[1]).GroupId);Assert.AreEqual(otherRevision,domain.Navigation.GroupFor(ids[1]).OrderRevision);Assert.AreEqual(otherRequest,Read().Single(b=>b.Id==ids[1]).Installed,"entry subset altered remaining current decision identity");
                firstHost=host.CertifiedPublication;Assert.GreaterOrEqual(firstHost.RecastLongQueries,2,"initial cohort and new subset each use real native queries");Assert.GreaterOrEqual(firstHost.RecastAttachments,2);Assert.AreEqual(1,firstHost.RecastFoundationBuilds);Assert.Greater(firstHost.RecastFoundationReuses,0);
                var capture=runtime.RequestCaptureBytes();yield return Until(()=>capture.IsCompleted,"current format capture");ThrowAwaited();Assert.IsFalse(capture.IsFaulted,capture.Exception?.ToString());Assert.IsFalse(capture.IsCanceled);saved=capture.Result;
                using(var envelope=PlayableWorldState.Decode(saved)){Assert.AreEqual(PlayableWorldState.CurrentVersion,envelope.Version);Assert.AreEqual(Seed,envelope.Seed);Assert.AreEqual(Generation,envelope.Generation);Assert.AreEqual(config.SourceIdentity,envelope.SourceIdentity);CollectionAssert.AreEqual(WorldWire.Binding(profile),envelope.Binding);CollectionAssert.AreEqual(OfflineConfigurationWire.Binding(config,profile),envelope.Participants);savedTick=envelope.Tick;}
                File.WriteAllBytes(Path.Combine(output,"guard-current.world"),saved);domain.CombatStepForQa=null;firstCensus=Cleanup(runtime,host,domain);runtime=null;host=null;domain=null;
                phase="restored same match";runtime=PlayableRuntime.RestoreBytes(saved,config);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest.Paused&&host.CertifiedPublication?.RecastFoundationBuilds==1,"restored public native preparation");ThrowAwaited();domain=Domain(runtime);
                Assert.AreEqual(savedTick,runtime.Latest.Tick,"restore preparation never advances live time");Assert.True(runtime.Latest.Paused);var restored=Read();Assert.AreEqual(prefix.Length,restored.Length);
                foreach(var expected in prefix){var actual=restored.Single(b=>b.Id==expected.Id);Assert.AreEqual(expected.Position,actual.Position);Assert.AreEqual(expected.Location,actual.Location);Assert.AreEqual(expected.Height,actual.Height);Assert.AreEqual(expected.Goal,actual.Goal);Assert.AreEqual(expected.Outcome,actual.Outcome);Assert.AreEqual(expected.Held,actual.Held);Assert.AreEqual(expected.Installed,actual.Installed);Assert.AreEqual(expected.Spine,actual.Spine);Assert.True(expected.Progress.Equals(actual.Progress),"authoritative Recast progress restored");}
                Assert.Zero(host.CertifiedPublication.RecastLongQueries,"ready decisions restore without route recompute");Volatile.Write(ref pauseForSave,0);domain.CombatStepForQa=Observe;runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume saved movement");ThrowAwaited();
                while(Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest.Failure);Drain();var last=observed.LastOrDefault();arrived=last!=null&&last.Bodies.Where(b=>b.Selected).All(b=>b.Outcome==NavigationOutcome.Arrived);if(arrived||last!=null&&last.Tick-issued>=Horizon||!observerErrors.IsEmpty)break;yield return null;}
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"terminal evidence pause");ThrowAwaited();Drain();phase="safety and arrival";Frame previous=null;var geometry=runtime.NavigationGeometry;
                foreach(var frame in observed){foreach(var body in frame.Bodies){
                    if(!map.IsValidLocation(body.Location,body.Radius)||!geometry.IsFree(body.Position,body.Radius))failures.Add("footprint/layer "+body.Id+"@"+frame.Tick);
                    if(body.Selected&&body.Moving&&!body.AllSlots)failures.Add("motion before all slots "+body.Id);
                    if(body.Selected&&(body.Outcome==NavigationOutcome.Rejected||body.Outcome==NavigationOutcome.Blocked||body.Outcome==NavigationOutcome.Unreachable))failures.Add("terminal failure "+body.Id);
                    if(previous!=null){var old=previous.Bodies.Single(b=>b.Id==body.Id);double dx=body.Position.X-old.Position.X,dz=body.Position.Z-old.Position.Z;
                        if(dx*dx+dz*dz>body.Speed*body.Speed/900+1e-9)failures.Add("speed/teleport "+body.Id);
                        if(!geometry.SegmentFree(old.Position,body.Position,body.Radius)||!map.TryTraverse(old.Location,body.Location,body.Radius,out var at)||!at.Equals(body.Location))failures.Add("typed terrain sweep "+body.Id);
                        if(Math.Abs(body.Height-old.Height)>Math.Sqrt(dx*dx+dz*dz)+1e-8)failures.Add("physical height discontinuity "+body.Id);
                        if(body.Selected&&dx*dx+dz*dz>1e-12&&firstMotion<0)firstMotion=frame.Tick;
                        foreach(var peer in frame.Bodies.Where(b=>b.Id<body.Id)){var oldPeer=previous.Bodies.Single(b=>b.Id==peer.Id);double sx=old.Position.X-oldPeer.Position.X,sz=old.Position.Z-oldPeer.Position.Z,vx=dx-(peer.Position.X-oldPeer.Position.X),vz=dz-(peer.Position.Z-oldPeer.Position.Z),q=vx*vx+vz*vz,t=q==0?0:Math.Max(0,Math.Min(1,-(sx*vx+sz*vz)/q)),x=sx+vx*t,z=sz+vz*t;if(x*x+z*z<Math.Pow(body.Radius+peer.Radius,2)-1e-8)failures.Add("continuous peer sweep "+body.Id+"/"+peer.Id);}
                    }
                }previous=frame;}
                foreach(var frame in observed.Where(f=>f.Tick>=holdStart&&f.Tick<=holdEnd)){var body=frame.Bodies.Single(b=>b.Id==ids[0]);Assert.True(body.Held);Assert.AreEqual(heldPosition,body.Position);Assert.AreEqual(heldLocation,body.Location);}
                Assert.IsEmpty(observerErrors);Assert.IsEmpty(failures);Assert.True(arrived,"both typed actors naturally Arrived by1800 ticks");Assert.GreaterOrEqual(firstMotion,issued);
                foreach(int id in ids){var trail=observed.SelectMany(f=>f.Bodies.Where(b=>b.Id==id)).ToArray();Assert.True(trail.Any(b=>b.Location.SurfaceId=="ramp-support"),"actual ramp "+id);if(id==ids[1])Assert.True(trail.Any(b=>b.Location.SurfaceId=="upper"),"remaining original elevated intent "+id);else Assert.AreEqual(RecastLayerInputs.GuardGoal,trail.Last().Location,"new subset exact semantic endpoint");var final=trail.Last();double x=final.Position.X-final.Goal.X,z=final.Position.Z-final.Goal.Z;Assert.LessOrEqual(Math.Sqrt(x*x+z*z),profile.Navigation.ArrivalTolerance,"own assigned slot "+id);}
                lastHost=host.CertifiedPublication;Assert.Zero(lastHost.RecastLongQueries,"saved ready routes remain installed");complete=true;phase="complete";
            }finally{
                release.Set();if(controlledKernel!=null)controlledKernel.BeforePrepare=null;
                Drain();try{if(runtime!=null)lastCensus=Cleanup(runtime,host,domain);}catch(Exception error){cleanupErrors.Add(error.ToString());}
                try{TestMatchLogSession.Shutdown();}catch(Exception error){cleanupErrors.Add(error.ToString());}
                File.WriteAllText(Path.Combine(output,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(observed));
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Result=complete&&cleanupErrors.Count==0?"PASS":"FAIL",phase,Seed,Generation,Horizon,SavePrefix="first new subset Ready moving toward exact ramp-entry goal",PendingObserved=pendingObserved,PendingTick=pendingTick,HoldStart=holdStart,HoldEnd=holdEnd,HeldPosition=heldPosition,HeldLocation=heldLocation,OtherOriginalGoal=otherOriginalGoal,OtherGoal=otherGoal,OtherGroup=otherGroup,OtherRevision=otherRevision,OtherRequest=otherRequest,GuardGoal=RecastLayerInputs.GuardGoal,Goal,Scenario=scenario,Ids=ids,Issued=issued,FirstMotion=firstMotion,SavedTick=savedTick,Arrived=arrived,FirstHost=firstHost,LastHost=lastHost,Failures=failures,ObserverErrors=observerErrors.ToArray(),CleanupFailures=cleanupErrors,FirstCensus=firstCensus,LastCensus=lastCensus,NativeBefore=nativeBefore,NativeAfter=NativeAllocations(),ReleaseFaults=ReleaseFaults(),Candidate=Environment.GetEnvironmentVariable("RECAST_GUARD_CANDIDATE")},Newtonsoft.Json.Formatting.Indented));
            }
            entered.Dispose();release.Dispose();Assert.IsEmpty(cleanupErrors);Assert.AreEqual(nativeBefore,NativeAllocations());Assert.Zero(ReleaseFaults());
        }
    }
}
