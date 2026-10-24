using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationArrivalTerminalRuntimeTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private const int Seed=19092026,Horizon=2700;
        private const long Generation=71;
        private sealed class Row
        {
            public int Id;public NavLocation Location;public NavPoint Position,Goal,RootGoal;
            public double Radius,Speed,Turn,Heading;public bool Held,Moving,CanYield;
            public NavigationOutcome Outcome;public long Incarnation,Mobility,MemberMobility,InstalledGroup,InstalledRevision;
            public long Group,World,Command,RootRevision,Membership,StampSequence,StampRevision;
            public string Owner,Kind,Origin;public int[] Members;
        }
        private sealed class Frame {public long Tick;public Row[] Rows;public long[] ReservationGroups;}
        private static double DistanceSquared(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return x*x+z*z;}
        private static NavLocation Bare(NavLocation p)=>new NavLocation(p.Position,p.SurfaceId);

        [Test,Timeout(180000)]
        public void IndependentOriginalGroupsNaturallyArriveAroundParkedAllyAndReleaseReservations()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;var goal=new NavPoint(-16,0);
            var startsAt=new[]{new NavPoint(-18,-1.8),new NavPoint(-18,1.8),new NavPoint(-24,0)};
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=Seed};setup.InitializeParticipants();foreach(var p in setup.Participants)p.Human=true;
            var genesis=NativeLobbyMatch.Create(profile,setup);var costs=new double[genesis.Starts.Count,genesis.Starts.Count];
            for(int i=0;i<genesis.Starts.Count;i++)for(int j=0;j<genesis.Starts.Count;j++)costs[i,j]=genesis.RouteCost(i,j);
            int player=genesis.Roster[0].LogicalPlayer;string owner=genesis.Roster[0].Id;
            var scenario=new[]{new OfflineScenarioUnit(player,PlayableEntityKind.Tank,startsAt[0]),new OfflineScenarioUnit(player,PlayableEntityKind.Tank,startsAt[1]),new OfflineScenarioUnit(player,PlayableEntityKind.Explorer,startsAt[2]),new OfflineScenarioUnit(player,PlayableEntityKind.Tank,goal)};
            var config=new OfflineMatchConfiguration(profile,"a4-terminal-original-groups-v1",genesis.MapIdentity,genesis.RouteProvenance,Seed,genesis.Roster.ToArray(),genesis.Starts.ToArray(),genesis.Sites.ToArray(),genesis.Obstacles.ToArray(),costs,genesis.Spectators.ToArray(),scenario,profile.AuthoredMap.Id,genesis.ScenarioBuildings.ToArray());
            string folder=Path.GetFullPath(Environment.GetEnvironmentVariable("SPACEWARS_A4_TERMINAL_OUTPUT")??Path.Combine(TestContext.CurrentContext.WorkDirectory,"a4-terminal-"+Guid.NewGuid().ToString("N")));
            Assert.False(Directory.Exists(folder)&&Directory.EnumerateFileSystemEntries(folder).Any());Directory.CreateDirectory(folder);
            void Save(string name,object value)=>File.WriteAllText(Path.Combine(folder,name),Newtonsoft.Json.JsonConvert.SerializeObject(value,Newtonsoft.Json.Formatting.Indented));
            var runtime=new PlayableRuntime(config,Generation,startPaused:true);var host=new UnityHostRouteService();
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",Hidden).GetValue(runtime);var nav=domain.Navigation;
            var clock=Stopwatch.StartNew();var queued=new ConcurrentQueue<Frame>();var observerErrors=new ConcurrentQueue<string>();var observed=new List<Frame>();var checks=new List<string>();var cleanup=new List<string>();
            var expected=new Dictionary<int,long>();var groups=new Dictionary<long,long>();var targets=new Dictionary<int,NavPoint>();
            int[] movers=Array.Empty<int>();int parked=-1,count=0;long commandTick=-1,firstMotion=-1,previousTick=-1;bool arrived=false,sawBothReservations=false;string phase="startup";Exception primary=null;Frame previous=null;Row[] initial=null;object terminal=null,census=null;
            Row[] ReadRows()=>nav.Crowd.Units.OrderBy(a=>a.Id).Select(a=>{
                var group=nav.GroupFor(a.Id);var member=group?.Members.SingleOrDefault(m=>m.Entity==a.Id);var units=(IDictionary)typeof(PlayableDomain).GetField("units",Hidden).GetValue(domain);var unit=units[a.Id];var stamp=(PlayableOrderStamp)unit.GetType().GetField("LastOrder",Hidden).GetValue(unit);
                return new Row{Id=a.Id,Position=a.Position,Location=Bare(a.Location),Goal=a.Goal,Radius=a.Radius,Speed=a.Speed,Turn=a.TurnSpeed,Heading=a.Heading,Held=a.Held,Moving=a.Moving,CanYield=a.CanYield,Outcome=a.Outcome,Incarnation=a.Incarnation,Mobility=a.MobilityRevision,MemberMobility=member?.MobilityRevision??-1,InstalledGroup=a.InstalledGroupId,InstalledRevision=a.InstalledRevision,Group=group?.GroupId??0,World=group?.WorldGeneration??0,Command=group?.CommandSequence??0,RootRevision=group?.OrderRevision??0,Membership=group?.MembershipRevision??0,RootGoal=group?.OriginalGoal??default,Owner=group?.OwnerId,Kind=group?.Kind.ToString(),Origin=group?.Origin.ToString(),Members=group?.Members.Select(m=>m.Entity).ToArray(),StampSequence=stamp?.Sequence??0,StampRevision=stamp?.Revision??0};
            }).ToArray();
            void Audit(){while(queued.TryDequeue(out var f)){
                observed.Add(f);if(f.Tick!=previousTick+1)checks.Add("tick gap "+f.Tick);previousTick=f.Tick;
                foreach(var row in f.Rows){
                    var basis=initial.Single(r=>r.Id==row.Id);var old=previous==null?basis:previous.Rows.Single(r=>r.Id==row.Id);
                    if(row.Incarnation!=basis.Incarnation||row.Radius!=basis.Radius||row.Speed!=basis.Speed||row.Turn!=basis.Turn||row.CanYield!=basis.CanYield)checks.Add("body contract "+row.Id);
                    if(!profile.AuthoredMap.IsValidLocation(row.Location,row.Radius)||!runtime.NavigationGeometry.IsFree(row.Position,row.Radius))checks.Add("typed footprint "+row.Id);
                    double dx=row.Position.X-old.Position.X,dz=row.Position.Z-old.Position.Z;
                    if(dx*dx+dz*dz>row.Speed*row.Speed/900+1e-9)checks.Add("speed "+row.Id);
                    if(Math.Abs(Math.Atan2(Math.Sin(row.Heading-old.Heading),Math.Cos(row.Heading-old.Heading)))>row.Turn/30+1e-9)checks.Add("turn "+row.Id);
                    if(!runtime.NavigationGeometry.SegmentFree(old.Position,row.Position,row.Radius)||!profile.AuthoredMap.SupportsSweep(old.Position,row.Position,row.Radius)||!profile.AuthoredMap.TryTraverse(old.Location,row.Position,row.Radius,out var typed)||!typed.Equals(row.Location))checks.Add("typed sweep "+row.Id);
                    foreach(var peer in f.Rows.Where(r=>r.Id<row.Id)){var oldPeer=previous==null?initial.Single(r=>r.Id==peer.Id):previous.Rows.Single(r=>r.Id==peer.Id);double sx=old.Position.X-oldPeer.Position.X,sz=old.Position.Z-oldPeer.Position.Z,vx=dx-(peer.Position.X-oldPeer.Position.X),vz=dz-(peer.Position.Z-oldPeer.Position.Z),q=vx*vx+vz*vz,t=q==0?0:Math.Max(0,Math.Min(1,-(sx*vx+sz*vz)/q)),x=sx+vx*t,z=sz+vz*t;if(x*x+z*z<Math.Pow(row.Radius+peer.Radius,2)-1e-8)checks.Add("continuous body sweep "+row.Id+"/"+peer.Id);}
                    if(row.Id==parked&&(!row.Held||!row.Position.Equals(goal)||row.Mobility!=basis.Mobility))checks.Add("parked ally changed");
                    if(expected.TryGetValue(row.Id,out long sequence)&&row.Command==sequence){
                        if(!groups.ContainsKey(sequence))groups.Add(sequence,row.Group);
                        var members=expected.Where(p=>p.Value==sequence).Select(p=>p.Key).OrderBy(id=>id).ToArray();
                        if(row.Group!=groups[sequence]||row.World!=Generation||row.RootRevision!=row.StampRevision||row.StampSequence!=sequence||row.Membership!=1||row.Kind!="Move"||row.Origin!="Human"||row.Owner!=owner||!row.RootGoal.Equals(goal)||row.Members==null||!row.Members.OrderBy(id=>id).SequenceEqual(members)||row.MemberMobility!=row.Mobility)checks.Add("original root/execution generation "+row.Id);
                        if(row.InstalledGroup==row.Group){if(!targets.ContainsKey(row.Id))targets.Add(row.Id,row.Goal);if(!row.Goal.Equals(targets[row.Id])||row.InstalledRevision!=row.StampRevision)checks.Add("assigned endpoint changed "+row.Id);}
                        if(!row.Position.Equals(basis.Position)&&firstMotion<0)firstMotion=f.Tick;
                    }
                }
                if(f.ReservationGroups.Distinct().Count()==2)sawBothReservations=true;previous=f;
            }}
            bool Until(Func<bool> ready,string stage){while(clock.Elapsed.TotalSeconds<180){host.Service(runtime,8);Audit();if(runtime.Latest.Failure!=null||runtime.IsStopped||checks.Count>0||!observerErrors.IsEmpty)return false;if(ready())return true;Thread.Sleep(1);}return false;}
            void Submit(long sequence,PlayableCommandKind kind,int[] ids)=>Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,sequence,owner,kind,ids,goal).AsHuman()).Accepted);
            bool Applied(long sequence)=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==sequence&&r.Status==PlayableCommandStatus.Applied);
            void Capture(string name){var task=runtime.RequestCaptureBytes();Assert.True(Until(()=>task.IsCompleted,"capture"));Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,task.Status,task.Exception?.ToString());var bytes=task.Result;using(var state=PlayableWorldState.Decode(bytes)){Assert.AreEqual(PlayableWorldState.CurrentVersion,state.Version);Assert.AreEqual(Seed,state.Seed);Assert.AreEqual(Generation,state.Generation);CollectionAssert.AreEqual(WorldWire.Binding(profile),state.Binding);CollectionAssert.AreEqual(OfflineConfigurationWire.Binding(config,profile),state.Participants);}File.WriteAllBytes(Path.Combine(folder,name),bytes);}
            try{
                Assert.True(Until(()=>runtime.IsPrepared&&runtime.Latest.Paused,"startup"));
                var visible=runtime.Latest.Entities;movers=startsAt.Select(p=>visible.Single(e=>e.Position.Equals(p)).Id).ToArray();parked=visible.Single(e=>e.Kind==PlayableEntityKind.Tank&&e.Position.Equals(goal)).Id;
                foreach(var a in nav.Crowd.Units){Assert.True(runtime.NavigationGeometry.IsFree(a.Position,a.Radius));Assert.True(profile.AuthoredMap.IsValidLocation(a.Location,a.Radius));}
                runtime.RequestPause(false);Assert.True(Until(()=>!runtime.Latest.Paused,"unpause hold"));Submit(1,PlayableCommandKind.Hold,new[]{parked});Assert.True(Until(()=>Applied(1),"hold receipt"));runtime.RequestPause(true);Assert.True(Until(()=>runtime.Latest.Paused,"hold prefix pause"));
                initial=ReadRows();Assert.True(initial.Single(r=>r.Id==parked).Held);previousTick=domain.Tick;Capture("held-prefix-current.bin");expected.Add(movers[0],2);expected.Add(movers[1],2);expected.Add(movers[2],3);
                domain.CombatStepForQa=()=>{try{if(Interlocked.Increment(ref count)>Horizon+32){observerErrors.Enqueue("bounded frame buffer exhausted");runtime.RequestPause(true);return;}queued.Enqueue(new Frame{Tick=domain.Tick,Rows=ReadRows(),ReservationGroups=nav.ArrivalReservations.Where(r=>movers.Contains((int)r.Entity)).Select(r=>r.Group).ToArray()});if(commandTick>=0&&domain.Tick-commandTick>=Horizon)runtime.RequestPause(true);}catch(Exception error){observerErrors.Enqueue(error.ToString());runtime.RequestPause(true);}};
                phase="original independent commands";runtime.RequestPause(false);Assert.True(Until(()=>!runtime.Latest.Paused,"unpause move"));Submit(2,PlayableCommandKind.Move,new[]{movers[0],movers[1]});Submit(3,PlayableCommandKind.Move,new[]{movers[2]});
                Assert.True(Until(()=>Applied(2)&&Applied(3),"both move receipts"));commandTick=runtime.ReceiptsAfter(0).Where(r=>r.Sequence==2||r.Sequence==3).Min(r=>r.AppliedTick);
                phase="natural terminal arrival";Assert.True(Until(()=>{var last=observed.LastOrDefault();arrived=last!=null&&last.Rows.Where(r=>movers.Contains(r.Id)).All(r=>r.Outcome==NavigationOutcome.Arrived);return arrived||(last!=null&&last.Tick-commandTick>=Horizon);},"all arrivals"),runtime.Latest.Failure+" "+string.Join(";",checks));
                runtime.RequestPause(true);Assert.True(Until(()=>runtime.Latest.Paused,"terminal pause"));Audit();Assert.IsEmpty(checks);Assert.IsEmpty(observerErrors);Assert.True(arrived);Assert.GreaterOrEqual(firstMotion,commandTick);Assert.AreEqual(2,groups.Count);Assert.AreNotEqual(groups[2],groups[3]);Assert.AreEqual(3,targets.Count);Assert.True(sawBothReservations,"independent groups must own their reservations concurrently");
                foreach(var id in movers)Assert.Zero(nav.ArrivalReservations.Count(r=>r.Entity==id));var occupancy=nav.CaptureArrivalOccupancySnapshot();
                foreach(var id in movers.Concat(new[]{parked})){var actor=nav.Crowd.Units.Single(a=>a.Id==id);var footprint=occupancy.Occupants.Single(o=>o.Kind==NavArrivalOccupantKind.Actor&&o.ActorId==id);Assert.True(footprint.BlocksPlacement);Assert.AreEqual(actor.Radius,footprint.Radius);Assert.AreEqual(actor.Location,footprint.Location);}
                Assert.False(targets.Values.Any(p=>p.Equals(goal)),"parked ally retains the occupied central point");Capture("terminal-current.bin");terminal=new{commandTick,firstMotion,Tick=domain.Tick,arrived,groups,targets,sawBothReservations};phase="complete";
            }catch(Exception error){primary=error;throw;}
            finally{
                domain.CombatStepForQa=null;
                void Clean(Action action){try{action();}catch(Exception error){cleanup.Add(error.ToString());}}
                var worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",Hidden).GetValue(host);
                Clean(()=>{runtime.Dispose();host.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));});
                if(runtime.IsStopped&&(worker==null||worker.IsStopped))Clean(()=>{nav.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);});
                census=new{RuntimeStopped=runtime.IsStopped,WorkerStopped=worker==null||worker.IsStopped,nav.PhysicalHostBudget.PhysicalBytes,nav.PhysicalHostBudget.HeldBytes,nav.PhysicalHostBudget.ActiveScopes};
                try{Save("frames.json",observed);Save("result.json",new{Result=primary==null&&cleanup.Count==0?"PASS":"FAIL",phase,Seed,Generation,Horizon,Goal=goal,Starts=startsAt,movers,parked,terminal,Checks=checks,ObserverErrors=observerErrors.ToArray(),Primary=primary?.ToString(),CleanupFailures=cleanup,Census=census});}catch(Exception error){if(primary==null)throw;TestContext.WriteLine("A4 artifact secondary: "+error);}
                if(primary==null)Assert.IsEmpty(cleanup);else foreach(var error in cleanup)TestContext.WriteLine("A4 cleanup secondary: "+error);
            }
        }
    }
}
