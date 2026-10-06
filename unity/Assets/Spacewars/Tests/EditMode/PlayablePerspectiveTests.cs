using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayablePerspectiveTests
    {
        private static readonly Type Type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;
        private object Call(string name,params object[] args)=>Type.GetMethod(name,Flags).Invoke(domain,args);
        private NavigationSession Nav=>(NavigationSession)Type.GetProperty("Navigation",Flags).GetValue(domain);
        private PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        private PlayableSnapshot RouteView(params PlayableRouteRequest[] requests)=>(PlayableSnapshot)Call("PlayerSnapshotWithRoutes",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player,requests);
        private int Spawn(double x,double z){Call("SpawnPlayer",new NavPoint(x,z),new NavPoint(x,z));return View().Entities.Max(e=>e.Id);}
        private void Field(int building,string name,object value)
        {
            var all=(IDictionary)Type.GetField("buildings",Flags).GetValue(domain);var b=all[building];b.GetType().GetField(name).SetValue(b,value);
        }
        private PlayableCommandStatus Attack(int unit,int target)
        {var args=new object[]{new PlayableCommand(1,1,"player-1",PlayableCommandKind.Attack,new[]{unit},targetId:target),null};return (PlayableCommandStatus)Type.GetMethod("Apply",Flags).Invoke(domain,args);}
        [Test] public void ProjectileVisibilityAndEndpointIdentitiesDoNotRevealHiddenActors()
        {
            var list=(IList)Type.GetField("projectiles",Flags).GetValue(domain);var projectileType=Type.GetNestedType("Projectile",BindingFlags.NonPublic);
            Action<int,PlayableOwner,NavPoint,int,int> add=(id,faction,pos,source,target)=>{var shell=Activator.CreateInstance(projectileType);foreach(var item in new[]{Tuple.Create("Id",(object)id),Tuple.Create("Faction",(object)faction),Tuple.Create("Position",(object)pos),Tuple.Create("Owner",(object)source),Tuple.Create("Target",(object)target)})projectileType.GetField(item.Item1).SetValue(shell,item.Item2);list.Add(shell);};
            add(90,PlayableOwner.Enemy,new NavPoint(25,0),3,2);
            add(91,PlayableOwner.Player,new NavPoint(25,0),999,2);
            add(92,PlayableOwner.Enemy,new NavPoint(-20,0),3,1);
            var view=View();CollectionAssert.AreEquivalent(new[]{91,92},view.Projectiles.Select(p=>p.Id));
            Assert.Zero(view.Projectiles.Single(p=>p.Id==91).TargetId);Assert.True(view.Projectiles.All(p=>p.OwnerId==0));
            Assert.AreEqual(1,view.Projectiles.Single(p=>p.Id==92).TargetId);
            Nav.Crowd.Remove(3);Nav.Crowd.Add(3,new NavPoint(29,25));Field(2,"Position",new NavPoint(25,25));Field(2,"Health",1d);
            var after=View();CollectionAssert.AreEqual(view.Projectiles.Select(p=>p.Id+":"+p.OwnerId+":"+p.TargetId),after.Projectiles.Select(p=>p.Id+":"+p.OwnerId+":"+p.TargetId));
        }
        [SetUp] public void Setup(){domain=Activator.CreateInstance(Type,Flags,null,new object[]{PlayableProfile.Default,1L},null);}
        private PlayableCommandStatus ApplyOwner(PlayableCommand command)=> (PlayableCommandStatus)Call("Apply",command,null);
        [Test] public void EnemyOwnerObservationAndEconomyActionUseOnlyEnemyProjection()
        {
            var player=View();var enemy=View(PlayableOwner.Enemy);
            var playerObservation=PlayableAiObservation.From(player);var enemyObservation=PlayableAiObservation.From(enemy);
            Assert.AreEqual("player-1",playerObservation.OwnerId);Assert.AreEqual("enemy-1",enemyObservation.OwnerId);
            Assert.AreNotEqual(playerObservation.Identity,enemyObservation.Identity);
            Assert.False(enemyObservation.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.PrivateState!=null));
            Assert.False(enemyObservation.Entities.Any(e=>e.Owner==PlayableOwner.Player&&e.CurrentOrder!=null));
            var policy=new PlayableAiEconomicLivenessPolicy(ownerId:"enemy-1");
            var action=policy.TryPlan(enemyObservation);
            Assert.NotNull(action);Assert.AreEqual("enemy-1",action.PlayerId);
            Assert.AreEqual(enemyObservation.Seed,action.Seed);
            Assert.AreEqual(PlayableAiOpeningComposition.SourceIdentity,action.SourceIdentity);
            Assert.AreEqual(2,action.SiteId);
            Assert.Throws<ArgumentException>(()=>policy.TryPlan(playerObservation));
            policy.ObserveReceipt(new PlayableAiTraceRecord(enemyObservation.Identity,action.ActionId,0,9,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"foreign",ownerId:"player-1",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));
            Assert.True(policy.HasPendingObligation,"A foreign-owner receipt cannot clear the enemy policy.");
            var command=new PlayableCommand(enemy.Generation,1,action.PlayerId,action.Kind,action.CopyEntityIds(),action.Target,
                siteId:action.SiteId,slotId:action.SlotId,buildingKind:action.BuildingKind,parentId:action.ParentId);
            Assert.AreEqual(PlayableCommandStatus.Applied,ApplyOwner(command));
            policy.ObserveReceipt(new PlayableAiTraceRecord(enemyObservation.Identity,action.ActionId,0,1,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));
            Assert.False(policy.HasPendingObligation);
            var after=View(PlayableOwner.Enemy);Assert.True(after.Buildings.Any(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.Factory));
            Assert.False(View().Buildings.Any(b=>b.Owner==PlayableOwner.Enemy&&b.PrivateState!=null));
        }
        [Test] public void EnemyTacticalCommandsRespectUnitOwnershipAndFog()
        {
            var enemy=View(PlayableOwner.Enemy);var own=enemy.Entities.First(e=>e.Owner==PlayableOwner.Enemy);
            var full=(PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            var playerId=full.Entities.Single(e=>e.Owner==PlayableOwner.Player).Id;
            var geometry=(NavGeometry)Type.GetProperty("Geometry",Flags).GetValue(domain);
            var target=new[]{new NavPoint(own.Position.X+4,own.Position.Z),new NavPoint(own.Position.X,own.Position.Z+4),new NavPoint(own.Position.X,own.Position.Z-4)}.First(p=>geometry.IsFree(p,PlayableProfile.Default.TankCollisionRadius));
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,ApplyOwner(new PlayableCommand(1,1,"enemy-1",PlayableCommandKind.Move,new[]{playerId},target)));
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,ApplyOwner(new PlayableCommand(1,2,"enemy-1",PlayableCommandKind.Attack,new[]{own.Id},targetId:playerId)));
            Assert.AreEqual(PlayableCommandStatus.Applied,ApplyOwner(new PlayableCommand(1,3,"enemy-1",PlayableCommandKind.Move,new[]{own.Id},target)));
            Assert.AreEqual(PlayableOwner.Enemy,View(PlayableOwner.Enemy).Entities.Single(e=>e.Id==own.Id).CurrentOrder.Owner);
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,ApplyOwner(new PlayableCommand(1,4,"enemy-1",PlayableCommandKind.Stop,new[]{own.Id,playerId})));
            Assert.NotNull(View(PlayableOwner.Enemy).Entities.Single(e=>e.Id==own.Id).CurrentOrder);
            Assert.AreEqual(PlayableCommandStatus.Applied,ApplyOwner(new PlayableCommand(1,5,"enemy-1",PlayableCommandKind.Stop,new[]{own.Id})));
            Assert.IsNull(View(PlayableOwner.Enemy).Entities.Single(e=>e.Id==own.Id).CurrentOrder);
        }
        [Test] public void EnemyResearchAdmissionUsesOwnCenterAndPrivateQueue()
        {
            Call("AddBuilding",PlayableOwner.Enemy,PlayableBuildingKind.ScientificCenter,new NavPoint(25,8),true);
            var enemy=View(PlayableOwner.Enemy);
            var center=enemy.Buildings.Single(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.ScientificCenter);
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,ApplyOwner(new PlayableCommand(1,1,"player-1",PlayableCommandKind.QueueResearch,new[]{center.Id})));
            Assert.AreEqual(PlayableCommandStatus.Applied,ApplyOwner(new PlayableCommand(1,2,"enemy-1",PlayableCommandKind.QueueResearch,new[]{center.Id})));
            Assert.True(View(PlayableOwner.Enemy).Buildings.Single(b=>b.Id==center.Id).PrivateState.Research.Any());
            Assert.False(View().Buildings.Any(b=>b.Id==center.Id&&b.PrivateState!=null));
        }
        [Test] public void StrategicPoliciesRejectForeignOwnerState()
        {
            var enemy=PlayableAiObservation.From(View(PlayableOwner.Enemy));
            var player=PlayableAiObservation.From(View());
            var opening=PlayableAiOpeningComposition.Initialize(enemy.Seed,"enemy-1");
            var mission=new PlayableAiMissionDefensePolicy(PlayableProfile.Default,ownerId:"enemy-1");
            var midgame=new PlayableAiMidgameStrategyPolicy(PlayableProfile.Default,"enemy-1");
            var artillery=new PlayableAiArtillerySupportPolicy(PlayableProfile.Default,ownerId:"enemy-1");
            mission.TryPlan(enemy,opening);
            var state=midgame.Review(enemy,opening,mission.Mission);
            Assert.AreEqual("enemy-1",state.OwnerId);
            artillery.TryPlan(enemy,opening,state);
            Assert.Throws<ArgumentException>(()=>mission.TryPlan(player,opening));
            Assert.Throws<ArgumentException>(()=>midgame.Review(player,opening));
            Assert.Throws<ArgumentException>(()=>artillery.TryPlan(player,opening,state));
        }
        [Test] public void LivenessPoliciesAndDiagnosticIdentityStayOwnerBound()
        {
            var enemy=PlayableAiObservation.From(View(PlayableOwner.Enemy));
            var player=PlayableAiObservation.From(View());
            var economy=new PlayableAiEconomicLivenessPolicy(ownerId:"enemy-1");
            var production=new PlayableAiProductionLivenessPolicy(profile:PlayableProfile.Default,ownerId:"enemy-1");
            var scout=new PlayableAiScoutLivenessPolicy(ownerId:"enemy-1");
            var research=new PlayableAiResearchLivenessPolicy(ownerId:"enemy-1");
            Assert.Throws<ArgumentException>(()=>economy.TryPlan(player));
            Assert.Throws<ArgumentException>(()=>production.TryPlan(player));
            Assert.Throws<ArgumentException>(()=>scout.TryPlan(player));
            var readiness=new PlayableAiResearchReadiness(player.Generation,player.Tick,-1,false,1,0,0,1);
            Assert.Throws<ArgumentException>(()=>research.TryPlan(player,readiness));
            Assert.AreEqual("enemy-1",scout.TryPlan(enemy)?.PlayerId);
            var identity=new PlayableAiDiagnosticIdentity("owner-probe",PlayableAiOpeningComposition.SourceIdentity,enemy);
            Assert.True(identity.Matches(enemy));Assert.False(identity.Matches(player));
            var colonProfileIdentity="observation-13:local:main@1:enemy-1:1:2:3:7";
            Assert.AreEqual("enemy-1",new PlayableAiTraceRecord(colonProfileIdentity,1,0,0,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity).OwnerId);
        }
        [Test] public void RouteProofUsesCurrentNavigationGeometryAndOwnerVisibleSemanticAnchor()
        {
            var start=View().Entities.Single().Position;
            var geometry=(NavGeometry)Type.GetProperty("Geometry",Flags).GetValue(domain);
            var target=Enumerable.Range(4,6).Select(n=>new NavPoint(start.X+n,start.Z)).First(p=>geometry.IsFree(p,PlayableProfile.Default.ExplorerCollisionRadius)&&View().Vision.IsVisible(p));
            int ally=Spawn(target.X,target.Z);
            int unit=View().Entities.Single(e=>e.Id!=ally).Id;
            var request=new PlayableRouteRequest(unit,PlayableRouteTargetKind.FriendlyAnchor,ally,1,0,start,PlayableProfile.Default.ExplorerCollisionRadius,target);
            var observed=PlayableAiObservation.From(RouteView(request));
            var proof=observed.RouteProofs.Single();
            Assert.AreEqual(unit,proof.UnitId);Assert.AreEqual(PlayableOwner.Player,proof.Owner);
            Assert.AreEqual(observed.Generation,proof.Generation);Assert.AreEqual(observed.Tick,proof.Tick);
            Assert.AreEqual(start.X,proof.Origin.X);Assert.AreEqual(target.X,proof.Target.X);
            Assert.AreEqual(PlayableProfile.Default.ExplorerCollisionRadius,proof.Radius);
            Assert.AreEqual(target.X,proof.Path.Last().X);
            Assert.IsEmpty(RouteView(new PlayableRouteRequest(unit,PlayableRouteTargetKind.FriendlyAnchor,ally,1,0,start,proof.Radius+1,target)).RouteProofs,"A changed footprint invalidates admission.");
            Assert.IsEmpty(RouteView(new PlayableRouteRequest(unit,PlayableRouteTargetKind.FriendlyAnchor,ally,2,0,start,proof.Radius,target)).RouteProofs,"A foreign generation cannot refresh evidence.");
            Assert.IsEmpty(RouteView(new PlayableRouteRequest(unit,PlayableRouteTargetKind.FriendlyAnchor,ally,1,0,start,proof.Radius,new NavPoint(target.X+1,target.Z))).RouteProofs,"The semantic target point must still match.");
            Assert.IsEmpty(PlayableAiObservation.From(RouteView(new PlayableRouteRequest(unit,PlayableRouteTargetKind.VisibleEnemy,3,1,0,start,proof.Radius,new NavPoint(0,0)))).RouteProofs,"A hidden guessed enemy id is not a target.");
            var barrier=new NavObstacle((start.X+target.X)/2-.1,-geometry.HalfExtent,(start.X+target.X)/2+.1,geometry.HalfExtent);
            var blocked=new NavGeometry(geometry.HalfExtent,new[]{barrier},geometry.Revision+1);
            Type.GetProperty("Geometry",Flags).SetValue(domain,blocked);Nav.ChangeGeometry(blocked);
            Assert.IsEmpty(PlayableAiObservation.From(RouteView(request)).RouteProofs,"The real navigation geometry blocks this leg.");
        }
        [Test] public void EnemyRouteProofUsesOnlyItsOwnVisibleAnchor()
        {
            var enemy=View(PlayableOwner.Enemy);
            var unit=enemy.Entities.First(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Explorer);
            var target=Enumerable.Range(4,6).Select(n=>new NavPoint(unit.Position.X,unit.Position.Z+n))
                .First(p=>Nav.Crowd.CanPlace(p,PlayableProfile.Default.ExplorerCollisionRadius)&&enemy.Vision.IsVisible(p));
            int ally=(int)Call("SpawnUnit",target,PlayableOwner.Enemy,PlayableEntityKind.Explorer);
            var request=new PlayableRouteRequest(unit.Id,PlayableRouteTargetKind.FriendlyAnchor,ally,enemy.Generation,enemy.Tick,unit.Position,PlayableProfile.Default.ExplorerCollisionRadius,target);
            PlayableSnapshot route(PlayableOwner owner)=>(PlayableSnapshot)Call("PlayerSnapshotWithRoutes",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner,new[]{request});
            var proof=PlayableAiObservation.From(route(PlayableOwner.Enemy)).RouteProofs.Single();
            Assert.AreEqual(PlayableOwner.Enemy,proof.Owner);Assert.AreEqual(unit.Id,proof.UnitId);
            Assert.IsEmpty(route(PlayableOwner.Player).RouteProofs,"A player projection cannot borrow an enemy route proof.");
        }
        [Test] public void RouteProofRejectsStaleTickAndForeignOwnerProjection()
        {
            var start=View().Entities.Single().Position;
            var geometry=(NavGeometry)Type.GetProperty("Geometry",Flags).GetValue(domain);
            var target=Enumerable.Range(4,6).Select(n=>new NavPoint(start.X+n,start.Z)).First(p=>geometry.IsFree(p,PlayableProfile.Default.ExplorerCollisionRadius)&&View().Vision.IsVisible(p));
            int ally=Spawn(target.X,target.Z),unit=View().Entities.Single(e=>e.Id!=ally).Id;
            var request=new PlayableRouteRequest(unit,PlayableRouteTargetKind.FriendlyAnchor,ally,1,0,start,PlayableProfile.Default.ExplorerCollisionRadius,target);
            var proof=RouteView(request).RouteProofs.Single();
            Assert.IsEmpty(((PlayableSnapshot)Call("PlayerSnapshotWithRoutes",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Enemy,new[]{request})).RouteProofs);
            Call("Step",1d/30d);
            Assert.IsEmpty(RouteView(request).RouteProofs,"An old query cannot be refreshed across authority ticks.");
            var current=View();
            var stale=new PlayableSnapshot(current.ProfileId,current.ProfileRevision,current.Generation,current.Seed,current.Sequence,current.Tick,current.Status,current.Paused,current.Outcome,current.Credits,current.Geometry,current.Entities.ToArray(),current.Buildings.ToArray(),current.Projectiles.ToArray(),current.Metrics,current.Failure,vision:current.Vision,routeProofs:new[]{proof});
            Assert.IsEmpty(PlayableAiObservation.From(stale).RouteProofs);
        }
        [Test] public void VisibleEnemyUnitRouteBindsCurrentSemanticTarget()
        {
            var start=View().Entities.Single().Position;
            var geometry=(NavGeometry)Type.GetProperty("Geometry",Flags).GetValue(domain);
            var target=Enumerable.Range(4,6).Select(n=>new NavPoint(start.X+n,start.Z)).First(p=>geometry.IsFree(p,PlayableProfile.Default.TankCollisionRadius)&&View().Vision.IsVisible(p));
            Assert.IsTrue(Nav.Crowd.Remove(3));
            Nav.Crowd.Add(3,target,PlayableProfile.Default.TankCollisionRadius,PlayableProfile.Default.TankSpeed,PlayableProfile.Default.TankTurnSpeed);
            var visible=View();Assert.IsTrue(visible.Entities.Any(e=>e.Id==3&&e.Owner==PlayableOwner.Enemy));
            var unit=visible.Entities.Single(e=>e.Owner==PlayableOwner.Player);
            var request=new PlayableRouteRequest(unit.Id,PlayableRouteTargetKind.VisibleEnemy,3,visible.Generation,visible.Tick,unit.Position,PlayableProfile.Default.ExplorerCollisionRadius,target);
            var proof=PlayableAiObservation.From(RouteView(request)).RouteProofs.Single();
            Assert.AreEqual(PlayableRouteTargetKind.VisibleEnemy,proof.Kind);Assert.AreEqual(3,proof.TargetId);
            Assert.AreEqual(target.X,proof.Path.Last().X);
        }
        [Test] public void InitialPlayerViewHasNoHiddenEnemiesOrDynamicNavigationObstacles()
        {
            var player=View();var enemy=View(PlayableOwner.Enemy);
            Assert.AreEqual(1,player.Entities.Count);Assert.AreEqual(PlayableEntityKind.Explorer,player.Entities[0].Kind);Assert.AreEqual(PlayableOwner.Player,player.Entities[0].Owner);Assert.AreEqual(1,player.Buildings.Count);Assert.AreEqual(PlayableOwner.Player,player.Buildings[0].Owner);
            Assert.AreEqual(3,enemy.Entities.Count);Assert.AreEqual(1,enemy.Buildings.Count);Assert.AreNotEqual(player.Vision.Team,enemy.Vision.Team);
            Assert.False(player.DiscoveredSites.Any(s=>s.Id==2));Assert.False(player.Sites.Any(s=>s.Site.Id==2));
            var full=(NavGeometry)Type.GetProperty("Geometry",Flags).GetValue(domain);Assert.Greater(full.Obstacles.Count,player.Geometry.Obstacles.Count);
        }
        [Test] public void HiddenConstructionAndHealthDoNotChangePlayerInformation()
        {
            var before=View();Field(2,"Health",1d);Field(2,"RepeatTank",true);Field(2,"Rally",new NavPoint(20,20));
            var args=new object[]{2,1,PlayableBuildingKind.Factory,2,PlayableOwner.Enemy,null};
            Assert.AreEqual(PlayableCommandStatus.Applied,Type.GetMethod("BuildAt",Flags).Invoke(domain,args));Call("AdvanceFoundations");
            var after=View();Assert.AreSame(before.Geometry,after.Geometry);Assert.AreSame(before.Vision,after.Vision);
            CollectionAssert.AreEqual(before.Entities.Select(e=>e.Id),after.Entities.Select(e=>e.Id));
            CollectionAssert.AreEqual(before.Buildings.Select(b=>b.Id),after.Buildings.Select(b=>b.Id));
            CollectionAssert.AreEqual(before.Sites.Select(s=>s.CenterId),after.Sites.Select(s=>s.CenterId));
        }
        [Test] public void VisibleEnemyDoesNotExposeQueueRallyOrTargets()
        {
            Spawn(10,-4);Field(2,"RepeatTank",true);Field(2,"Rally",new NavPoint(30,30));
            var b=View().Buildings.Single(x=>x.Id==2);Assert.IsNull(b.PrivateState);Assert.Zero(b.QueueCount);Assert.Zero(b.Rally.X);
            Assert.True(View().Entities.Where(x=>x.Owner==PlayableOwner.Enemy).All(x=>x.TargetId==0));
        }
        [Test] public void DomainMemorySurvivesHiddenDeathUntilRevisited()
        {
            int scout=Spawn(10,-4);Assert.True(View().Vision.KnownBuildings.Any(b=>b.Id==2));
            Call("Damage",scout,100000);var hidden=View();Call("Damage",2,100000);
            Assert.True(View().Vision.KnownBuildings.Any(b=>b.Id==2));Assert.IsEmpty(View().Buildings.Where(b=>b.Id==2));
            Spawn(10,-4);Assert.False(View().Vision.KnownBuildings.Any(b=>b.Id==2));Assert.True(hidden.Vision.KnownBuildings.Any(b=>b.Id==2));
        }
        [Test] public void ExplicitTargetMustBeVisibleAtApplication()
        {
            int near=Spawn(-10,0);Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Attack(near,3));
            int scout=Spawn(10,-4);Assert.AreEqual(PlayableCommandStatus.Applied,Attack(scout,3));
            Call("Damage",scout,100000);Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Attack(near,3));
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Attack(near,0));
        }
        [Test] public void LostTargetCancelsPendingChaseWithoutAcquiringHiddenReplacement()
        {
            int unit=Spawn(4,-4);Assert.AreEqual(PlayableCommandStatus.Applied,Attack(unit,3));Call("Step",1d/30d);
            Nav.Crowd.Remove(3);Nav.Crowd.Add(3,new NavPoint(28,-20));Call("Step",1d/30d);
            Assert.Zero(View().Entities.Single(e=>e.Id==unit).TargetId);Assert.False(Nav.IsPending(unit));
        }
        [Test] public void ScriptedAssaultUsesOneGroundOrderPerUnitAndIgnoresHiddenHqPosition()
        {
            Field(1,"Position",new NavPoint(-28,-20));
            Type.GetField("elapsed",Flags).SetValue(domain,PlayableProfile.Default.EnemyAdvanceDelaySeconds+1);
            for(int i=0;i<40;i++)Call("Step",1d/30d);
            Assert.AreEqual(3,Nav.Requests.Count,"A waiting route must not be replaced every tick.");
            while(Nav.Requests.TryDequeue(out var request))
            {
                Assert.AreEqual(PlayableProfile.Default.PlayerHeadquartersX+PlayableProfile.Default.DefenderOffsetX,request.Goal.X);
                Assert.AreEqual(PlayableProfile.Default.HeadquartersZ+PlayableProfile.Default.DefenderOffsetZ,request.Goal.Z);
            }
            Assert.True(View(PlayableOwner.Enemy).Entities.All(e=>e.TargetId==0));
        }
        [Test] public void OutpostLifecycleChangesSourcesAndNewSessionClearsHistory()
        {
            int scout=Spawn(-16,20);for(int i=0;i<155;i++)Call("Step",1d/30d);Call("Damage",scout,100000);
            var args=new object[]{3,0,PlayableBuildingKind.Outpost,0,PlayableOwner.Player,null};
            Assert.AreEqual(PlayableCommandStatus.Applied,Type.GetMethod("BuildAt",Flags).Invoke(domain,args));
            var pending=View();Assert.False(pending.Vision.Sources.Any(s=>s.Position.Z==20));
            Call("Step",1d/30d);var foundation=View();Assert.True(foundation.Vision.Sources.Any(s=>s.Position.Z==20&&s.Radius==9));
            var probe=new NavPoint(0,20);Assert.False(foundation.Vision.IsVisible(probe));
            for(int i=0;i<601;i++)Call("Step",1d/30d);
            var ready=View();Assert.True(ready.Vision.IsVisible(probe));int center=ready.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Outpost).Id;
            Call("Damage",center,100000);Assert.False(View().Vision.IsVisible(probe));
            var cells=View().Vision.DiscoveredCells.Count;Setup();Assert.Less(View().Vision.DiscoveredCells.Count,cells);
        }
        [Test] public void PublicBuildingRecordCannotRetainPrivateFieldsWhenDisabled()
        {
            var b=new PlayableBuildingSnapshot(1,PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(1,2),100,1,23,.8,new NavPoint(20,30),includePrivateState:false);
            Assert.IsNull(b.PrivateState);Assert.Zero(b.QueueCount);Assert.Zero(b.ProductionProgress);Assert.Zero(b.Rally.X);Assert.Zero(b.Rally.Z);
        }
        [Test] public void RuntimePublishesSafeViewAndSeparateTrustedNavigationGeometry()
        {
            var runtime=new PlayableRuntime(PlayableProfile.Default,9,42,false);
            try
            {
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>1,3000));
                Assert.IsNotNull(runtime.Latest.Vision);Assert.AreEqual(1,runtime.Latest.Buildings.Count);
                Assert.Greater(runtime.NavigationGeometry.Obstacles.Count,runtime.Latest.Geometry.Obstacles.Count);
            }
            finally{runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));}
        }
    }
}
