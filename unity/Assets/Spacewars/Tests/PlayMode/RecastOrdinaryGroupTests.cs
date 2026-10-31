using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class RecastOrdinaryGroupTests
    {
        private const int Seed=19092026,Horizon=2700;
        private static readonly NavPoint Goal=new NavPoint(-46,46);
        private sealed class Body{public int Id;public long Incarnation;public NavPoint Position,Goal;public NavLocation Location;public double Radius,Speed,Turn,Heading,Height;public NavigationOutcome Outcome;public bool Moving;public long Installed,Group;public bool AllSlots;public PacingBody Pacing;}
        private sealed class Frame{public long Tick;public Body[] Bodies;public int ProbeActor;public object MotionProbe,RecastSweep;}
        private sealed class PacingBody{public int GlobalIndex,GlobalLength,LocalIndex,LocalLength,NoProgress;public NavPoint? GlobalTarget,LocalTarget;public double BlockedSeconds,Anchor,Stretch,Progress,Offset;public long ProgressRequest;public int MarchVersion;public bool MarchInitialized,CanYield;public string TypedFrom,TypedTo,TypedPortal,TypedKind;public long CorridorRequest;public int RecastVersion,RecastPhase,RecastCursor;public double RecastStation,RecastHighWater;public long RecastRequest;public bool RecastMapped;}
        [UnityTest,Timeout(240000)]
        public IEnumerator TenTanksUseOneWorkerCorridorAroundWallAndUpRamp()
        {
            string output=Environment.GetEnvironmentVariable("RECAST_ORDINARY_OUTPUT")??System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/recast-ordinary"));
            System.IO.Directory.CreateDirectory(output);
            Assert.True(NativeSilentStartup.IsSilent);TestMatchLogSession.StartDefault(System.IO.Path.Combine(output,"TestLogs"));Assert.IsNotNull(TestMatchLogSession.CurrentDirectory);
            bool diagnostic=Environment.GetEnvironmentVariable("RECAST_PACING_DIAGNOSTIC")=="1";
            var routeIndexField=typeof(NavUnit).GetField("routeIndex",BindingFlags.Instance|BindingFlags.NonPublic);
            var localIndexField=typeof(NavUnit).GetField("localIndex",BindingFlags.Instance|BindingFlags.NonPublic);
            var localRouteField=typeof(NavUnit).GetField("localRoute",BindingFlags.Instance|BindingFlags.NonPublic);
            var noProgressField=typeof(NavUnit).GetField("noProgressTicks",BindingFlags.Instance|BindingFlags.NonPublic);
            var blockedField=typeof(NavUnit).GetField("blockedSeconds",BindingFlags.Instance|BindingFlags.NonPublic);
            var probeActorField=typeof(NavigationSession).GetField("MovementDelayActor",BindingFlags.Instance|BindingFlags.NonPublic);
            var probeField=typeof(NavigationSession).GetField("MovementDelayComputation",BindingFlags.Instance|BindingFlags.NonPublic);
            var recastProbeField=typeof(NavigationSession).GetField("MovementDelayRecastReceipt",BindingFlags.Instance|BindingFlags.NonPublic);
            var knownRecastPlans=new Dictionary<long,object>();
            var knownCorridors=new Dictionary<long,NavCorridorDescriptor>();int probeActor=0;
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=(ThreeCrossingsMap)profile.AuthoredMap;
            var baseConfig=map.Configuration(profile,Seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var scenario=(from x in new[]{-27.25d,-24.25d} from z in new[]{42d,44d,46d,48d,50d} select new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(x,z))).ToArray();
            var costs=new double[4,4];for(int i=0;i<4;i++)for(int j=0;j<4;j++)costs[i,j]=baseConfig.RouteCost(i,j);
            var config=new OfflineMatchConfiguration(profile,baseConfig.SourceIdentity,baseConfig.MapIdentity,baseConfig.RouteProvenance,Seed,baseConfig.Roster.ToArray(),baseConfig.Starts.ToArray(),baseConfig.Sites.ToArray(),baseConfig.Obstacles.ToArray(),costs,baseConfig.Spectators.ToArray(),scenario,map.Id,baseConfig.ScenarioBuildings.ToArray());
            PlayableRuntime runtime=null;UnityHostRouteService host=null;PlayableDomain domain=null;var frames=new ConcurrentQueue<Frame>();var observed=new List<Frame>();var failures=new List<string>();var observerErrors=new ConcurrentQueue<string>();
            int[] ids=Array.Empty<int>();long issued=-1,firstMotion=-1;bool arrived=false;double deadline=Time.realtimeSinceStartupAsDouble+180;
            IEnumerator Until(Func<bool> done,string phase){while(!done()&&Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest?.Failure,phase);yield return null;}Assert.True(done(),phase);}
            void Drain(){while(frames.TryDequeue(out var frame))observed.Add(frame);}
            try{
                runtime=new PlayableRuntime(config,801,startPaused:true);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest!=null,"genesis");
                ids=runtime.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).OrderBy(x=>x).ToArray();Assert.AreEqual(10,ids.Length);
                domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);var nav=domain.Navigation;var geometry=runtime.NavigationGeometry;
                foreach(int id in ids){Assert.True(nav.Crowd.TryGet(id,out var body));Assert.AreEqual("bank/west",body.Location.SurfaceId);Assert.True(geometry.IsFree(body.Position,body.Radius));}
                Assert.AreEqual(1,map.Supports.Single(s=>s.SurfaceId=="deck/-1/1").Height);Assert.True(map.TryLocate(Goal,profile.TankCollisionRadius,out var goal));Assert.AreEqual("deck/-1/1",goal.SurfaceId);
                Assert.True(ids.Any(id=>{nav.Crowd.TryGet(id,out var body);return !geometry.SegmentFree(body.Position,Goal,body.Radius);}),"real static wall obstructs a direct start-to-target segment");
                if(diagnostic){probeActor=ids[0];probeActorField.SetValue(nav,probeActor);}
                PacingBody CapturePacing(NavUnit actor,GroupOrderState group){
                    if(!diagnostic)return null;int gi=(int)routeIndexField.GetValue(actor),li=(int)localIndexField.GetValue(actor);var local=(NavPoint[])localRouteField.GetValue(actor);var member=group?.Members.SingleOrDefault(m=>m.Entity==actor.Id);
                    NavCorridorDescriptor corridor=null;if(actor.InstalledRequest>0&&!knownCorridors.TryGetValue(actor.InstalledRequest,out corridor)){corridor=nav.InstalledExecutionFor(actor.Id)?.Corridor;if(corridor!=null){knownCorridors.Add(actor.InstalledRequest,corridor);knownRecastPlans.Add(actor.InstalledRequest,corridor.CopyRecastDecisionEvidence());}}
                    var leg=corridor?.Legs.Where(l=>l.Kind==NavTypedLegKind.Surface).Skip(gi).FirstOrDefault();
                    return new PacingBody{RecastMapped=member?.Recast.Request>0&&member.Recast.Phase!=RecastMovementPhase.UnmappedProduction,RecastVersion=member?.Recast.Request>0?2:0,RecastRequest=member?.Recast.Request??0,RecastPhase=(int)(member?.Recast.Phase??RecastMovementPhase.Origin),RecastCursor=member?.Recast.Cursor??0,RecastStation=member?.Recast.Station??0,RecastHighWater=member?.Recast.HighWater??0,GlobalIndex=gi,GlobalLength=actor.Route.Count,LocalIndex=li,LocalLength=local.Length,GlobalTarget=gi<actor.Route.Count?(NavPoint?)actor.Route[gi]:null,LocalTarget=li<local.Length?(NavPoint?)local[li]:null,NoProgress=(int)noProgressField.GetValue(actor),BlockedSeconds=(double)blockedField.GetValue(actor),Anchor=group?.MarchAnchor??0,Stretch=group?.MarchStretch??0,Progress=member?.Progress??0,Offset=member?.ProgressOffset??0,ProgressRequest=member?.ProgressRequest??0,MarchVersion=group?.MarchVersion??0,MarchInitialized=group?.MarchInitialized??false,CanYield=actor.CanYield,CorridorRequest=corridor?.Request??0,TypedFrom=leg?.From.SurfaceId,TypedTo=leg?.To.SurfaceId,TypedPortal=leg?.PortalId,TypedKind=leg?.Kind.ToString()};
                }
                domain.CombatStepForQa=()=>{try{
                    if(frames.Count+observed.Count>Horizon+64){observerErrors.Enqueue("frame capacity exceeded");runtime.RequestPause(true);return;}
                    frames.Enqueue(new Frame{Tick=domain.Tick,ProbeActor=diagnostic?probeActor:0,MotionProbe=diagnostic?probeField.GetValue(nav):null,RecastSweep=diagnostic?recastProbeField.GetValue(nav):null,Bodies=nav.Crowd.Units.Where(a=>ids.Contains(a.Id)).Select(a=>{var group=nav.GroupFor(a.Id);return new Body{Id=a.Id,Incarnation=a.Incarnation,Position=a.Position,Location=a.Location,Goal=a.Goal,Radius=a.Radius,Speed=a.Speed,Turn=a.TurnSpeed,Heading=a.Heading,Height=map.SurfaceHeight(a.Position),Outcome=a.Outcome,Moving=a.Moving,Installed=a.InstalledRequest,Group=group?.GroupId??0,AllSlots=group!=null&&group.Members.Length==10&&group.Members.All(m=>m.HasGoal&&m.GoalLocation.HasValue),Pacing=CapturePacing(a,group)};}).ToArray()});
                    if(diagnostic){probeActor=ids[(int)(domain.Tick%ids.Length)];probeActorField.SetValue(nav,probeActor);}
                    long at=System.Threading.Interlocked.Read(ref issued);if(at>=0&&domain.Tick-at>=Horizon)runtime.RequestPause(true);
                }catch(Exception error){observerErrors.Enqueue(error.ToString());runtime.RequestPause(true);}};
                runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume");
                var submitted=runtime.TrySubmit(new PlayableCommand(801,1,"player-1",PlayableCommandKind.Move,ids,Goal));Assert.True(submitted.Accepted);
                yield return Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1&&r.Status!=PlayableCommandStatus.Accepted),"ordinary Move receipt");
                var receipt=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==1&&r.Status!=PlayableCommandStatus.Accepted);Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);System.Threading.Interlocked.Exchange(ref issued,receipt.AppliedTick);
                while(Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest.Failure);Drain();var last=observed.LastOrDefault();arrived=last!=null&&last.Bodies.Length==10&&last.Bodies.All(b=>b.Outcome==NavigationOutcome.Arrived);if(arrived||last!=null&&last.Tick-issued>=Horizon||!observerErrors.IsEmpty)break;yield return null;}
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"pause evidence");Drain();Frame previous=null;
                foreach(var frame in observed){foreach(var body in frame.Bodies){
                    if(!map.IsValidLocation(body.Location,body.Radius)||!geometry.IsFree(body.Position,body.Radius))failures.Add("footprint/layer "+body.Id+"@"+frame.Tick);
                    if(body.Moving&&!body.AllSlots)failures.Add("movement before all final slots "+body.Id);
                    if(body.Outcome==NavigationOutcome.Rejected||body.Outcome==NavigationOutcome.Blocked||body.Outcome==NavigationOutcome.Unreachable)failures.Add("terminal failure "+body.Id+"@"+frame.Tick);
                    if(previous!=null){var old=previous.Bodies.Single(b=>b.Id==body.Id);double dx=body.Position.X-old.Position.X,dz=body.Position.Z-old.Position.Z;
                        if(dx*dx+dz*dz>body.Speed*body.Speed/900+1e-9)failures.Add("teleport/speed "+body.Id);
                        if(!geometry.SegmentFree(old.Position,body.Position,body.Radius)||!map.TryTraverse(old.Location,body.Position,body.Radius,out var at)||!at.Equals(body.Location))failures.Add("sweep/layer "+body.Id);
                        if(dx*dx+dz*dz>1e-12&&firstMotion<0)firstMotion=frame.Tick;
                        foreach(var peer in frame.Bodies.Where(b=>b.Id<body.Id)){var oldPeer=previous.Bodies.Single(b=>b.Id==peer.Id);double sx=old.Position.X-oldPeer.Position.X,sz=old.Position.Z-oldPeer.Position.Z,vx=dx-(peer.Position.X-oldPeer.Position.X),vz=dz-(peer.Position.Z-oldPeer.Position.Z),q=vx*vx+vz*vz,t=q==0?0:Math.Max(0,Math.Min(1,-(sx*vx+sz*vz)/q)),x=sx+vx*t,z=sz+vz*t;if(x*x+z*z<Math.Pow(body.Radius+peer.Radius,2)-1e-8)failures.Add("body sweep "+body.Id+"/"+peer.Id);}
                    }
                }previous=frame;}
                Assert.IsEmpty(observerErrors);Assert.IsEmpty(failures);Assert.True(arrived,"all ten naturally Arrived within2700 ticks");Assert.GreaterOrEqual(firstMotion,issued);
                foreach(int id in ids){var trail=observed.SelectMany(f=>f.Bodies.Where(b=>b.Id==id)).ToArray();Assert.True(trail.Any(b=>b.Location.SurfaceId=="ramp/-1/1/x"),"actual ramp "+id);Assert.True(trail.Any(b=>b.Height>=1&&b.Location.SurfaceId=="deck/-1/1"),"actual elevated deck "+id);var final=trail.Last();double gx=final.Position.X-final.Goal.X,gz=final.Position.Z-final.Goal.Z;Assert.LessOrEqual(Math.Sqrt(gx*gx+gz*gz),profile.Navigation.ArrivalTolerance);}
                Assert.AreEqual(1,host.CertifiedPublication.RecastLongQueries,"one long search per compatible subgroup");Assert.AreEqual(10,host.CertifiedPublication.RecastAttachments);
            }finally{
                Drain();if(domain!=null)domain.CombatStepForQa=null;
                if(diagnostic){System.IO.File.WriteAllText(System.IO.Path.Combine(output,"corridors.json"),Newtonsoft.Json.JsonConvert.SerializeObject(knownCorridors));System.IO.File.WriteAllText(System.IO.Path.Combine(output,"plans.json"),Newtonsoft.Json.JsonConvert.SerializeObject(knownRecastPlans));}
                System.IO.File.WriteAllText(System.IO.Path.Combine(output,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(observed));
                System.IO.File.WriteAllText(System.IO.Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Seed,Horizon,Goal,Ids=ids,Issued=issued,FirstMotion=firstMotion,Arrived=arrived,Failures=failures,ObserverErrors=observerErrors.ToArray(),Host=host?.CertifiedPublication},Newtonsoft.Json.Formatting.Indented));
                runtime?.RequestStop();host?.Dispose();runtime?.Dispose();TestMatchLogSession.Shutdown();
            }
        }
    }
}
