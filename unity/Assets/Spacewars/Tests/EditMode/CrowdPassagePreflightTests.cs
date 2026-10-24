using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdPassagePreflightTests
    {
        private static double crowdGrid(NavigationProfile p)=>p.GridCell;
        [Test] public void FiniteHeldRowRequiresInstalledSingleWidthRouteAndUnchangedMarchStaging()=>RunFiniteHeldRow(false);
        [Test] public void FiniteHeldRowTrafficMovesBothOrdinaryGroupsWithinOriginalMotionBound()=>RunFiniteHeldRow(true);
        [Test] public void QueuedTrafficColdRestoreAfterOrdinarySubsetCommandKeepsSurvivorAgeAndExactNext()=>RunFiniteHeldRow(false,true);
        [Test] public void FirstTrafficActivationRefusesBeforeIncomingReadyInstallAndThenPublishesComposedFrame()=>RunFiniteHeldRow(false,false,true);
        [TestCase(false)][TestCase(true)] public void ActualTrafficCandidateLastArrayRefusalUnwindsMovementAndComposedResults(bool ready)=>RunFiniteHeldRow(false,false,false,ready?2:1);
        private void RunFiniteHeldRow(bool motion,bool cold=false,bool activation=false,int trafficRefusal=0)
        {
            var basis=CrowdPassageTests.Configuration();var terrain=basis.Profile.AuthoredMap;
            double radius=basis.Profile.TankCollisionRadius;
            var clearance=new NavClearanceProfile("B2-preflight",1,"tank",1,radius);
            var initial=new PartitionNavigationProvider(terrain,new NavGeometry(terrain.HalfExtent,basis.Obstacles.ToArray(),1),null);
            Assert.True(initial.TryLocate(new NavPoint(-18,0),clearance,out var from));
            Assert.True(initial.TryTrace(from,new NavPoint(18,0),clearance,out var to,out var trace));
            var paired=trace.Where(l=>l.Kind==NavTypedLegKind.Portal).Select(l=>initial.Seams.Single(s=>s.Id==l.PortalId)).ToArray();
            Assert.AreEqual(2,paired.Length,"finite straight-strip premise: two real physical seams");
            // This finite candidate is derived from the physical seam extents, not a map/bridge id.
            double minZ=paired.Max(s=>Math.Min(s.From.Z,s.To.Z)),maxZ=paired.Min(s=>Math.Max(s.From.Z,s.To.Z));
            double entry=paired.Min(s=>s.From.X),exit=paired.Max(s=>s.From.X),middle=(entry+exit)/2;
            Assert.True(paired.All(s=>s.Allowed&&s.From.X==s.To.X));
            var movement=new NavGeometry(terrain.HalfExtent,basis.Obstacles.ToArray(),1);
            var originalTop=new NavPoint(middle,maxZ-radius);
            TestContext.WriteLine("B2_ORIGINAL_ROW "+Newtonsoft.Json.JsonConvert.SerializeObject(new{entry,exit,minZ,maxZ,OriginalTop=originalTop,OriginalTopFree=movement.IsFree(originalTop,radius),Blocking=movement.Obstacles.Where(o=>o.DistanceSquared(originalTop)<=radius*radius).Select(o=>new{o.MinX,o.MinZ,o.MaxX,o.MaxZ}).ToArray()}));
            // Strict packing replaces the touching row: half of the spare width belongs
            // to the traversable edge gap; the other half is shared by the top and seven
            // interior gaps. No pair touches and no narrow internal gap admits a mover.
            double spare=maxZ-minZ-18*radius;
            double grid=crowdGrid(basis.Profile.Navigation),gridLane=Math.Ceiling((minZ+radius)/grid)*grid;
            // Half the admissible non-lane gap keeps the first supported ordinary
            // lattice row inside the single lane; all eight other gaps stay strict.
            double gap=(spare-(gridLane-(minZ+radius)))/16;
            Assert.Greater(spare,0);Assert.Greater(gap,0);
            double top=maxZ-radius-gap;
            var held=Enumerable.Range(0,8).Select(i=>new NavPoint(middle,top-i*(2*radius+gap))).ToArray();
            double low=minZ+radius,high=held.Last().Z-2*radius,lane=(low+high)/2;
            Assert.GreaterOrEqual(high,low,"one full-footprint lane exists");
            Assert.Less(high-low,2*radius,"two opposing full circles cannot pass at the row plane");
            foreach(var point in held)Assert.True(movement.IsFree(point,radius)&&terrain.SupportsFootprint(point,radius),"derived row legal full footprint "+Newtonsoft.Json.JsonConvert.SerializeObject(point));
            var scenario=basis.ScenarioUnits.Concat(held.Select(p=>new OfflineScenarioUnit(basis.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,p,0))).ToArray();
            var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int i=0;i<basis.Starts.Count;i++)for(int j=0;j<basis.Starts.Count;j++)costs[i,j]=basis.RouteCost(i,j);
            var c=new OfflineMatchConfiguration(basis.Profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,basis.Spectators.ToArray(),scenario,terrain.Id,basis.ScenarioBuildings.ToArray());
            var world=new OfflineParticipantAuthority(c,71);var nav=world.Navigation;var facts=new List<object>();int preparation=0;
            var host=new UnityHostRouteService();try{
                var tanks=world.View(c.Roster[0].Id).Entities.Where(e=>e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).OrderBy(id=>id).ToArray();
                var blockers=tanks.Where(id=>nav.Crowd.Units.Single(u=>u.Id==id).Position.X==middle).ToArray();
                var movers=tanks.Except(blockers).ToArray();Assert.AreEqual(8,blockers.Length);Assert.AreEqual(4,movers.Length);
                foreach(var a in nav.Crowd.Units){
                    Assert.True(world.Domain.Geometry.IsFree(a.Position,a.Radius)&&terrain.SupportsFootprint(a.Position,a.Radius),"full legal initial footprint "+a.Id);
                    foreach(var b in nav.Crowd.Units.Where(b=>b.Id<a.Id)){double dx=a.Position.X-b.Position.X,dz=a.Position.Z-b.Position.Z;Assert.GreaterOrEqual(dx*dx+dz*dz,Math.Pow(a.Radius+b.Radius,2)-1e-8,"full initial pair "+a.Id+"/"+b.Id);}
                }
                long sequence=1;
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,sequence++,c.Roster[0].Id,PlayableCommandKind.Hold,blockers,new NavPoint(middle,0)).AsHuman()).Status);
                foreach(int id in blockers)Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Held);
                foreach(int direction in new[]{1,-1}){
                    var group=movers.Where(id=>Math.Sign(nav.Crowd.Units.Single(u=>u.Id==id).Position.X)==-direction).ToArray();Assert.AreEqual(2,group.Length);
                    Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,sequence++,c.Roster[0].Id,PlayableCommandKind.Move,group,new NavPoint(direction*18,0)).AsHuman()).Status);
                }
                for(;preparation<CrowdPassageTests.RoutePreparationBound&&nav.PendingCount>0;preparation++){host.Service(new PlayableRouteBinding(world.Domain.Geometry,c.Profile,nav.Admission,nav.RoutePort),CrowdPassageTests.RouteBudget);if(activation&&nav.Answers.Count>0)break;nav.ApplyResults();}
                if(activation){RunFirstActivationBoundary(world,host,c,nav,facts);return;}
                facts.Add(new{entry,exit,minZ,maxZ,middle,low,high,lane,held,Preparation=preparation,nav.PendingCount,Grid=grid,gridLane,Actors=movers.Select(id=>{var a=nav.Crowd.Units.Single(u=>u.Id==id);return new{a.Id,a.Outcome,a.Moving,a.InstalledRequest};}).ToArray()});
                facts.Add(new{OrdinaryHostSolverEpoch=host.SolverEpoch,OrdinaryHostCounters=host.CertifiedCounters,host.LastProjectionFailure,Arrival=FinalArrivalScalars(nav),Allocating=nav.RoutePort.AllocationCount,QueuedRequests=nav.RoutePort.QueuedRequestCount,Answers=nav.Answers.Count,nav.PhysicalHostBudget.PhysicalBytes,nav.PhysicalHostBudget.HeldBytes,nav.PhysicalHostBudget.ActiveScopes});
                Assert.Zero(nav.PendingCount,"4096 preparation pumps at unchanged budget8; motion has not started");
                foreach(int id in movers){
                    Assert.NotNull(nav.InstalledExecutionFor(id),"ordinary installed route for mover "+id);
                    var installed=((NavigationInstalledExecutionOwner)nav.Crowd.Units.Single(u=>u.Id==id).StorageOwner.InstalledExecution).State;
                    var provider=(PartitionNavigationProvider)installed.StorageOwner.Corridor.ProviderOwner.Provider;
                    var actual=installed.Corridor.Legs.Where(l=>l.Kind==NavTypedLegKind.Portal).Select(l=>provider.Seams.Single(s=>s.Id==l.PortalId)).ToArray();
                    facts.Add(new{Actor=id,installed.Corridor.HoldBinding,Seams=actual.Select(s=>new{s.From,s.To,s.Id}).ToArray(),Route=installed.InstalledRoute});
                    Assert.AreEqual(2,actual.Length);CollectionAssert.AreEquivalent(paired.Select(s=>s.Id),actual.Select(s=>s.Id),"ordinary typed route must select this paired strip");
                    var cutLeft=new double[installed.MovementGeometry.Obstacles.Count];var cutRight=new double[cutLeft.Length];
                    Assert.True(NavigationTrafficStrip.TryCreate(installed.StorageOwner,terrain,installed.StorageOwner.Corridor.Recipe.Clearance,cutLeft,cutRight,out var traffic),"generic installed paired-strip certificate");
                    Assert.True(traffic.ExitSeatSafe(provider,installed.MovementGeometry,terrain,installed.StorageOwner.Corridor.Recipe.Clearance,1),"second distinct complete downstream exit");
                    Assert.AreEqual(entry,traffic.Entry);Assert.AreEqual(exit,traffic.Exit);facts.Add(new{TrafficDirection=traffic.Direction,traffic.Lane,Wait=traffic.Wait,Exit0=traffic.Downstream(0),Exit1=traffic.Downstream(1)});
                    Assert.AreEqual(8,installed.MovementGeometry.Obstacles.Count(o=>o.CircleRadius==radius),"actual ordinary HOLD movement circles");
                    foreach(double direction in new[]{1d,-1d}){
                        // Full endpoint, turn-in-place (circle) and sweep proof through the row and both seams.
                        var wait=new NavPoint(direction>0?entry-2*radius:exit+2*radius,lane);
                        var finish=new NavPoint(direction>0?exit+4*radius:entry-4*radius,lane);
                        Assert.True(installed.MovementGeometry.IsFree(wait,radius)&&installed.MovementGeometry.IsFree(finish,radius));
                        Assert.True(installed.MovementGeometry.SegmentFree(wait,finish,radius)&&terrain.SupportsSweep(wait,finish,radius));
                        Assert.True(provider.TryLocate(wait,clearance,out var origin)&&provider.TryTraverse(origin,finish,clearance,out _));
                        var leader=new NavPoint(middle+direction*radius,lane);var follower=new NavPoint(middle-direction*radius,lane);
                        Assert.True(provider.TryLocate(leader,clearance,out var lead));Assert.True(provider.TryLocate(follower,clearance,out var follow));
                        double lp=GroupMarch.Project(installed.Corridor,lead,double.NegativeInfinity),fp=GroupMarch.Project(installed.Corridor,follow,double.NegativeInfinity);
                        facts.Add(new{Actor=id,direction,LeaderProgress=lp,FollowerProgress=fp,Stretch=c.Profile.GroupMarchMaximumStretch});
                        if(Math.Sign(installed.Corridor.Endpoint.Position.X-installed.Corridor.Origin.Position.X)==direction)Assert.LessOrEqual(lp-fp,c.Profile.GroupMarchMaximumStretch,"staggered entry under existing GroupMarch.Project cap");
                        var exit2=new NavPoint(finish.X+direction*2*radius,finish.Z);Assert.True(installed.MovementGeometry.SegmentFree(finish,exit2,radius)&&terrain.SupportsSweep(finish,exit2,radius),"distinct downstream full exit for follower/leader");
                    }
                }
                foreach(int sign in new[]{1,-1}){
                    var group=movers.Where(id=>Math.Sign(nav.Crowd.Units.Single(u=>u.Id==id).Position.X)==-sign).ToArray();
                    var corridors=group.Select(id=>nav.InstalledExecutionFor(id).Corridor).ToArray();
                    var middlePoint=new NavPoint(middle,lane);var provider=(PartitionNavigationProvider)((NavigationInstalledExecutionOwner)nav.Crowd.Units.Single(u=>u.Id==group[0]).StorageOwner.InstalledExecution).Corridor.ProviderOwner.Provider;
                    Assert.True(provider.TryLocate(middlePoint,clearance,out var center));
                    var centerProgress=corridors.Select(d=>GroupMarch.Project(d,center,double.NegativeInfinity)).ToArray();
                    // Terminal normalization can require a stagingGap larger than two radii.
                    // Put the member with the farther terminal first, preserving stretch.
                    int leader=centerProgress[0]<centerProgress[1]?0:1,follower=1-leader;double stagingGap=Math.Max(2*radius,Math.Abs(centerProgress[0]-centerProgress[1]));
                    Assert.Less(stagingGap+2*radius,exit-entry,"both complete footprints fit before leader clears");
                    var front=new NavPoint(middle+sign*stagingGap/2,lane);var rear=new NavPoint(middle-sign*stagingGap/2,lane);
                    Assert.True(provider.TryLocate(front,clearance,out var lead));Assert.True(provider.TryLocate(rear,clearance,out var follow));
                    double lp=GroupMarch.Project(corridors[leader],lead,double.NegativeInfinity),fp=GroupMarch.Project(corridors[follower],follow,double.NegativeInfinity),anchor=Math.Min(lp,fp);
                    Assert.Greater(GroupMarch.MaxSpeed(c.Profile.TankSpeed,c.Profile.TankSpeed,lp,anchor,c.Profile.GroupMarchMaximumStretch,1d/30),0,"actual leader retains march preference");
                    Assert.Greater(GroupMarch.MaxSpeed(c.Profile.TankSpeed,c.Profile.TankSpeed,fp,anchor,c.Profile.GroupMarchMaximumStretch,1d/30),0,"actual follower retains march preference before leader exit");
                    facts.Add(new{Group=nav.GroupFor(group[0]).GroupId,Leader=group[leader],Follower=group[follower],Gap=stagingGap,LeaderProgress=lp,FollowerProgress=fp,Anchor=anchor,Stretch=c.Profile.GroupMarchMaximumStretch});
                }
                if(motion)RunTrafficMotion(world,host,c,nav,movers,blockers,entry,exit,facts);
                if(cold)RunQueuedTrafficCold(world,host,c,nav,sequence,facts);
                if(trafficRefusal>0)RunTrafficCandidateRefusal(world,host,c,nav,sequence,trafficRefusal==2,facts);
            }finally{
                host.Dispose();nav.ReleaseCommittedHostRoot();world.Domain.DestroyOwnedNavigation();
                TestContext.WriteLine("B2_FINITE_PREFLIGHT "+Newtonsoft.Json.JsonConvert.SerializeObject(new{c.Seed,Radius=radius,Facts=facts,Cleanup=new{nav.PhysicalHostBudget.PhysicalBytes,nav.PhysicalHostBudget.HeldBytes,nav.PhysicalHostBudget.ActiveScopes}}));
                Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
            }
        }
        private static void RunTrafficCandidateRefusal(OfflineParticipantAuthority world,UnityHostRouteService host,OfflineMatchConfiguration c,NavigationSession nav,long sequence,bool ready,List<object> facts)
        {
            var field=typeof(NavigationSession).GetField("trafficAuthority",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);nav.Step(1d/30);var initial=(NavigationTrafficAuthority)field.GetValue(nav);Assert.NotNull(initial);Assert.AreEqual(4,initial.Count);
            if(ready){
                int replaced=(int)Enumerable.Range(0,initial.Count).Where(i=>initial.At(i,6)==0).Select(i=>initial.At(i,0)).First();var goal=nav.GroupFor(replaced).OriginalGoal;
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,sequence,c.Roster[0].Id,PlayableCommandKind.Move,new[]{replaced},goal).AsHuman()).Status);
                for(int prep=0;prep<4096&&nav.Answers.Count==0;prep++){host.Service(new PlayableRouteBinding(nav.Admission.Geometry,c.Profile,nav.Admission,nav.RoutePort),8);if(nav.Answers.Count==0)nav.ApplyResults();}
                Assert.Greater(nav.Answers.Count,0,"incoming current Ready before composed carrier refusal");
            }
            var previous=(NavigationTrafficAuthority)field.GetValue(nav);var bytes=world.CaptureBytes();long physical=nav.PhysicalHostBudget.PhysicalBytes;int references=previous.References,denied=0,readyPublications=nav.OwnedReadyPublications,movementPublications=nav.OwnedMovementPublications;long partial=0;
            nav.PhysicalHostBudget.AllocationAdmission=_=>{
                var frames=new System.Diagnostics.StackTrace().GetFrames();bool traffic=frames.Any(f=>f.GetMethod().DeclaringType==typeof(NavigationTrafficAuthority)&&f.GetMethod().Name=="Create");
                bool exit=frames.Any(f=>{var type=f.GetMethod().DeclaringType;return type!=null&&type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(NavRetainedArray<>)&&type.GetGenericArguments()[0]==typeof(NavPoint)&&f.GetMethod().Name=="Copy";});
                if(traffic&&exit){denied++;partial=nav.PhysicalHostBudget.PhysicalBytes;return false;}return true;
            };
            try{nav.Step(1d/30);}finally{nav.PhysicalHostBudget.AllocationAdmission=null;}
            Assert.AreEqual(1,denied,"refuse Traffic.Create Exits after Values/Executions have been born");Assert.Greater(partial,physical);Assert.AreSame(previous,field.GetValue(nav));Assert.AreEqual(references,previous.References);Assert.AreEqual(physical,nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);Assert.AreEqual(readyPublications,nav.OwnedReadyPublications);Assert.AreEqual(movementPublications,nav.OwnedMovementPublications);ExactWorldBytes(bytes,world.CaptureBytes(),"partially born Traffic rollback, ready="+ready);
            nav.Step(1d/30);Assert.AreNotSame(previous,field.GetValue(nav));Assert.Greater(nav.OwnedMovementPublications,movementPublications);if(ready)Assert.Greater(nav.OwnedReadyPublications,readyPublications);
            facts.Add(new{TrafficCandidateRefusal=true,ComposedReady=ready,RefusalSite="Traffic.Create/Exits Copy after Values+Executions",Denied=denied,OldPhysical=physical,PartialBornPhysical=partial,OldTrafficReferences=references});
        }
        private static void ExactWorldBytes(byte[] expected,byte[] actual,string context)
        {
            Assert.AreEqual(expected.Length,actual.Length,context+" length");
            for(int i=0;i<expected.Length;i++)if(expected[i]!=actual[i])Assert.Fail(context+" byte "+i+": "+expected[i]+" != "+actual[i]);
        }
        private static void RunFirstActivationBoundary(OfflineParticipantAuthority world,UnityHostRouteService host,OfflineMatchConfiguration c,NavigationSession nav,List<object> facts)
        {
            var field=typeof(NavigationSession).GetField("trafficAuthority",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);Assert.Null(field.GetValue(nav));Assert.Greater(nav.Answers.Count,0);
            int denied=0,maintenance=0,deniedWhileAllocating=0,preparationDenials=0;var source=nav.Crowd.Units.Select(u=>u.CopyMovementHeader()).ToArray();
            // A ready subset can reach composed publication before every other
            // allocation settles. Refuse the target frame from the first Step,
            // while still permitting request-only PLAN maintenance.
            var layout=typeof(NavPreparedCrowdStep).GetMethod("Layout",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);long frameBytes=NavStorageLayout.Charge((long)layout.Invoke(null,new object[]{nav.Crowd.Units.Count}));
            nav.PhysicalHostBudget.AllocationAdmission=bytes=>{
                if(bytes!=frameBytes)return true;
                if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavPreparedCrowdStep)&&f.GetMethod().Name=="Create")){
                    denied++;if(nav.RoutePort.AllocationCount>0)deniedWhileAllocating++;if(denied==1)TestContext.WriteLine("B2_FIRST_FRAME_ATTEMPT allocations="+nav.RoutePort.AllocationCount+",answers="+nav.Answers.Count);return false;}return true;
            };
            try{
                for(int service=0;service<4096&&nav.RoutePort.AllocationCount>0;service++){
                    host.Service(new PlayableRouteBinding(nav.Admission.Geometry,c.Profile,nav.Admission,nav.RoutePort),8);int requests=nav.Requests.Count;nav.Step(1d/30);
                    foreach(var actor in nav.Crowd.Units)Assert.True(source.Single(u=>u.Id==actor.Id).SameMovementValues(actor),"maintenance changes no actor header");Assert.Null(field.GetValue(nav));Assert.Greater(nav.Answers.Count,0);if(nav.Requests.Count>requests)maintenance++;
                }
                Assert.Zero(nav.RoutePort.AllocationCount,"original4096 bound reaches request-only PLAN maintenance");
                preparationDenials=denied;var prefix=world.CaptureBytes();nav.Step(1d/30);
                Assert.Greater(denied,preparationDenials,"real composed-frame refusal after permitted maintenance");
                foreach(var actor in nav.Crowd.Units)Assert.True(source.Single(u=>u.Id==actor.Id).SameMovementValues(actor));Assert.Null(field.GetValue(nav));Assert.Greater(nav.Answers.Count,0);
                CollectionAssert.AreEqual(prefix,world.CaptureBytes(),"full prepared frame refusal precedes every result/traffic publication");
            }finally{nav.PhysicalHostBudget.AllocationAdmission=null;}
            nav.Step(1d/30);var traffic=(NavigationTrafficAuthority)field.GetValue(nav);Assert.NotNull(traffic);Assert.Greater(traffic.Count,0);Assert.Greater(nav.OwnedResultBatchPublications,0);Assert.Greater(nav.OwnedMovementPublications,0);
            facts.Add(new{FirstActivationRefusals=denied,PreparationFrameRefusals=preparationDenials,DeniedWhileAllocating=deniedWhileAllocating,MaintenanceOnlyPlans=maintenance,FrameBytes=frameBytes,TrafficRows=traffic.Count,nav.OwnedResultBatchPublications,nav.OwnedMovementPublications});
        }
        private static void RunQueuedTrafficCold(OfflineParticipantAuthority world,UnityHostRouteService host,OfflineMatchConfiguration c,NavigationSession nav,long sequence,List<object> facts)
        {
            var field=typeof(NavigationSession).GetField("trafficAuthority",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            world.Step(1d/30);var before=(NavigationTrafficAuthority)field.GetValue(nav);Assert.NotNull(before);Assert.AreEqual(4,before.Count);
            var queued=Enumerable.Range(0,before.Count).Where(i=>before.At(i,6)==0).ToArray();Assert.AreEqual(2,queued.Length);int replaced=(int)before.At(queued[0],0),survivor=(int)before.At(queued[1],0);long age=before.At(queued[1],5),incarnation=before.At(queued[1],1);
            var goal=nav.GroupFor(replaced).OriginalGoal;Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,sequence,c.Roster[0].Id,PlayableCommandKind.Move,new[]{replaced},goal).AsHuman()).Status);
            // Deliberately capture after ordinary Apply, before any movement can prune stale rows.
            var bytes=world.CaptureBytes();var cold=OfflineParticipantAuthority.Restore(bytes,c);var coldHost=new UnityHostRouteService();
            try{ExactWorldBytes(bytes,cold.CaptureBytes(),"initial full queued checkpoint");
                for(int tick=0;tick<3;tick++){
                    host.Service(new PlayableRouteBinding(nav.Admission.Geometry,c.Profile,nav.Admission,nav.RoutePort),8);coldHost.Service(new PlayableRouteBinding(cold.Navigation.Admission.Geometry,c.Profile,cold.Navigation.Admission,cold.Navigation.RoutePort),8);
                    world.Step(1d/30);cold.Step(1d/30);ExactWorldBytes(world.CaptureBytes(),cold.CaptureBytes(),"exact full queued/subset continuation tick "+tick);
                    var now=(NavigationTrafficAuthority)field.GetValue(nav);int row=now.Find(survivor,incarnation);Assert.GreaterOrEqual(row,0);Assert.AreEqual(age,now.At(row,5),"surviving queued member keeps FIFO age");
                    int changed=now.Find(replaced,nav.Crowd.Units.Single(u=>u.Id==replaced).Incarnation);if(changed>=0)Assert.Zero(now.At(changed,6),"replaced queued command cannot inherit an old grant");
                }
                var movers=Enumerable.Range(0,before.Count).Select(i=>(int)before.At(i,0)).ToArray();var held=nav.Crowd.Units.Where(a=>a.Held).ToDictionary(a=>a.Id,a=>a.CopyMovementHeader());int ticks=3,survivorGrant=-1,replacedGrant=-1;
                for(;ticks<2700&&movers.Any(id=>nav.Crowd.Units.Single(a=>a.Id==id).Outcome!=NavigationOutcome.Arrived);ticks++){
                    var prior=nav.Crowd.Units.Select(a=>a.CopyMovementHeader()).ToArray();host.Service(new PlayableRouteBinding(nav.Admission.Geometry,c.Profile,nav.Admission,nav.RoutePort),8);coldHost.Service(new PlayableRouteBinding(cold.Navigation.Admission.Geometry,c.Profile,cold.Navigation.Admission,cold.Navigation.RoutePort),8);world.Step(1d/30);cold.Step(1d/30);
                    var now=(NavigationTrafficAuthority)field.GetValue(nav);for(int row=0;row<now.Count;row++)if(now.At(row,6)>0){if(now.At(row,0)==survivor&&survivorGrant<0)survivorGrant=ticks;if(now.At(row,0)==replaced&&replacedGrant<0)replacedGrant=ticks;}
                    for(int i=0;i<nav.Crowd.Units.Count;i++){
                        var actor=nav.Crowd.Units[i];var old=prior.Single(a=>a.Id==actor.Id);var twin=cold.Navigation.Crowd.Units.Single(a=>a.Id==actor.Id);Assert.AreEqual(actor.Position,twin.Position);Assert.AreEqual(BitConverter.DoubleToInt64Bits(actor.Heading),BitConverter.DoubleToInt64Bits(twin.Heading));Assert.AreEqual(actor.Outcome,twin.Outcome);Assert.AreEqual(actor.InstalledRequest,twin.InstalledRequest);Assert.AreEqual(actor.routeIndex,twin.routeIndex);Assert.AreEqual(actor.localIndex,twin.localIndex);
                        double dx=actor.Position.X-old.Position.X,dz=actor.Position.Z-old.Position.Z;Assert.LessOrEqual(dx*dx+dz*dz,old.Speed*old.Speed/900+1e-9);Assert.True(c.Profile.AuthoredMap.SupportsSweep(old.Position,actor.Position,actor.Radius));Assert.True(nav.Admission.Geometry.SegmentFree(old.Position,actor.Position,actor.Radius));
                        if(held.TryGetValue(actor.Id,out var stop)){Assert.AreEqual(stop.Position,actor.Position);Assert.AreEqual(BitConverter.DoubleToInt64Bits(stop.Heading),BitConverter.DoubleToInt64Bits(actor.Heading));Assert.True(actor.Held);}
                        for(int j=0;j<i;j++){var other=nav.Crowd.Units[j];var original=prior.Single(a=>a.Id==other.Id);double x=old.Position.X-original.Position.X,z=old.Position.Z-original.Position.Z,vx=dx-other.Position.X+original.Position.X,vz=dz-other.Position.Z+original.Position.Z,q=vx*vx+vz*vz,t=q>0?Math.Max(0,Math.Min(1,-(x*vx+z*vz)/q)):0,r=actor.Radius+other.Radius;Assert.GreaterOrEqual((x+vx*t)*(x+vx*t)+(z+vz*t)*(z+vz*t),r*r-1e-8);}
                    }
                    if(ticks%30==0)ExactWorldBytes(world.CaptureBytes(),cold.CaptureBytes(),"full saved continuation in subset suffix tick "+ticks);
                }
                facts.Add(new{QueuedColdBytes=bytes.Length,Replaced=replaced,Survivor=survivor,SurvivorAge=age,ExactNextSteps=3,MotionSuffixTicks=ticks,SurvivorGrantTick=survivorGrant,ReplacedGrantTick=replacedGrant,nav.PendingCount,Actors=movers.Select(id=>{var a=nav.Crowd.Units.Single(u=>u.Id==id);return new{a.Id,a.Outcome,a.Moving,a.Position,a.InstalledRequest};}).ToArray()});
                Assert.GreaterOrEqual(survivorGrant,0);Assert.Greater(replacedGrant,survivorGrant,"older surviving queue member is served before the replacement command");foreach(int id in movers)Assert.AreEqual(NavigationOutcome.Arrived,nav.Crowd.Units.Single(a=>a.Id==id).Outcome,"current subset order arrives within unchanged2700 suffix");Assert.Zero(nav.PendingCount);ExactWorldBytes(world.CaptureBytes(),cold.CaptureBytes(),"final current-order world");
            }finally{coldHost.Dispose();cold.Navigation.ReleaseCommittedHostRoot();cold.Domain.DestroyOwnedNavigation();Assert.Zero(cold.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(cold.Navigation.PhysicalHostBudget.HeldBytes);Assert.Zero(cold.Navigation.PhysicalHostBudget.ActiveScopes);}
        }
        private static void RunTrafficMotion(OfflineParticipantAuthority world,UnityHostRouteService host,OfflineMatchConfiguration c,NavigationSession nav,int[] movers,int[] blockers,double entry,double exit,List<object> facts)
        {
            var held=blockers.ToDictionary(id=>id,id=>nav.Crowd.Units.Single(u=>u.Id==id).CopyMovementHeader());
            var groups=movers.GroupBy(id=>nav.GroupFor(id).GroupId).ToDictionary(g=>g.Key,g=>g.ToArray());var follow=new HashSet<long>();bool queue=false;int tick=0;
            var field=typeof(NavigationSession).GetField("trafficAuthority",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            for(;tick<2700&&movers.Any(id=>nav.Crowd.Units.Single(u=>u.Id==id).Outcome!=NavigationOutcome.Arrived);tick++){
                var before=nav.Crowd.Units.Select(u=>u.CopyMovementHeader()).ToArray();host.Service(new PlayableRouteBinding(nav.Admission.Geometry,c.Profile,nav.Admission,nav.RoutePort),8);world.Step(1d/30);
                var authority=(NavigationTrafficAuthority)field.GetValue(nav);if(authority!=null){int waiting=0,admitted=0;for(int i=0;i<authority.Count;i++){if(authority.At(i,6)==0)waiting++;else admitted++;}queue|=waiting>=2&&admitted>=2;}
                var occupying=new HashSet<long>();foreach(var group in groups){int inside=0;foreach(int id in group.Value){var actor=nav.Crowd.Units.Single(u=>u.Id==id);if(actor.Position.X+actor.Radius>=entry&&actor.Position.X-actor.Radius<=exit)occupying.Add(group.Key);if(actor.Position.X-actor.Radius>=entry&&actor.Position.X+actor.Radius<=exit)inside++;}if(inside==2)follow.Add(group.Key);}Assert.LessOrEqual(occupying.Count,1,"opposite cohorts never share the certified strip, tick "+tick);
                for(int i=0;i<nav.Crowd.Units.Count;i++){
                    var actor=nav.Crowd.Units[i];var old=before.Single(u=>u.Id==actor.Id);double dx=actor.Position.X-old.Position.X,dz=actor.Position.Z-old.Position.Z;
                    Assert.LessOrEqual(dx*dx+dz*dz,old.Speed*old.Speed/900+1e-9);Assert.True(c.Profile.AuthoredMap.SupportsSweep(old.Position,actor.Position,actor.Radius));Assert.True(nav.Admission.Geometry.SegmentFree(old.Position,actor.Position,actor.Radius));
                    if(held.TryGetValue(actor.Id,out var stop)){Assert.AreEqual(stop.Position,actor.Position);Assert.AreEqual(BitConverter.DoubleToInt64Bits(stop.Heading),BitConverter.DoubleToInt64Bits(actor.Heading));Assert.True(actor.Held);}
                    for(int j=0;j<i;j++){var other=nav.Crowd.Units[j];var previous=before.Single(u=>u.Id==other.Id);double x=old.Position.X-previous.Position.X,z=old.Position.Z-previous.Position.Z,vx=dx-other.Position.X+previous.Position.X,vz=dz-other.Position.Z+previous.Position.Z,q=vx*vx+vz*vz,t=q>0?Math.Max(0,Math.Min(1,-(x*vx+z*vz)/q)):0,r=actor.Radius+other.Radius;Assert.GreaterOrEqual((x+vx*t)*(x+vx*t)+(z+vz*t)*(z+vz*t),r*r-1e-8,"joint full circles tick "+tick);}
                }
            }
            facts.Add(new{TrafficMotionTicks=tick,Queue=queue,FollowerBeforeLeaderExit=follow.ToArray(),nav.OwnedMovementPublications,Actors=movers.Select(id=>{var a=nav.Crowd.Units.Single(u=>u.Id==id);var group=nav.GroupFor(id);var corridor=nav.InstalledExecutionFor(id)?.Corridor;double progress=corridor==null?double.NaN:GroupMarch.Project(corridor,a.Location,double.NegativeInfinity);return new{a.Id,a.Position,a.Moving,a.Outcome,a.InstalledRequest,a.Heading,a.routeIndex,a.localIndex,a.noProgressTicks,a.blockedSeconds,Target=a.routeIndex<a.route.Length?(NavPoint?)a.route[a.routeIndex]:null,Progress=progress,Anchor=group?.MarchAnchor,Preferred=group==null?double.NaN:GroupMarch.MaxSpeed(a.Speed,a.Speed,progress,group.MarchAnchor,group.MarchStretch,1d/30)};}).ToArray()});
            if(movers.Any(id=>nav.Crowd.Units.Single(u=>u.Id==id).Outcome!=NavigationOutcome.Arrived))facts.Add(TrafficMotionPreview(nav,c.Profile.AuthoredMap));
            Assert.True(queue,"both complete cohorts are admitted/queued through actual prepared movement");CollectionAssert.AreEquivalent(groups.Keys,follow,"every follower enters before its leader fully clears");foreach(int id in movers)Assert.AreEqual(NavigationOutcome.Arrived,nav.Crowd.Units.Single(u=>u.Id==id).Outcome,"ordinary current-order arrival "+id+" after2700");
        }
        private static object TrafficMotionPreview(NavigationSession nav,IPlayableTerrain terrain)
        {
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            object Read(string name)=>typeof(NavigationSession).GetField(name,flags).GetValue(nav);
            var groups=(NavigationCommandSlots<long,GroupOrderState>)Read("groups");var members=(NavigationCommandSlots<int,long>)Read("memberGroups");var source=(NavigationTrafficAuthority)Read("trafficAuthority");
            long baseline=nav.PhysicalHostBudget.PhysicalBytes;Assert.True(nav.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Projection,71,99999,out var scope));object result;
            using(scope){var r=scope.OwnedReservation();var frame=NavPreparedCrowdStep.Create(nav.Crowd,nav.Crowd.Units.Count,r);NavigationTrafficAuthority traffic=null;var writes=new List<NavigationAuthorityGroup>();
                try{frame.Observe();var owners=frame.Source.Select(a=>a.StorageOwner.InstalledExecution as NavigationInstalledExecutionOwner).ToArray();var detached=owners.Select(o=>o?.State.Detached??false).ToArray();
                    var march=new List<object>();foreach(var g in groups){var previous=((NavigationLiveGroup)groups.OwnerFor(g.Key)).ImmutableVersion;var next=GroupMarch.PrepareOwned(frame,previous,owners,detached,members,g.Value.MarchStretch,g.Value.MarchProfileRevision,1d/30,r);if(next!=null)writes.Add(next);march.Add(new{Group=g.Key,Anchor=(next??previous).Data.MarchAnchor,Stretch=(next??previous).Data.MarchStretch,Members=(next??previous).Members.Select(m=>{var d=((NavigationAuthorityMember)m).Data;return new{d.Entity,d.Progress,d.ProgressRequest};}).ToArray()});}
                    var before=(double[])frame.Preferred.Clone();traffic=NavigationTrafficAuthority.Prepare(source,frame,owners,detached,members,terrain,r);
                    var bound=typeof(NavigationSession).GetMethod("RebindStagingScratchBound",flags|System.Reflection.BindingFlags.Static);long scratch=(long)bound.Invoke(bound.IsStatic?null:nav,new object[]{frame.After.Length,0,nav.Admission.Geometry.Obstacles.Count});using(NavStorageScratch.Reserve(r,scratch))frame.Compute(1d/30);
                    var post=(NavMotionProposal[])frame.Proposals.Clone();var selected=new NavMotionProposal[frame.After.Length];
                    for(int i=0;i<frame.After.Length;i++)frame.After[i].CopyMovementValuesFrom(frame.Before[i]);
                    for(int i=0;i<frame.After.Length;i++)selected[i]=NavCrowdMotion.Select(frame.Motion,i,frame.Neighbors,frame.NeighborCounts[i],frame.Profile,1d/30,frame.Working);
                    result=new{PreviewMarch=march,nav.Crowd.RepairCount,nav.Crowd.RepairFailed,nav.Crowd.MaxNoProgressTicks,Rows=frame.After.Select((a,i)=>new{a.Id,BeforeTraffic=before[i],AfterTraffic=frame.Preferred[i],frame.Motion[i].Active,frame.Motion[i].Speed,frame.Motion[i].TurnSpeed,Target=frame.Motion[i].Target,SourceRouteIndex=frame.Before[i].routeIndex,SourceLocalIndex=frame.Before[i].localIndex,LocalCount=frame.Before[i].localRoute.Length,RouteCount=frame.Before[i].route.Length,NextGlobal=frame.Before[i].routeIndex<frame.Before[i].route.Length?(NavPoint?)frame.Before[i].route[frame.Before[i].routeIndex]:null,SelectedPosition=selected[i].Position,SelectedVelocity=selected[i].Velocity,SelectedBlocked=selected[i].Blocked,SelectedHeading=selected[i].Heading,JointHeading=post[i].Heading,ActualDt=1d/30,SelectedOverflow=selected[i].Overflow,JointPosition=post[i].Position,JointVelocity=post[i].Velocity,JointBlocked=post[i].Blocked,JointOverflow=post[i].Overflow,Before=frame.Before[i].Position,SourceHeading=frame.Before[i].Heading,BeforeTrafficProgress=owners[i]==null?double.NaN:GroupMarch.Project(owners[i].Corridor.Corridor,frame.Before[i].Location,double.NegativeInfinity)}).ToArray()};
                }finally{traffic?.Release();foreach(var write in writes)write.ReleaseStorage();frame.Release();}
            }
            Assert.AreEqual(baseline,nav.PhysicalHostBudget.PhysicalBytes);return result;
        }
        private static object[] FinalArrivalScalars(NavigationSession nav)
        {
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
            object Read(object value,string name)=>value?.GetType().GetField(name,flags)?.GetValue(value);
            var result=new List<object>();var rows=(System.Collections.IEnumerable)Read(nav,"arrivalAttempts");
            foreach(var pair in rows){var a=pair.GetType().GetProperty("Value").GetValue(pair);var work=(NavigationArrivalWork)Read(a,"Work");var scheduler=(LayeredNavigationScheduler)Read(work,"scheduler");var jobs=(NavPersistentQueue<LayeredNavigationScheduler.Job>)Read(scheduler,"jobs");var job=jobs?.First;
                result.Add(new{Id=Read(a,"Id"),Group=Read(a,"Group"),Epoch=Read(a,"Epoch"),Status=Read(a,"SnapshotStatus"),Phase=Read(a,"SnapshotPhase"),Candidate=Read(a,"SnapshotCandidate"),Member=Read(a,"SnapshotMember"),Published=Read(a,"Published"),Working=Read(a,"Working"),Retiring=Read(a,"Retiring"),Allocation=work.AllocationWorkConsumed,ArrivalCounters=work.RouteCounters,
                    CurrentJob=FinalJobScalars(job)});
            }return result.ToArray();
        }
        private static object FinalJobScalars(LayeredNavigationScheduler.Job job)
        {
            if(job==null)return null;var field=job.Field;int n=job.Graph?.Nodes.Count??0,c=field?.Region.Candidates.Count??0;int? w=field==null?(int?)null:job.Graph.Nodes.Count(v=>v.Location.SurfaceId!=field.Region.Requested.SurfaceId);
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
            object Read(object value,string name)=>value?.GetType().GetField(name,flags)?.GetValue(value);
            return new{Phase=job.Phase.ToString(),Subscribers=job.Subscribers.Count,FieldStatus=field?.Status,FieldCapacitySafe=field?.PartialCapacitySafe,FieldSeedComplete=field?.SeedComplete,Nodes=n,Candidates=c,WrongSurfaceNodes=w,OldSeedPaidUnits=field==null?(long?)null:(long)n*c+c+1,NodeSeedPaidUnits=w.HasValue?(long?)c+w.Value+(long)(n-w.Value)*c:null,Labels=field?.LabelCount,Frontier=field?.FrontierCount,ConnectorPhase=Read(job.Connector,"phase")?.ToString(),ConnectorBest=Read(job.Connector,"best"),ConnectorCursor=Read(job.Connector,"cursor"),ConnectorCandidate=Read(job.Connector,"candidate"),FieldCurrent=Read(field,"current"),FieldPhase=Read(field,"phase")?.ToString()};
        }
    }
}
