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
    public sealed class RecastLayerExecutionTests
    {
        private const int Seed=RecastLayerInputs.Seed,Horizon=RecastLayerInputs.Horizon;
        private const long Generation=RecastLayerInputs.Generation;
        private static readonly NavPoint Goal=RecastLayerInputs.Goal.Position;
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private sealed class Body{public int Id;public NavPoint Position,Goal;public NavLocation Location;public double Radius,Speed,Height;public bool Moving,Selected,AllSlots;public NavigationOutcome Outcome;public long Installed;public RecastMemberProgress Progress;public string Spine;}
        private sealed class Frame{public long Tick;public Body[] Bodies;}
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
        [UnityTest,Timeout(240000)]
        public IEnumerator TypedLayeredRampCurrentActiveSaveNaturallyArrive()
        {
            string output=Environment.GetEnvironmentVariable("RECAST_LAYER_OUTPUT")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/recast-layer/actual"));Directory.CreateDirectory(output);
            Assert.True(NativeSilentStartup.IsSilent);TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));Assert.IsNotNull(TestMatchLogSession.CurrentDirectory);
            var profile=PlayableProfile.RecastLayerQualification;var map=(StackedRampQualificationMap)profile.AuthoredMap;
            var config=RecastLayerInputs.Create(profile,out var scenario);
            PlayableRuntime runtime=null;UnityHostRouteService host=null;PlayableDomain domain=null;var frames=new ConcurrentQueue<Frame>();var observed=new List<Frame>();var failures=new List<string>();var observerErrors=new ConcurrentQueue<string>();var cleanupErrors=new List<string>();
            object firstCensus=null,lastCensus=null;CertifiedRoutePublication firstHost=null,lastHost=null;int[] ids=Array.Empty<int>();long issued=-1,firstMotion=-1,savedTick=-1;int pauseForSave=1;bool arrived=false,complete=false;byte[] saved=null;string phase="startup";
            ulong nativeBefore=NativeAllocations();double deadline=Time.realtimeSinceStartupAsDouble+180;
            IEnumerator Until(Func<bool> done,string name){while(!done()&&Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest?.Failure,name);yield return null;}Assert.True(done(),name);}
            void Drain(){while(frames.TryDequeue(out var frame))observed.Add(frame);}
            Body[] Read(){var nav=domain.Navigation;return nav.Crowd.Units.Select(a=>{var group=nav.GroupFor(a.Id);var member=group?.Members.SingleOrDefault(m=>m.Entity==a.Id);return new Body{Id=a.Id,Position=a.Position,Goal=a.Goal,Location=a.Location,Radius=a.Radius,Speed=a.Speed,Height=map.Height(a.Location),Moving=a.Moving,Selected=ids.Contains(a.Id),AllSlots=group!=null&&group.Members.Length==2&&group.Members.All(m=>m.HasGoal&&m.GoalLocation.HasValue),Outcome=a.Outcome,Installed=a.InstalledRequest,Progress=member?.Recast??default,Spine=SpineBinding(nav.InstalledExecutionFor(a.Id)?.Corridor)};}).OrderBy(x=>x.Id).ToArray();}
            void Observe(){try{if(frames.Count+observed.Count>Horizon+64){observerErrors.Enqueue("bounded frame capacity exceeded");runtime.RequestPause(true);return;}frames.Enqueue(new Frame{Tick=domain.Tick,Bodies=Read()});long start=Interlocked.Read(ref issued);if(start>=0&&(domain.Tick-start>=Horizon||Volatile.Read(ref pauseForSave)!=0&&Read().Any(b=>b.Selected&&b.Moving&&b.Location.SurfaceId=="ramp-support"&&b.Position.X>-1&&b.Position.X<1)))runtime.RequestPause(true);}catch(Exception error){observerErrors.Enqueue(error.ToString());runtime.RequestPause(true);}}
            try{
                runtime=new PlayableRuntime(config,Generation,startPaused:true);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest.Paused&&host.CertifiedPublication?.RecastFoundationBuilds==1,"startup public native foundation");
                Assert.Zero(runtime.Latest.Tick,"preparation did not advance live clock");Assert.True(runtime.Latest.Paused);
                Assert.AreEqual(2,host.CertifiedPublication.RecastColdBuilds,"two distinct public ground clearances");
                domain=Domain(runtime);ids=runtime.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player&&scenario.Any(s=>s.Kind==e.Kind&&s.Position.Equals(e.Position))).Select(e=>e.Id).OrderBy(x=>x).ToArray();Assert.AreEqual(2,ids.Length);
                Assert.True(ids.All(id=>runtime.Latest.Entities.Single(e=>e.Id==id).Kind==PlayableEntityKind.Explorer),"two source-validated small ground actors");
                Assert.AreEqual(4,domain.Navigation.Crowd.Units.Count,"two movers plus two default parked Explorers");
                foreach(int id in ids){var actor=domain.Navigation.Crowd.Units.Single(a=>a.Id==id);Assert.AreEqual("lower",actor.Location.SurfaceId);Assert.True(runtime.NavigationGeometry.IsFree(actor.Position,actor.Radius));}
                Assert.False(map.TryLocate(Goal,0,out _),"overlapping upper goal requires explicit typed command; no first-floor fallback");
                domain.CombatStepForQa=Observe;runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume ordinary command");
                Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,1,"player-1",PlayableCommandKind.Move,ids,Goal).WithResolvedLocation(RecastLayerInputs.Goal)).Accepted);
                yield return Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1&&r.Status!=PlayableCommandStatus.Accepted),"ordinary typed Move receipt");
                var receipt=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==1&&r.Status!=PlayableCommandStatus.Accepted);Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);Interlocked.Exchange(ref issued,receipt.AppliedTick);
                phase="active ramp current save";yield return Until(()=>runtime.Latest.Paused&&Read().Any(b=>b.Selected&&b.Moving&&b.Location.SurfaceId=="ramp-support"&&b.Position.X>-1&&b.Position.X<1),"source-defined active ramp save prefix");Drain();Assert.IsEmpty(observerErrors);
                var prefix=Read();Assert.True(prefix.Where(b=>b.Selected).All(b=>b.Installed>0&&b.Spine!=null),"all typed ready decisions installed before active current save");Assert.True(prefix.Any(b=>b.Selected&&b.Moving),"save contains actual unfinished movement");
                firstHost=host.CertifiedPublication;Assert.AreEqual(1,firstHost.RecastLongQueries,"one long query for compatible ground cohort");Assert.AreEqual(2,firstHost.RecastAttachments);Assert.AreEqual(1,firstHost.RecastFoundationBuilds);Assert.Greater(firstHost.RecastFoundationReuses,0);
                var capture=runtime.RequestCaptureBytes();yield return Until(()=>capture.IsCompleted,"current format capture");Assert.IsFalse(capture.IsFaulted,capture.Exception?.ToString());Assert.IsFalse(capture.IsCanceled);saved=capture.Result;
                using(var envelope=PlayableWorldState.Decode(saved)){Assert.AreEqual(PlayableWorldState.CurrentVersion,envelope.Version);Assert.AreEqual(Seed,envelope.Seed);Assert.AreEqual(Generation,envelope.Generation);Assert.AreEqual(config.SourceIdentity,envelope.SourceIdentity);CollectionAssert.AreEqual(WorldWire.Binding(profile),envelope.Binding);CollectionAssert.AreEqual(OfflineConfigurationWire.Binding(config,profile),envelope.Participants);savedTick=envelope.Tick;}
                File.WriteAllBytes(Path.Combine(output,"layer-current.world"),saved);domain.CombatStepForQa=null;firstCensus=Cleanup(runtime,host,domain);runtime=null;host=null;domain=null;
                phase="restored same match";runtime=PlayableRuntime.RestoreBytes(saved,config);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest.Paused&&host.CertifiedPublication?.RecastFoundationBuilds==1,"restored public native preparation");domain=Domain(runtime);
                Assert.AreEqual(savedTick,runtime.Latest.Tick,"restore preparation never advances live time");Assert.True(runtime.Latest.Paused);var restored=Read();Assert.AreEqual(prefix.Length,restored.Length);
                foreach(var expected in prefix){var actual=restored.Single(b=>b.Id==expected.Id);Assert.AreEqual(expected.Position,actual.Position);Assert.AreEqual(expected.Location,actual.Location);Assert.AreEqual(expected.Height,actual.Height);Assert.AreEqual(expected.Goal,actual.Goal);Assert.AreEqual(expected.Outcome,actual.Outcome);Assert.AreEqual(expected.Installed,actual.Installed);Assert.AreEqual(expected.Spine,actual.Spine);Assert.True(expected.Progress.Equals(actual.Progress),"authoritative Recast progress restored");}
                Assert.Zero(host.CertifiedPublication.RecastLongQueries,"ready decisions restore without route recompute");Volatile.Write(ref pauseForSave,0);domain.CombatStepForQa=Observe;runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume saved movement");
                while(Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest.Failure);Drain();var last=observed.LastOrDefault();arrived=last!=null&&last.Bodies.Where(b=>b.Selected).All(b=>b.Outcome==NavigationOutcome.Arrived);if(arrived||last!=null&&last.Tick-issued>=Horizon||!observerErrors.IsEmpty)break;yield return null;}
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"terminal evidence pause");Drain();phase="safety and arrival";Frame previous=null;var geometry=runtime.NavigationGeometry;
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
                Assert.IsEmpty(observerErrors);Assert.IsEmpty(failures);Assert.True(arrived,"both typed actors naturally Arrived by1800 ticks");Assert.GreaterOrEqual(firstMotion,issued);
                foreach(int id in ids){var trail=observed.SelectMany(f=>f.Bodies.Where(b=>b.Id==id)).ToArray();Assert.True(trail.Any(b=>b.Location.SurfaceId=="ramp-support"),"actual ramp "+id);Assert.True(trail.Any(b=>b.Location.SurfaceId=="upper"),"actual elevated deck "+id);var final=trail.Last();double x=final.Position.X-final.Goal.X,z=final.Position.Z-final.Goal.Z;Assert.LessOrEqual(Math.Sqrt(x*x+z*z),profile.Navigation.ArrivalTolerance,"own assigned slot "+id);}
                lastHost=host.CertifiedPublication;Assert.Zero(lastHost.RecastLongQueries,"saved ready routes remain installed");complete=true;phase="complete";
            }finally{
                Drain();try{if(runtime!=null)lastCensus=Cleanup(runtime,host,domain);}catch(Exception error){cleanupErrors.Add(error.ToString());}
                try{TestMatchLogSession.Shutdown();}catch(Exception error){cleanupErrors.Add(error.ToString());}
                File.WriteAllText(Path.Combine(output,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(observed));
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Result=complete&&cleanupErrors.Count==0?"PASS":"FAIL",phase,Seed,Generation,Horizon,SavePrefix="first moving actor on ramp-support with -1<x<1",Goal,Scenario=scenario,Ids=ids,Issued=issued,FirstMotion=firstMotion,SavedTick=savedTick,Arrived=arrived,FirstHost=firstHost,LastHost=lastHost,Failures=failures,ObserverErrors=observerErrors.ToArray(),CleanupFailures=cleanupErrors,FirstCensus=firstCensus,LastCensus=lastCensus,NativeBefore=nativeBefore,NativeAfter=NativeAllocations(),ReleaseFaults=ReleaseFaults(),Candidate=Environment.GetEnvironmentVariable("RECAST_LAYER_CANDIDATE")},Newtonsoft.Json.Formatting.Indented));
            }
            Assert.IsEmpty(cleanupErrors);Assert.AreEqual(nativeBefore,NativeAllocations());Assert.Zero(ReleaseFaults());
        }
    }
}
