using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class RecastGroupObstacleTests
    {
        private const int Seed=19092026,Horizon=1800;
        private const long Generation=809;
        private sealed class Body
        {
            public int Id;public NavPoint Position,Goal;public NavLocation Location;
            public double Radius,Speed;public bool Held,Moving;public NavigationOutcome Outcome;
            public long Group,OrderRevision;public NavLocation? Assigned;public NavPoint[] Route;
        }
        private sealed class Frame {public long Tick;public int Geometry;public Body[] Bodies;}
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static readonly PropertyInfo UnitStorage=typeof(NavUnit).GetProperty("StorageOwner",Hidden);
        private static readonly FieldInfo InstalledOwner=UnitStorage.PropertyType.GetField("InstalledExecution",Hidden);
        private static NavCorridorDescriptor OwnedCorridor(NavUnit actor)
        {
            var storage=UnitStorage.GetValue(actor);
            return (storage==null?null:InstalledOwner.GetValue(storage) as NavigationInstalledExecutionOwner)?.Corridor?.Corridor;
        }
        [UnityTest,Timeout(240000)] public IEnumerator GroupKeepsOriginalSlotsAroundBuildingOrderedAfterFirstMotion()=>Run(false);
        [UnityTest,Timeout(240000)] public IEnumerator GroupKeepsOriginalSlotsAroundStationaryAlliedHold()=>Run(true);

        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
        private static double SegmentDistance(NavPoint a,NavPoint b,NavPoint p)
        {
            double x=b.X-a.X,z=b.Z-a.Z,q=x*x+z*z,t=q==0?0:Math.Max(0,Math.Min(1,((p.X-a.X)*x+(p.Z-a.Z)*z)/q));
            return Distance(new NavPoint(a.X+x*t,a.Z+z*t),p);
        }
        private IEnumerator Run(bool hold)
        {
            string root=Environment.GetEnvironmentVariable("RECAST_OBSTACLE_OUTPUT")??throw new InvalidOperationException("Unique output root required");
            string output=Path.Combine(root,hold?"hold":"building");Assert.False(Directory.Exists(output));Directory.CreateDirectory(output);
            Assert.True(NativeSilentStartup.IsSilent);
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=(ThreeCrossingsMap)profile.AuthoredMap;
            var genesis=map.Configuration(profile,Seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,1,OfflineControl.Human),new OfflineParticipant("player-3",3,2,OfflineControl.Human)});
            var site=genesis.Sites.Single(s=>s.Id==genesis.Starts.Single(s=>s.PinnedLogicalPlayer==1).SiteId);var slot=site.Slots.Single(s=>s.Id==1);var center=slot.Position;
            var goal=new NavPoint(center.X,center.Z+7);
            // Initial scenario placement preserves ordinary reserved building pads.
            var heldPoint=new NavPoint(center.X,center.Z+3);
            var scenario=(from x in new[]{-1.8,0,1.8} from z in new[]{-7d,-9d} select new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(center.X+x,center.Z+z),Math.PI/2)).ToList();
            if(hold)scenario.Add(new OfflineScenarioUnit(2,PlayableEntityKind.Tank,heldPoint));
            var config=RecastFormationInputs.WithScenario(profile,genesis,scenario.ToArray());
            TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            PlayableRuntime runtime=null;UnityHostRouteService host=null;PlayableDomain domain=null;
            var frames=new ConcurrentQueue<Frame>();var errors=new ConcurrentQueue<string>();var observed=new List<Frame>();
            int[] ids=Array.Empty<int>();int heldId=0,buildingId=0,pauseAtMotion=1;long issued=-1,firstMotion=-1,obstacleTick=-1;
            bool arrived=false,complete=false;string phase="startup";Frame initial=null,previous=null;object cleanup=null,lastHost=null;
            Dictionary<int,NavLocation> original=null;double deadline=Time.realtimeSinceStartupAsDouble+180;
            var submittedCommands=new List<PlayableCommand>();
            void Submit(PlayableCommand command){submittedCommands.Add(command);var result=runtime.TrySubmit(command);Assert.True(result.Accepted,command.Kind+" ingress: "+result.Status);}
            void Drain(){while(frames.TryDequeue(out var frame))observed.Add(frame);}
            IEnumerator Until(Func<bool> done,string name)
            {
                while(!done()&&Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);if(runtime.Latest?.Failure!=null){errors.Enqueue(name+": "+runtime.Latest.Failure);yield break;}yield return null;}
                if(!done())errors.Enqueue("deadline: "+name);
            }
            void Check(){Assert.IsEmpty(errors);}
            PlayableCommandReceipt Receipt(long seq,string player)=>runtime.ReceiptsAfter(0).FirstOrDefault(r=>r.Sequence==seq&&r.OwnerId==player&&r.Status!=PlayableCommandStatus.Accepted);
            void Observe()
            {
                try{
                    var nav=domain.Navigation;var geometry=domain.Geometry;
                    var frame=new Frame{Tick=domain.Tick,Geometry=geometry.Revision,Bodies=nav.Crowd.Units.Select(a=>{
                        var group=nav.GroupFor(a.Id);var member=group?.Members.SingleOrDefault(m=>m.Entity==a.Id);
                        return new Body{Id=a.Id,Position=a.Position,Goal=a.Goal,Location=a.Location,Radius=a.Radius,Speed=a.Speed,Held=a.Held,Moving=a.Moving,Outcome=a.Outcome,Group=group?.GroupId??0,OrderRevision=group?.OrderRevision??0,Assigned=member?.GoalLocation,Route=ids.Contains(a.Id)&&initial==null?(OwnedCorridor(a)?.ProjectedRoute()??a.Route.ToArray()):null};
                    }).ToArray()};
                    foreach(var b in frame.Bodies){
                        if(!geometry.IsFree(b.Position,b.Radius)||!map.IsValidLocation(b.Location,b.Radius))errors.Enqueue("footprint/layer "+b.Id+"@"+frame.Tick);
                        if(b.Id==heldId&&(!b.Held||!b.Position.Equals(heldPoint)||b.Moving))errors.Enqueue("HOLD moved/released @"+frame.Tick);
                        if(ids.Contains(b.Id)){
                            var group=nav.GroupFor(b.Id);
                            if(b.Moving&&(group==null||group.Members.Length!=ids.Length||group.Members.Any(m=>!m.HasGoal||!m.GoalLocation.HasValue)))errors.Enqueue("motion before final slots "+b.Id);
                            if(original!=null&&(!b.Assigned.HasValue||!b.Assigned.Value.Equals(original[b.Id])))errors.Enqueue("initial slot changed "+b.Id+"@"+frame.Tick);
                            if(b.Outcome==NavigationOutcome.Rejected||b.Outcome==NavigationOutcome.Blocked||b.Outcome==NavigationOutcome.Unreachable)errors.Enqueue("terminal failure "+b.Id+"@"+frame.Tick);
                        }
                        if(previous==null)continue;var old=previous.Bodies.Single(p=>p.Id==b.Id);
                        if(Distance(old.Position,b.Position)>b.Speed/30+1e-8)errors.Enqueue("speed/teleport "+b.Id+"@"+frame.Tick);
                        if(!geometry.SegmentFree(old.Position,b.Position,b.Radius)||!map.TryTraverse(old.Location,b.Position,b.Radius,out var at)||!at.Equals(b.Location))errors.Enqueue("continuous geometry/layer sweep "+b.Id+"@"+frame.Tick);
                        foreach(var peer in frame.Bodies.Where(p=>p.Id<b.Id)){
                            var oldPeer=previous.Bodies.Single(p=>p.Id==peer.Id);
                            var relativeFrom=new NavPoint(old.Position.X-oldPeer.Position.X,old.Position.Z-oldPeer.Position.Z);
                            var relativeTo=new NavPoint(b.Position.X-peer.Position.X,b.Position.Z-peer.Position.Z);
                            if(SegmentDistance(relativeFrom,relativeTo,default)<b.Radius+peer.Radius-1e-8)errors.Enqueue("continuous peer sweep "+b.Id+"/"+peer.Id+"@"+frame.Tick);
                        }
                    }
                    if(initial==null&&ids.All(id=>frame.Bodies.Single(b=>b.Id==id).Assigned.HasValue)&&frame.Bodies.Where(b=>ids.Contains(b.Id)).Any(b=>b.Moving)){
                        initial=frame;original=frame.Bodies.Where(b=>ids.Contains(b.Id)).ToDictionary(b=>b.Id,b=>b.Assigned.Value);
                    }
                    if(initial!=null&&firstMotion<0&&ids.Any(id=>Distance(frame.Bodies.Single(b=>b.Id==id).Position,scenario[Array.IndexOf(ids,id)].Position)>1e-5)){
                        Interlocked.Exchange(ref firstMotion,frame.Tick);if(Volatile.Read(ref pauseAtMotion)==1)runtime.RequestPause(true);
                    }
                    frames.Enqueue(frame);previous=frame;
                    if(Interlocked.Read(ref issued)>=0&&frame.Tick-issued>=Horizon||!errors.IsEmpty)runtime.RequestPause(true);
                }catch(Exception e){errors.Enqueue(e.ToString());runtime.RequestPause(true);}
            }
            try{
                runtime=new PlayableRuntime(config,Generation,startPaused:true);host=new UnityHostRouteService();
                yield return Until(()=>runtime.IsPrepared&&runtime.Latest!=null,"ordinary prepared runtime");Check();
                ids=runtime.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).OrderBy(id=>id).ToArray();Assert.AreEqual(6,ids.Length);
                runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume command ingress");Check();
                if(hold){heldId=runtime.Latest.Entities.Single(e=>e.Owner==(PlayableOwner)1&&e.Kind==PlayableEntityKind.Tank).Id;
                    Submit(new PlayableCommand(Generation,1,"player-2",PlayableCommandKind.Hold,new[]{heldId}).AsHuman());
                    yield return Until(()=>Receipt(1,"player-2")!=null,"allied HOLD receipt");Check();Assert.AreEqual(PlayableCommandStatus.Applied,Receipt(1,"player-2").Status);
                }
                domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
                foreach(int id in ids){Assert.True(domain.Navigation.Crowd.TryGet(id,out var body));Assert.True(domain.Geometry.IsFree(body.Position,body.Radius));Assert.True(map.IsValidLocation(body.Location,body.Radius));}
                domain.CombatStepForQa=Observe;
                Submit(new PlayableCommand(Generation,1,"player-1",PlayableCommandKind.Move,ids,goal).AsHuman());
                yield return Until(()=>Receipt(1,"player-1")!=null,"single group Move receipt");Check();Assert.AreEqual(PlayableCommandStatus.Applied,Receipt(1,"player-1").Status);issued=Receipt(1,"player-1").AppliedTick;
                yield return Until(()=>runtime.Latest.Paused&&Interlocked.Read(ref firstMotion)>=0,"first motion and initial slots");Check();Drain();Assert.NotNull(initial);Assert.AreEqual(6,original.Count);
                Assert.True(initial.Bodies.Where(b=>ids.Contains(b.Id)).Any(b=>b.Route.Length>0),"ordinary host installed a corridor before first motion");
                Assert.True(initial.Bodies.Where(b=>ids.Contains(b.Id)&&b.Moving).All(b=>b.Route.Length>0),"every moving member has an installed corridor");
                Volatile.Write(ref pauseAtMotion,0);runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"resume after initial slots");Check();
                if(!hold){
                    phase="build after motion";int parent=runtime.Latest.Buildings.Single(b=>b.SiteId==site.Id&&b.Kind==PlayableBuildingKind.Headquarters).Id;
                    Submit(new PlayableCommand(Generation,2,"player-1",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:site.Id,slotId:slot.Id,parentId:parent,buildingKind:PlayableBuildingKind.Factory).AsHuman());
                    yield return Until(()=>Receipt(2,"player-1")!=null,"real BuildAt receipt");Check();Assert.AreEqual(PlayableCommandStatus.Applied,Receipt(2,"player-1").Status,Receipt(2,"player-1").Message);
                    yield return Until(()=>runtime.Latest.Buildings.Any(b=>b.SiteId==site.Id&&b.SlotId==slot.Id),"published building snapshot");Check();
                    buildingId=runtime.Latest.Buildings.Single(b=>b.SiteId==site.Id&&b.SlotId==slot.Id).Id;
                }
                Volatile.Write(ref pauseAtMotion,0);runtime.RequestPause(false);phase="natural detour and arrival";
                while(Time.realtimeSinceStartupAsDouble<deadline){
                    host.Service(runtime,8);Assert.IsNull(runtime.Latest.Failure);Drain();
                    if(hold&&obstacleTick<0)obstacleTick=issued;
                    if(!hold&&obstacleTick<0&&runtime.Latest.Buildings.Any(b=>b.Id==buildingId&&b.Phase!=ConstructionPhase.Pending))obstacleTick=runtime.Latest.Tick;
                    var last=observed.LastOrDefault();arrived=last!=null&&ids.All(id=>last.Bodies.Single(b=>b.Id==id).Outcome==NavigationOutcome.Arrived);
                    if(arrived||last!=null&&last.Tick-issued>=Horizon||!errors.IsEmpty)break;yield return null;
                }
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"evidence pause");Check();Drain();
                Assert.GreaterOrEqual(obstacleTick,0);Assert.Greater(host.CertifiedPublication.Pumps,0);Assert.True(domain.Navigation.HasCertifiedHost);
                int blocked=0;
                foreach(var body in initial.Bodies.Where(b=>ids.Contains(b.Id))){var from=body.Position;
                    if(hold){
                        // HOLD exists before Move: the installed corridor may already detour.
                        // Prove that it obstructs an otherwise terrain/geometry-valid direct path.
                        if(domain.Geometry.SegmentFree(from,original[body.Id].Position,body.Radius)&&map.TryTraverse(body.Location,original[body.Id].Position,body.Radius,out var direct)&&direct.Equals(original[body.Id])&&SegmentDistance(from,original[body.Id].Position,heldPoint)<body.Radius+profile.TankCollisionRadius)blocked++;
                    }else foreach(var to in body.Route){if(!domain.Geometry.SegmentFree(from,to,body.Radius)){blocked++;break;}from=to;}
                }
                Assert.Greater(blocked,0,hold?"HOLD obstructs an otherwise valid direct path":"new building intersects an installed pre-construction corridor");
                Assert.True(arrived,"all six naturally Arrived at original slots within1800 ticks");
                var lastFrame=observed.Last();foreach(int id in ids){var body=lastFrame.Bodies.Single(b=>b.Id==id);Assert.AreEqual(original[id],body.Assigned);Assert.LessOrEqual(Distance(body.Position,original[id].Position),profile.Navigation.ArrivalTolerance);}
                Assert.AreEqual(1,submittedCommands.Count(c=>c.Kind==PlayableCommandKind.Move),"no second Move");
                Assert.AreEqual(submittedCommands.Count,runtime.ReceiptsAfter(0).Count(r=>r.Status==PlayableCommandStatus.Applied));
                File.WriteAllText(Path.Combine(output,"obstruction.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{BlockedInitialRoutes=blocked,Witness=hold?"direct path valid without the stationary HOLD body":"installed corridor before construction",Center=center,HeldPoint=heldPoint,BuildingId=buildingId,HeldId=heldId,ObstacleTick=obstacleTick,Initial=initial,OriginalSlots=original},Newtonsoft.Json.Formatting.Indented));
                complete=true;phase="complete";
            }finally{
                Drain();if(domain!=null)domain.CombatStepForQa=null;
                lastHost=host?.CertifiedPublication;
                try{if(runtime!=null){
                    var worker=host==null?null:(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",Hidden).GetValue(host);
                    runtime.RequestStop();host?.Dispose();runtime.Dispose();
                    Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000),"runtime and route-worker stop ACK");
                    if(domain!=null){domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();var b=domain.Navigation.PhysicalHostBudget;cleanup=new{b.PhysicalBytes,b.HeldBytes,b.ActiveScopes};Assert.Zero(b.PhysicalBytes);Assert.Zero(b.HeldBytes);Assert.Zero(b.ActiveScopes);}
                }}catch(Exception e){errors.Enqueue("cleanup: "+e);}
                try{TestMatchLogSession.Shutdown();}catch(Exception e){errors.Enqueue("journal shutdown: "+e);}
                File.WriteAllText(Path.Combine(output,"initial.json"),Newtonsoft.Json.JsonConvert.SerializeObject(initial,Newtonsoft.Json.Formatting.Indented));
                File.WriteAllText(Path.Combine(output,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(observed));
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Result=complete&&errors.IsEmpty?"PASS":"FAIL",phase,Seed,Horizon,Generation,Ids=ids,Hold=hold,Center=center,HeldPoint=heldPoint,Goal=goal,Issued=issued,FirstMotion=firstMotion,ObstacleTick=obstacleTick,BuildingId=buildingId,HeldId=heldId,Arrived=arrived,OriginalSlots=original,Commands=submittedCommands,Receipts=runtime?.ReceiptsAfter(0),Errors=errors.ToArray(),Host=lastHost,Cleanup=cleanup,Candidate=Environment.GetEnvironmentVariable("RECAST_OBSTACLE_CANDIDATE")},Newtonsoft.Json.Formatting.Indented));
            }
            Assert.IsEmpty(errors);
        }
    }
}
