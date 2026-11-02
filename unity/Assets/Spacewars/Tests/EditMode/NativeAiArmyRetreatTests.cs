using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiArmyTests
    {
        private static PlayableAiObservation A5Observation(string change="local",long tick=100,bool move=false,bool arrived=false)
        {
            var p=PlayableProfile.Default;var old=A4Observation(tick,effective:true);var goal=new NavPoint(-25,0);
            var entities=old.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>new PlayableEntitySnapshot(e.Id,e.Owner,e.Kind,arrived&&change!="unphysical"?new NavPoint(-25,(e.Id-1)*2):e.Position,e.Health,false,0,0,0,
                move&&change!="cleared"&&change!="unphysical"?new PlayableTacticalOrderSnapshot(e.Id,e.Owner,71,2,100,PlayableTacticalOrderKind.Move,goal,0):move?null:e.CurrentOrder,
                navigationOutcome:arrived?NavigationOutcome.Arrived:NavigationOutcome.Moving,
                orderStamp:move?new PlayableOrderStamp(e.Id,e.Owner,71,2,change=="sequence"?3:2,100,change=="human"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Move,change=="source"?"foreign":PlayableAiOpeningComposition.SourceIdentity,2,2):null)).ToList();
            entities.AddRange(Enumerable.Range(101,6).Select(id=>new PlayableEntitySnapshot(id,change=="allied"?PlayableOwner.Player:PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(change=="remote"?40:6,(id-101)*.5),p.TankHealth,false,0,0,0)));
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(change=="hidden"?new NavPoint(-30,0):default,change=="hidden"?5:50)},Array.Empty<KnownBuilding>());
            var proofs=entities.Where(e=>e.Id<=3).SelectMany(u=>entities.Where(e=>e.Owner==PlayableOwner.Enemy).Select(e=>new PlayableRouteProof(u.Id,u.Owner,71,tick,1,u.Position,PlayableUnitRules.Radius(p,u.Kind),PlayableRouteTargetKind.VisibleEnemy,e.Id,e.Position,e.Position,new[]{e.Position}))).ToList();
            if(change!="missing")foreach(var u in entities.Where(e=>e.Id<=3))
            {
                var path=change=="unsafe"?new[]{new NavPoint(7,0),goal}:new[]{goal};
                proofs.Add(new PlayableRouteProof(u.Id,u.Owner,change=="stale"?70:71,tick,1,u.Position,PlayableUnitRules.Radius(p,u.Kind),PlayableRouteTargetKind.FriendlyAnchor,move&&change!="cleared"&&change!="unphysical"?u.Id:50,move&&change!="cleared"&&change!="unphysical"?goal:old.Buildings.Single(b=>b.Id==50).Position,goal,path));
            }
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,old.Seed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,entities.ToArray(),old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:proofs.ToArray()));
        }
        private static PlayableAiTraceRecord A5Receipt(string change="valid")=>new PlayableAiTraceRecord("withdrawal",2,100,2,100,change=="unapplied"?PlayableAiDeliveryStatus.Cancelled:PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"fixture",policy:change=="policy"?"foreign":AiArmyPlanner.Policy,kind:PlayableCommandKind.Move,ownerId:"player-1",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity,receiptIdentity:new AiReceiptIdentity(71,"player-1",1,2));
        [TestCase("safe",true)][TestCase("inside-fire",false)][TestCase("occupied",false)]
        public void Retreat_CertifiedFirstLegLeavesThreatAndKeepsPlacementSpacing(string mode,bool prefix)
        {
            var p=PlayableProfile.Default;var r=Registry();var planner=A4Mission(r);planner.SetPhase(r,AiArmyPhase.Regrouping,100);((AiArmyMissionState)Field(planner,"state")).Reason="withdrawal";var old=A5Observation();
            var first=mode=="inside-fire"?new NavPoint(-2,0):new NavPoint(-12,4);var home=new NavPoint(-25,0);
            var units=old.Entities.Select(u=>u.Id==2&&mode=="occupied"?new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,first,u.Health,false,0,0,0,u.CurrentOrder):u).ToArray();
            var routes=old.RouteProofs.Select(route=>route.UnitId==1&&route.Kind==PlayableRouteTargetKind.FriendlyAnchor?
                new PlayableRouteProof(1,route.Owner,route.Generation,route.Tick,route.GeometryRevision,route.Origin,route.Radius,route.Kind,route.TargetId,route.Target,home,new[]{first,home}):route).ToArray();
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:routes,activeProfile:p));
            var army=r.Capture().Armies.Single();var action=AiTacticalExecutor.Withdrawal(o,army,p,AiProfile.Initial,99);
            Assert.NotNull(action);CollectionAssert.AreEqual(new[]{1},action.EntityIds);if(mode=="occupied")
            {
                Assert.AreNotEqual(first,action.Target);Assert.AreNotEqual(home,action.Target,"A free stand-off on the certified straight segment avoids the occupied corner");
                Assert.GreaterOrEqual(AiPositionDevelopmentPlanner.Distance(action.Target,first),Math.Max(p.Navigation.ArrivalSlotSpacing,4*p.TankCollisionRadius+2*p.Navigation.ArrivalTolerance+2*p.Navigation.LocalGridCell));
                Assert.False(AiTacticalExecutor.WithdrawalLeg(o,o.RouteProofs.Single(route=>route.UnitId==1&&route.Kind==PlayableRouteTargetKind.FriendlyAnchor),p,first),"Original occupied endpoint remains inadmissible");
            }
            else Assert.AreEqual(prefix?first:home,action.Target);
            Assert.True(o.RouteProofs.Any(route=>route.UnitId==1&&AiTacticalExecutor.WithdrawalLeg(o,route,p,action.Target)),"Selected leg retains the current full-home certificate and escape safety");
            if(mode!="inside-fire")
            {
                var selected=planner.ProposeWithRecords(o,A4Opening(),p,AiProfile.Initial,r,null);Assert.NotNull(selected);Assert.AreEqual(action.Target,selected.Target);planner.Commit(o,selected,r);
                Assert.AreEqual(AiArmyPhase.Retreating,planner.Capture().Phase);Assert.AreEqual(action.Target,r.Capture().Armies.Single().Anchor.Value);
                using var stream=new System.IO.MemoryStream();using(var w=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))planner.WriteState(w);var bytes=stream.ToArray();stream.Position=0;var cold=new AiArmyPlanner(o.OwnerId,o.Generation);using(var reader=new System.IO.BinaryReader(stream,System.Text.Encoding.UTF8,true))cold.ReadState(reader,o.Tick,r);
                using var copy=new System.IO.MemoryStream();using(var w=new System.IO.BinaryWriter(copy,System.Text.Encoding.UTF8,true))cold.WriteState(w);CollectionAssert.AreEqual(bytes,copy.ToArray());
            }
        }
        [Test] public void Retreat_ActualBlockedCornerOperandsChooseAFreeCertifiedStandOff()
        {
            var p=PlayableProfile.Default;var old=A5Observation();var from=new NavPoint(15.436446056238761,18.860182047590193);var corner=new NavPoint(-12.4,6.8);var occupied=new NavPoint(-8.970065708490504,6.171564554667022);var home=new NavPoint(-14.687066799654088,6.573003747272123);
            var units=new[]{new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,from,100,false,0,0,0),new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,occupied,100,false,0,0,0),new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Hawk,new NavPoint(18,24),60,false,0,0,0)};
            var route=new PlayableRouteProof(1,PlayableOwner.Player,71,100,1,from,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,50,old.Buildings.Single(b=>b.Id==50).Position,home,new[]{corner,home});
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:new[]{route},activeProfile:p));
            Assert.False(AiTacticalExecutor.WithdrawalLeg(o,route,p,corner));var goal=AiTacticalExecutor.WithdrawalDestination(o,route,p);
            Assert.AreNotEqual(home,goal);Assert.AreNotEqual(corner,goal);Assert.True(AiTacticalExecutor.WithdrawalLeg(o,route,p,goal));Assert.True(AiTacticalExecutor.WithdrawalPath(o,from,new[]{goal},p.TankCollisionRadius,p));
            double spacing=Math.Max(p.Navigation.ArrivalSlotSpacing,4*p.TankCollisionRadius+2*p.Navigation.ArrivalTolerance+2*p.Navigation.LocalGridCell);Assert.GreaterOrEqual(AiPositionDevelopmentPlanner.Distance(goal,occupied),spacing);
        }
        [Test] public void Retreat_SafeOriginUsesFinalHomeGoalInsteadOfAnotherIntermediate()
        {
            var p=PlayableProfile.Default;var o=A5Observation("remote");var unit=o.Entities.First(u=>u.Id==1);
            var home=o.Buildings.Single(b=>b.Id==50);var goal=new NavPoint(-25,0);
            var route=new PlayableRouteProof(unit.Id,o.Owner,o.Generation,o.Tick,1,unit.Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,home.Id,home.Position,goal,new[]{new NavPoint(-12,4),goal});
            Assert.True(AiTacticalExecutor.WithdrawalRoute(o,route,p));
            Assert.True(AiTacticalExecutor.WithdrawalPath(o,unit.Position,new[]{unit.Position},route.Radius,p));
            Assert.AreEqual(goal,AiTacticalExecutor.WithdrawalDestination(o,route,p),"A safe origin needs the final certified home goal, not another escape-prefix installation.");
        }
        [TestCase("blocking",true)][TestCase("off-corridor",false)][TestCase("human",false)]
        [TestCase("foreign-policy",false)][TestCase("unapplied",false)][TestCase("pending-mover",false)]
        [TestCase("stale-proof",false)][TestCase("unsafe-home",false)][TestCase("static-defense",false)][TestCase("missing-core",false)][TestCase("recovering",false)][TestCase("assembly-anchor",false)]
        [TestCase("in-flight-anchor",false)][TestCase("unapplied-wave",true)][TestCase("human-wave",true)][TestCase("foreign-wave",true)][TestCase("other-leader-wave",true)]
        [TestCase("moving-peer",true)][TestCase("same-direction",false)][TestCase("occupied-home",false)][TestCase("path-body",false)]
        public void Retreat_OriginalPeerClearsOnlyAProvenOwnMovingCorridor(string changed,bool expected)
        {
            bool anchor=changed=="assembly-anchor"||changed.EndsWith("-wave",StringComparison.Ordinal)||changed=="in-flight-anchor";
            var p=PlayableProfile.Default;var old=A5Observation("remote");var end=new NavPoint(-25,0);var peerAt=new NavPoint(-12,changed=="off-corridor"?6:0);var alternate=new NavPoint(-25,6);
            PlayableOrderStamp Stamp(int id)=>new PlayableOrderStamp(id,old.Owner,71,2,2,100,changed=="human"&&id==2?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,2,2);
            var move=new PlayableTacticalOrderSnapshot(1,old.Owner,71,2,100,PlayableTacticalOrderKind.Move,end,0);
            var pending=changed=="pending-mover"?new PlayableUnitQueueSnapshot(null,new PlayableQueuedOrderSnapshot(1,1,2,2,PlayableCommandKind.Move,new NavLocation(end,NavLocation.FlatSurface),0,aiActionId:2),Array.Empty<PlayableQueuedOrderSnapshot>(),null,pendingAiRouteViable:true):null;
            var mover=new PlayableEntitySnapshot(1,old.Owner,PlayableEntityKind.Tank,default,p.TankHealth,true,0,0,0,move,navigationOutcome:NavigationOutcome.Moving,orderStamp:Stamp(1),queue:pending);
            bool peerMoving=changed=="moving-peer"||changed=="same-direction";var peerGoal=changed=="same-direction"?end:new NavPoint(5,0);
            var peer=new PlayableEntitySnapshot(2,old.Owner,PlayableEntityKind.Tank,peerAt,p.TankHealth,peerMoving,0,0,0,peerMoving?new PlayableTacticalOrderSnapshot(2,old.Owner,71,2,100,PlayableTacticalOrderKind.Move,peerGoal,0):null,navigationOutcome:peerMoving?NavigationOutcome.Moving:NavigationOutcome.Arrived,orderStamp:Stamp(2));
            var third=new PlayableEntitySnapshot(3,old.Owner,PlayableEntityKind.Tank,changed=="occupied-home"?alternate:changed=="path-body"?new NavPoint(-20,3.7):new NavPoint(-2,8),changed=="missing-core"?0:p.TankHealth,false,0,0,0);
            var home=old.Buildings.Single(b=>b.Id==50);var proofs=new[]{
                new PlayableRouteProof(1,old.Owner,71,changed=="stale-proof"?99:100,1,mover.Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,1,end,end,new[]{new NavPoint(-12,0),end}),
                new PlayableRouteProof(2,old.Owner,71,100,1,peer.Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,50,home.Position,peer.Position,new[]{peer.Position}),
                new PlayableRouteProof(2,old.Owner,71,100,1,peer.Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,50,home.Position,alternate,new[]{alternate})};
            if(peerMoving)proofs=proofs.Concat(new[]{new PlayableRouteProof(2,old.Owner,71,100,1,peer.Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,2,peerGoal,peerGoal,new[]{peerGoal})}).ToArray();
            var enemy=new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,alternate,p.TankHealth,false,0,0,0);
            var followAt=new NavPoint(peerAt.X-(changed=="assembly-anchor"?p.FollowDistance:10),peerAt.Z);
            var follower=new PlayableEntitySnapshot(4,old.Owner,PlayableEntityKind.Tank,followAt,p.TankHealth,false,0,0,0,new PlayableTacticalOrderSnapshot(4,old.Owner,71,3,100,PlayableTacticalOrderKind.Follow,default,changed=="other-leader-wave"?3:2),orderStamp:new PlayableOrderStamp(4,old.Owner,71,3,3,100,changed=="human-wave"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Follow,PlayableAiOpeningComposition.SourceIdentity,3,3));
            var units=new[]{mover,peer,third}.Concat(changed=="unsafe-home"?new[]{enemy}:anchor?new[]{follower}:Array.Empty<PlayableEntitySnapshot>()).ToArray();
            if(anchor)proofs=proofs.Concat(new[]{new PlayableRouteProof(4,old.Owner,71,100,1,followAt,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,2,followAt,followAt,new[]{followAt})}).ToArray();
            var buildings=old.Buildings.Concat(changed=="static-defense"?new[]{new PlayableBuildingSnapshot(500,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,alternate,100,1,0,0,default)}:Array.Empty<PlayableBuildingSnapshot>()).ToArray();
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:proofs,activeProfile:p));
            var army=new AiArmyState{OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Phase=changed=="recovering"?AiArmyPhase.Recovering:AiArmyPhase.Retreating,Members=new[]{1,2,3},Reinforcements=Array.Empty<int>(),LeaderId=1,MinimumViableForce=3,CreatedTick=10,ProgressTick=20};
            var records=new[]{A5Receipt(changed=="foreign-policy"?"policy":changed=="unapplied"?"unapplied":"valid")};
            if(anchor)
            {
                army.Members=new[]{1,2,3,4};army.Reinforcements=new[]{4};army.LeaderId=2;
                records=records.Concat(new[]{new PlayableAiTraceRecord(o.Identity,3,100,3,100,changed=="unapplied-wave"?PlayableAiDeliveryStatus.Accepted:PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"assembly",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,1,3),changed=="foreign-wave"?"foreign":AiArmyPlanner.Policy,PlayableCommandKind.Follow)}).ToArray();
            }
            var action=AiTacticalExecutor.ClearWithdrawalCorridor(o,army,p,99,records);Assert.AreEqual(expected,action!=null,changed);
            Assert.AreEqual(20,army.ProgressTick);Assert.AreEqual(10,army.CreatedTick);Assert.AreEqual(3,army.MinimumViableForce);Assert.AreEqual(anchor?2:1,army.LeaderId);CollectionAssert.AreEqual(anchor?new[]{1,2,3,4}:new[]{1,2,3},army.Members);
            if(expected)
            {
                CollectionAssert.AreEqual(new[]{2},action.EntityIds);Assert.AreEqual(alternate,action.Target);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);
                var ordinary=AiTacticalExecutor.Withdrawal(o,army,p,AiProfile.Initial,99,records:records);CollectionAssert.AreEqual(action.EntityIds,ordinary.EntityIds);Assert.AreEqual(action.Target,ordinary.Target);
            }
        }
        [TestCase(false,false,2)][TestCase(true,false,1)][TestCase(true,true,2)]
        public void Retreat_RepairRendezvousLeaderPrecedesSafePeersButNotExposedBodies(bool priority,bool exposed,int expected)
        {
            var p=PlayableProfile.Default;var old=A5Observation("remote");
            var units=old.Entities.Where(u=>u.Owner==old.Owner).Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Id==2?50:p.TankHealth,false,0,0,0)).ToList();
            if(exposed)
            {
                // Put only body2 strictly inside the envelope. The exact boundary
                // can round outside when the planner subtracts its two radii.
                double reach=PlayableUnitRules.Range(p,PlayableEntityKind.Tank,false)+2*p.TankCollisionRadius;
                double x=(reach+Math.Sqrt(reach*reach-4))/2; // Other bodies are two metres off this axis.
                units.Add(new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(x,2),p.TankHealth,false,0,0,0));
            }
            var routes=old.RouteProofs.Where(r=>r.Kind==PlayableRouteTargetKind.FriendlyAnchor).ToList();
            if(exposed)
            {
                var u=units.Single(e=>e.Id==2);var e=units.Single(x=>x.Id==101);
                routes.Add(new PlayableRouteProof(u.Id,u.Owner,71,100,1,u.Position,p.TankCollisionRadius,PlayableRouteTargetKind.VisibleEnemy,e.Id,e.Position,e.Position,new[]{e.Position}));
            }
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units.ToArray(),old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:routes.ToArray(),activeProfile:p));
            var army=new AiArmyState{OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Phase=AiArmyPhase.Retreating,Members=new[]{1,2,3},Reinforcements=Array.Empty<int>(),LeaderId=1,MinimumViableForce=3,CreatedTick=10,ProgressTick=20};
            Assert.AreEqual(exposed?1:0,AiTacticalExecutor.LocalThreats(o,army,p).Length,"The control must project an actually observed local contact");
            var action=AiTacticalExecutor.Withdrawal(o,army,p,AiProfile.Initial,99,prioritizeLeader:priority);
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);CollectionAssert.AreEqual(new[]{expected},action.EntityIds);
            Assert.AreEqual(20,army.ProgressTick);Assert.AreEqual(10,army.CreatedTick);Assert.AreEqual(3,army.MinimumViableForce);
        }
        [TestCase("blocked",true)][TestCase("unreachable",true)][TestCase("stopped",true)][TestCase("moving",false)][TestCase("same-goal",false)][TestCase("unowned",false)]
        public void Retreat_TerminalOwnMoveCanChooseADifferentCurrentSafeHomeGoal(string mode,bool replace)
        {
            var p=PlayableProfile.Default;var old=A5Observation("remote",move:true);var army=new AiArmyState{OwnerId=old.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Members=new[]{1,2,3},Reinforcements=Array.Empty<int>(),Phase=AiArmyPhase.Retreating,CreatedTick=10};
            var units=old.Entities.Select(u=>u.Id==1?new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Health,false,0,0,0,u.CurrentOrder,navigationOutcome:mode=="moving"?NavigationOutcome.Moving:mode=="stopped"?NavigationOutcome.Stopped:mode=="unreachable"?NavigationOutcome.Unreachable:NavigationOutcome.Blocked,orderStamp:u.OrderStamp):u).ToArray();
            var goal=mode=="same-goal"?units[0].CurrentOrder.Destination:new NavPoint(-25,4);var home=old.Buildings.Single(b=>b.Id==50);
            var routes=old.RouteProofs.Concat(new[]{new PlayableRouteProof(1,old.Owner,old.Generation,old.Tick,1,units[0].Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,home.Id,home.Position,goal,new[]{goal})}).ToArray();
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,old.Generation,old.Seed,old.Tick,old.Tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:routes));
            var action=AiTacticalExecutor.Withdrawal(o,army,p,AiProfile.Initial,99,records:new[]{A5Receipt(mode=="unowned"?"policy":"valid")});
            Assert.AreEqual(replace,action!=null);if(replace){CollectionAssert.AreEqual(new[]{1},action.EntityIds);Assert.AreEqual(goal,action.Target);Assert.AreNotEqual(units[0].CurrentOrder.Destination,action.Target);}
        }
        [TestCase(AiArmyPhase.Regrouping,false)][TestCase(AiArmyPhase.Retreating,false)][TestCase(AiArmyPhase.Recovering,true)]
        public void Retreat_GenericFollowWaveJoinsOnlyAfterOriginalCoreRecovery(AiArmyPhase phase,bool join)
        {
            var p=PlayableProfile.Default;var r=Registry();var mission=A4Mission(r);var old=A5Observation("remote",move:true);long id=mission.Capture().ArmyId;
            var wave=new PlayableEntitySnapshot(4,old.Owner,PlayableEntityKind.Tank,new NavPoint(-2,0),100,false,0,0,0,new PlayableTacticalOrderSnapshot(4,old.Owner,71,7,90,PlayableTacticalOrderKind.Follow,default,1),orderStamp:new PlayableOrderStamp(4,old.Owner,71,7,7,90,PlayableOrderOrigin.Ai,PlayableCommandKind.Follow,PlayableAiOpeningComposition.SourceIdentity,42,42));
            var proof=new PlayableRouteProof(4,old.Owner,71,100,1,wave.Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,1,default,default,new[]{default(NavPoint)});
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,old.Entities.Concat(new[]{wave}).ToArray(),old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:old.RouteProofs.Concat(new[]{proof}).ToArray()));
            AiTacticalExecutor.Observe(o,r,id,p);mission.SetPhase(r,phase,100);((AiArmyMissionState)Field(mission,"state")).Reason="withdrawal";
            var records=new[]{A5Receipt(),new PlayableAiTraceRecord(o.Identity,42,90,7,90,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"current applied Follow",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,7,1),AiArmyPlanner.Policy,PlayableCommandKind.Follow)};
            mission.Observe(o,r,AiProfile.Initial,records,replanQuietGuards:false);Assert.True(mission.Active);Assert.AreEqual(!join,r.Capture().Armies.Single().Reinforcements.Contains(4));
            Assert.AreEqual(3,r.Capture().Armies.Single().Members.Except(r.Capture().Armies.Single().Reinforcements).Count()-(join?1:0),"The original core body count is preserved; no viability threshold changes.");
        }
        [TestCase("valid",true)][TestCase("unapplied",false)][TestCase("foreign-policy",false)][TestCase("human",false)][TestCase("not-viable",false)]
        public void Retreat_OwnPendingReplacementDoesNotDependOnRepairClientBudget(string mode,bool keep)
        {
            var p=PlayableProfile.Default;var r=Registry();var mission=A4Mission(r);var old=A5Observation("remote",1000,move:true);mission.SetPhase(r,AiArmyPhase.Retreating,1000);((AiArmyMissionState)Field(mission,"state")).Reason="withdrawal";
            var goal=new NavPoint(-25,4);var pending=new PlayableQueuedOrderSnapshot(1,1,7,24,PlayableCommandKind.Move,new NavLocation(goal,NavLocation.FlatSurface),0,aiActionId:314);
            var queue=new PlayableUnitQueueSnapshot(null,pending,Array.Empty<PlayableQueuedOrderSnapshot>(),null,pendingAiRouteViable:mode!="not-viable");
            var units=old.Entities.Select(u=>u.Id==1?new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Health,false,0,0,0,u.CurrentOrder,orderStamp:new PlayableOrderStamp(1,u.Owner,71,24,7,900,mode=="human"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,314,314),queue:queue):u).ToArray();
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,1000,1000,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:old.RouteProofs.ToArray()));
            var receipt=new PlayableAiTraceRecord(o.Identity,314,900,7,900,mode=="unapplied"?PlayableAiDeliveryStatus.Accepted:PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"current pending core replacement",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,7,1),mode=="foreign-policy"?"foreign":AiArmyPlanner.Policy,PlayableCommandKind.Move);
            mission.Observe(o,r,AiProfile.Initial,new[]{A5Receipt(),receipt},replanQuietGuards:false);Assert.AreEqual(keep,mission.Active,"An owned viable core replacement is independent of a repair client's deadline; exact authority checks still apply.");
        }
        [Test] public void Retreat_ArrivedIntermediateLegStillNeedsActualHomeArrival()
        {
            var p=PlayableProfile.Default;var old=A5Observation();var at=new NavPoint(-12,4);var goal=new NavPoint(-25,0);
            var unit=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,at,100,false,0,0,0,navigationOutcome:NavigationOutcome.Arrived,
                orderStamp:new PlayableOrderStamp(1,PlayableOwner.Player,71,7,7,90,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,22,22));
            var route=new PlayableRouteProof(1,PlayableOwner.Player,71,100,1,at,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,50,old.Buildings.Single(b=>b.Id==50).Position,goal,new[]{goal});
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,new[]{unit},old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:new[]{route},activeProfile:p));
            var army=new AiArmyState{OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Members=new[]{1},Phase=AiArmyPhase.Retreating};
            var receipts=new[]{new PlayableAiTraceRecord(o.Identity,22,90,7,90,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"accepted intermediate leg",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,1,7),AiArmyPlanner.Policy,PlayableCommandKind.Move)};
            Assert.True(AiTacticalExecutor.EffectiveWithdrawal(o,army,unit,receipts,p,certified:false));
            Assert.False(AiTacticalExecutor.EffectiveWithdrawal(o,army,unit,receipts,p),"Intermediate progress cannot become home arrival/recovery");
        }
        [TestCase(false)][TestCase(true)] public void Repair_HealedSavedClientIsNotStolenByGenericReinforcementGather(bool expired)
        {
            var p=PlayableProfile.Default;long tick=expired?10+AiProfile.SecondsToTicks(AiProfile.Initial.Value("positionDevelopment.repairWaitSeconds"),30):400;var r=Registry();var planner=A4Mission(r);var old=A4Observation(tick,wave:true);long id=planner.Capture().ArmyId;AiTacticalExecutor.Observe(old,r,id,p);
            var at=old.Entities.Single(u=>u.Id==4).Position;var order=new PlayableTacticalOrderSnapshot(4,PlayableOwner.Player,71,7,10,PlayableTacticalOrderKind.Move,at,0);
            var units=old.Entities.Select(u=>u.Id==4?new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,100,false,0,0,0,order,navigationOutcome:NavigationOutcome.Arrived,
                orderStamp:new PlayableOrderStamp(4,PlayableOwner.Player,71,7,7,10,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,314,314)):u).ToArray();
            var tower=new PlayableBuildingSnapshot(30,PlayableOwner.Player,PlayableBuildingKind.RepairTower,new NavPoint(at.X-2,at.Z),100,1,0,0,default);
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,old.Buildings.Concat(new[]{tower}).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:old.RouteProofs.ToArray(),activeProfile:p));
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;typeof(AiArmyPlanner).GetField("nextId",flags).SetValue(planner,43L);typeof(AiArmyPlanner).GetField("completed",flags).SetValue(planner,new System.Collections.Generic.Dictionary<string,long>{{"repair-use:4:30:42:7",10}});
            var receipts=new[]{new PlayableAiTraceRecord(o.Identity,314,10,7,10,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"actual repaired client",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,1,7),AiArmyPlanner.Policy,PlayableCommandKind.Move)};
            Assert.True(AiRepairUsePolicy.Recovering(o,p,AiProfile.Initial,planner.Capture().RepairClients,AiArmyPlanner.Policy,receipts,includeRecovered:true,includeExpired:true).Contains(4));
            var before=Newtonsoft.Json.JsonConvert.SerializeObject(r.Capture());var action=planner.ProposeWithRecords(o,A4Opening(),p,AiProfile.Initial,r,receipts);
            Assert.AreEqual(expired,action!=null&&action.EntityIds.Contains(4),"Live saved client waits for its dedicated Follow; the original elapsed budget cannot be extended");
            Assert.True(planner.Capture().RepairClients.ContainsKey("repair-use:4:30:42:7"));Assert.AreEqual(before,Newtonsoft.Json.JsonConvert.SerializeObject(r.Capture()));
        }
        [TestCase(true,false)][TestCase(true,true)][TestCase(false,false)]
        public void Mission_PublicApproachDoesNotRetargetToCollidingVisibleWorldId(bool isPublic,bool building)
        {
            var p=PlayableProfile.Default;var r=Registry();var planner=A4Mission(r);const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var state=(AiArmyMissionState)typeof(AiArmyPlanner).GetField("state",flags).GetValue(planner);var approach=new NavPoint(18,0);var observed=new NavPoint(20,0);
            state.TargetKind=isPublic?PlayableRouteTargetKind.PublicObjective:PlayableRouteTargetKind.VisibleEnemy;state.TargetId=101;state.Target=approach;state.Phase=AiArmyPhase.Advancing;
            r.UpdateMission(state.ArmyId,AiArmyPlanner.Policy,AiArmyPhase.Advancing,100,((int)state.TargetKind)+":101",rally:approach);
            PlayableAiObservation View(long tick)
            {
                var old=A4Observation(tick);var command=isPublic?PlayableCommandKind.AttackMove:PlayableCommandKind.Attack;
                var units=old.Entities.Where(u=>!building||u.Id!=101).Select(u=>u.Owner!=PlayableOwner.Player?u:new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Health,false,0,0,0,
                    new PlayableTacticalOrderSnapshot(u.Id,u.Owner,71,7,90,isPublic?PlayableTacticalOrderKind.AttackMove:PlayableTacticalOrderKind.Attack,isPublic?approach:default,isPublic?0:101),
                    orderStamp:new PlayableOrderStamp(u.Id,u.Owner,71,7,7,90,PlayableOrderOrigin.Ai,command,PlayableAiOpeningComposition.SourceIdentity,22,22))).ToArray();
                var buildings=old.Buildings.Concat(building?new[]{new PlayableBuildingSnapshot(101,PlayableOwner.Enemy,PlayableBuildingKind.Headquarters,observed,300,2,0,0,default)}:Array.Empty<PlayableBuildingSnapshot>()).ToArray();
                return PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,buildings,
                    Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,activeProfile:p,
                    publicScoutObjectives:new[]{new PlayablePublicScoutObjective(101,approach,PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true)}));
            }
            var o=View(100);var kind=isPublic?PlayableCommandKind.AttackMove:PlayableCommandKind.Attack;
            var records=new[]{new PlayableAiTraceRecord(o.Identity,22,90,7,90,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"exact current mission leg",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,1,7),AiArmyPlanner.Policy,kind)};
            planner.Observe(o,r,AiProfile.Initial,records,replanQuietGuards:false);Assert.True(planner.Active);Assert.AreEqual(isPublic?approach:observed,planner.Capture().Target);
            if(isPublic){planner.SetPhase(r,AiArmyPhase.Regrouping,101);state.Reason="withdrawal";planner.Observe(View(101),r,AiProfile.Initial,records,replanQuietGuards:false);Assert.True(planner.Active,"The exact accepted public leg remains ours while withdrawal is prepared");Assert.AreEqual(approach,planner.Capture().Target);}
            using var stream=new System.IO.MemoryStream();using(var w=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))planner.WriteState(w);var bytes=stream.ToArray();stream.Position=0;var cold=new AiArmyPlanner(o.OwnerId,o.Generation);using(var reader=new System.IO.BinaryReader(stream,System.Text.Encoding.UTF8,true))cold.ReadState(reader,101,r);
            using var output=new System.IO.MemoryStream();using(var w=new System.IO.BinaryWriter(output,System.Text.Encoding.UTF8,true))cold.WriteState(w);CollectionAssert.AreEqual(bytes,output.ToArray(),"Current target binding persists exactly");
        }
        [TestCase("local",true)][TestCase("remote",false)][TestCase("hidden",false)][TestCase("allied",false)]
        public void Retreat_OnlyLocalVisibleHostileCatalogForce(string change,bool expected)
        {
            var r=Registry();var mission=A4Mission(r);var o=A5Observation(change);var army=r.Capture().Armies.Single();
            Assert.AreEqual(expected,AiTacticalExecutor.Withdraw(o,army,PlayableProfile.Default,AiProfile.Initial));
            Assert.AreEqual(expected,AiTacticalExecutor.Withdrawal(o,army,PlayableProfile.Default,AiProfile.Initial,2)!=null);
        }
        [TestCase("local",3)][TestCase("hidden",1)][TestCase("remote",1)][TestCase("allied",1)]
        public void Retreat_ObservedExposurePrioritizesEndangeredMemberWithoutPrivateTargets(string change,int expected)
        {
            var p=PlayableProfile.Default;var r=Registry();var mission=A4Mission(r);mission.SetPhase(r,AiArmyPhase.Retreating,100);var old=A5Observation(change);
            var units=old.Entities.Select(e=>e.Id>=101?new PlayableEntitySnapshot(e.Id,e.Owner,e.Kind,new NavPoint(e.Position.X,4+(e.Id-101)*.5),e.Health,e.Moving,0,e.HullHeading,e.TurretHeading):e).ToArray();
            Assert.True(units.Where(e=>e.Id>=101).All(e=>e.TargetId==0),"Enemy target identity is redacted in a lawful owner observation");
            var proofs=old.RouteProofs.Select(route=>route.Kind!=PlayableRouteTargetKind.VisibleEnemy?route:
                new PlayableRouteProof(route.UnitId,route.Owner,route.Generation,route.Tick,route.GeometryRevision,route.Origin,route.Radius,route.Kind,route.TargetId,
                    units.Single(e=>e.Id==route.TargetId).Position,units.Single(e=>e.Id==route.TargetId).Position,new[]{units.Single(e=>e.Id==route.TargetId).Position})).ToArray();
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,old.Generation,old.Seed,old.Tick,old.Tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,
                units,old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:proofs,activeProfile:p));
            var army=r.Capture().Armies.Single();var before=Newtonsoft.Json.JsonConvert.SerializeObject(r.Capture());
            var action=AiTacticalExecutor.Withdrawal(o,army,p,AiProfile.Initial,2);
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);CollectionAssert.AreEqual(new[]{expected},action.EntityIds);
            Assert.True(o.RouteProofs.Any(route=>route.UnitId==expected&&route.Goal.Equals(action.Target)&&AiTacticalExecutor.WithdrawalRoute(o,route,p)),"Priority still needs the same current safe home route");
            Assert.AreEqual(before,Newtonsoft.Json.JsonConvert.SerializeObject(r.Capture()),"Selection cannot change core membership or phase");
        }
        [TestCase("local",PlayableCommandKind.Move)][TestCase("unsafe",PlayableCommandKind.Attack)][TestCase("missing",PlayableCommandKind.Attack)][TestCase("stale",PlayableCommandKind.Attack)]
        public void Retreat_UnsafeOrMissingNativeFallbackDefendsInsteadOfIdle(string change,PlayableCommandKind expected)
        {
            var r=Registry();var mission=A4Mission(r);var o=A5Observation(change);mission.Observe(o,r,AiProfile.Initial);var id=mission.Capture().ArmyId;
            var action=mission.Propose(o,A4Opening(),PlayableProfile.Default,AiProfile.Initial,r);Assert.NotNull(action);Assert.AreEqual(expected,action.Kind);Assert.AreEqual(id,r.ArmyFor(action.EntityIds.Single()));
            Assert.AreEqual(AiArmyPhase.Regrouping,mission.Capture().Phase);
        }
        [TestCase("valid",true)][TestCase("human",false)][TestCase("source",false)][TestCase("sequence",false)][TestCase("unapplied",false)][TestCase("policy",false)][TestCase("missing",false)][TestCase("unsafe",false)][TestCase("stale",false)]
        public void Retreat_EffectiveMoveRequiresAuthorityAndCurrentCertificate(string change,bool expected)
        {
            var r=Registry();A4Mission(r);var o=A5Observation(change,101,move:true);var army=r.Capture().Armies.Single();
            bool actual=(bool)typeof(AiTacticalExecutor).GetMethod("EffectiveWithdrawal",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{o,army,o.Entities.First(),new[]{A5Receipt(change)},PlayableProfile.Default,true});
            Assert.AreEqual(expected,actual);
        }
        [Test] public void Retreat_StallFallsBackOnceAndStillHasBoundedRelease()
        {
            var r=Registry();var mission=A4Mission(r);long due=10+AiProfile.SecondsToTicks(AiProfile.Initial.Value("armies.stallSeconds"),30);
            var o=A5Observation("remote",due);mission.Observe(o,r,AiProfile.Initial);Assert.True(mission.Active);Assert.AreEqual(AiArmyPhase.Regrouping,mission.Capture().Phase);
            mission.Observe(A5Observation("remote",due+AiProfile.SecondsToTicks(AiProfile.Initial.Value("armies.stallSeconds"),30)),r,AiProfile.Initial);Assert.False(mission.Active);Assert.AreEqual("withdrawal no meaningful progress deadline",mission.Capture().Reason);
        }
        [Test] public void Retreat_ActualAuthorityMovesAndPreservesPendingRestore()
        {
            var c=A4NativeConfig();var a=new PlayableAuthorityTick(c,71);var owner=Owners(a).Single();var registry=(AiArmyRegistry)Field(owner,"armies");var planner=(AiArmyPlanner)Field(owner,"armyPlanner");var domain=Field(a,"domain");
            using(var routes=new UnityHostRouteService())
            {
                for(int i=0;i<300&&(!planner.Active||planner.Capture().Phase!=AiArmyPhase.Advancing);i++)NativeStep(a,routes);
                Assert.True(planner.Active);long army=planner.Capture().ArmyId;
                // Exercise the actual bounded stall branch while leaving all gameplay bodies intact.
                var state=(AiArmyMissionState)Field(planner,"state");
                planner.SetPhase(registry,AiArmyPhase.Regrouping,a.Tick);state.Reason="withdrawal";
                bool moved=false,restored=false;NavPoint? before=null;
                for(int i=0;i<300&&!moved;i++)
                {
                    NativeStep(a,routes);var view=a.ParticipantView("player-1");var unit=view.Entities.FirstOrDefault(u=>registry.ArmyFor(u.Id)==army&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai);
                    if(unit==null)continue;
                    Assert.AreEqual(army,planner.Capture().ArmyId);Assert.AreEqual(AiArmyPhase.Retreating,planner.Capture().Phase);
                    if(!restored){var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());restored=true;}
                    if(before.HasValue&&!before.Value.Equals(unit.Position))moved=true;before=unit.Position;
                }
                Assert.True(restored,"Actual retreat world/AI wire must roundtrip");Assert.True(moved,"A receipt is insufficient: a body must move through the native route barrier");
            }
        }
        [TestCase("away",true)][TestCase("toward",false)][TestCase("reentry",false)][TestCase("new-envelope",false)][TestCase("endpoint-exposed",false)]
        public void Retreat_MonotonicSegmentsCannotCrossOrReenterWeaponEnvelopes(string path,bool expected)
        {
            var o=A5Observation();var points=path=="away"?new[]{new NavPoint(-12,0),new NavPoint(-25,0)}:
                path=="toward"?new[]{new NavPoint(7,0),new NavPoint(-25,0)}:
                path=="reentry"?new[]{new NavPoint(-25,0),new NavPoint(7,0),new NavPoint(-25,0)}:
                path=="new-envelope"?new[]{new NavPoint(0,30),new NavPoint(7,0),new NavPoint(-25,0)}:new[]{new NavPoint(-.1,0)};
            bool actual=(bool)typeof(AiTacticalExecutor).GetMethod("WithdrawalPath",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{o,default(NavPoint),points,PlayableProfile.Default.TankCollisionRadius,PlayableProfile.Default});Assert.AreEqual(expected,actual);
        }
        [Test] public void Retreat_OrdinaryArtillerySupportMoveDoesNotHijackMissionPhase()
        {
            var r=Registry();var mission=A4Mission(r);var old=A4Observation(100,wave:true,gun:true,near:true);long id=mission.Capture().ArmyId;AiTacticalExecutor.Observe(old,r,id,PlayableProfile.Default);
            var fact=new PlayableArtillerySupportSnapshot(4,71,100,true,false,false,true,new NavPoint(-5,4));
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,old.Entities.ToArray(),old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:old.RouteProofs.ToArray(),artillerySupport:new[]{fact}));
            var action=mission.Propose(o,A4Opening(),PlayableProfile.Default,AiProfile.Initial,r);Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);CollectionAssert.AreEqual(new[]{4},action.EntityIds);var phase=mission.Capture().Phase;var anchor=r.Capture().Armies.Single().Anchor;
            mission.Commit(o,action,r);Assert.AreEqual(phase,mission.Capture().Phase);Assert.AreEqual(anchor,r.Capture().Armies.Single().Anchor);Assert.AreEqual(id,mission.Capture().ArmyId);
        }
        private static OfflineMatchConfiguration A5VisibleRecoveryConfig()
        {
            var c=A4NativeConfig();var observer=new NavPoint(20,-8);
            // Positive recovery has a legitimately observed objective. Only this local
            // fixture copy moves its existing refinery/declared slot; A4 is unchanged.
            var sites=c.Sites.Select(s=>s.Id==1?new TerritorySite(s.Id,s.Kind,s.Position,s.Slots.Select(x=>x.Id==2?new TerritorySlot(x.Id,observer,x.Heading):x).ToArray()):s).ToArray();
            var buildings=c.ScenarioBuildings.Select(b=>b.LogicalPlayer==1&&b.Kind==PlayableBuildingKind.Refinery?new OfflineScenarioBuilding(b.LogicalPlayer,b.Kind,b.SiteId,b.SlotId,observer,b.Heading):b).ToArray();
            return new OfflineMatchConfiguration(c.Profile,c.SourceIdentity+"-a5-visible-recovery",c.MapIdentity+"-a5-visible-recovery",c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),sites,c.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},spectators:c.Spectators.ToArray(),scenario:c.ScenarioUnits.ToArray(),scenarioBuildings:buildings);
        }
        [TestCase(true)][TestCase(false)] public void Retreat_ActualVisibleThreatCompletesNativeArrivalRecoveryAndReplay(bool injectReinforcements)
            =>A5RecoveryCycle(injectReinforcements,injectReinforcements);
        [Test] public void Retreat_OccludedObjectiveAfterActualRecoveryKeepsSameArmyAndDoesNotAttackHiddenTarget()
            =>A5RecoveryCycle(true,false);
        private static void A5RecoveryCycle(bool injectReinforcements,bool visibleObjective)
        {
            var c=visibleObjective?A5VisibleRecoveryConfig():A4NativeConfig();var a=new PlayableAuthorityTick(c,71);var owner=Owners(a).Single();var registry=(AiArmyRegistry)Field(owner,"armies");var planner=(AiArmyPlanner)Field(owner,"armyPlanner");var domain=Field(a,"domain");
            using(var routes=new UnityHostRouteService())
            {
                for(int i=0;i<300&&(!planner.Active||planner.Capture().Phase!=AiArmyPhase.Advancing);i++)NativeStep(a,routes);
                Assert.True(planner.Active);long army=planner.Capture().ArmyId;var front=a.ParticipantView("player-1").Entities.Where(u=>registry.ArmyFor(u.Id)==army).OrderBy(u=>u.Id).First().Position;
                for(int i=0;i<6;i++)domain.GetType().GetMethod("SpawnUnit",F).Invoke(domain,new object[]{new NavPoint(front.X+c.Profile.TankRange/2,front.Z+(i-3)*2),PlayableOwner.Enemy,PlayableEntityKind.Tank});
                bool retreat=false,recover=false,resume=false,moved=false,pendingRestore=false,effectiveRestore=false,injected=false;long lastSeenAtRetreat=-1,recoveredTick=-1;var injectedIds=new System.Collections.Generic.List<int>();PlayableAuthorityTick twin=null;var initial=a.ParticipantView("player-1").Entities.Where(u=>registry.ArmyFor(u.Id)==army).ToDictionary(u=>u.Id,u=>u.Position);
                for(int i=0;i<(injectReinforcements?900:1600)&&!resume&&(!retreat||planner.Active);i++)
                {
                    NativeStep(a,routes);if(twin!=null){NativeStep(twin,routes);ExactByteAssert.AreEqual(a.CaptureBytes(),twin.CaptureBytes(),"Exact native replay after withdrawal checkpoint");}
                    var phase=planner.Capture().Phase;retreat|=phase==AiArmyPhase.Retreating;recover|=phase==AiArmyPhase.Recovering;resume=recover&&(phase==AiArmyPhase.Staging||phase==AiArmyPhase.Advancing||phase==AiArmyPhase.Engaging);
                    if(retreat&&lastSeenAtRetreat<0)lastSeenAtRetreat=planner.Capture().LastSeenTick;
                    if(planner.Capture().Reason=="recovered"&&recoveredTick<0)recoveredTick=a.Tick;
                    if(retreat&&planner.Active)Assert.AreEqual(army,planner.Capture().ArmyId,"Original army owns the entire retreat/recovery cycle");
                    if(recover&&injectReinforcements&&!injected)
                    {
                        // Fixture-injected healthy reinforcements through the existing
                        // produced-unit spawn API; this does not prove production itself.
                        for(int n=0;n<3;n++)
                        {
                            var point=new NavPoint(-23,-14-n*3);
                            var units=(System.Collections.IDictionary)Field(domain,"units");var oldIds=units.Keys.Cast<int>().ToArray();
                            domain.GetType().GetMethod("SpawnProduced",F).Invoke(domain,new object[]{point,(NavPoint?)new NavPoint(8,-8),PlayableOwner.Player,PlayableEntityKind.Tank});
                            injectedIds.Add(units.Keys.Cast<int>().Except(oldIds).Single());
                            if(twin!=null){var td=Field(twin,"domain");td.GetType().GetMethod("SpawnProduced",F).Invoke(td,new object[]{point,(NavPoint?)new NavPoint(8,-8),PlayableOwner.Player,PlayableEntityKind.Tank});}
                        }
                        injected=true;TestContext.WriteLine("Fixture-injected 3 healthy reinforcements; actual native assembly and viability remain required");
                    }
                    var view=a.ParticipantView("player-1");moved|=view.Entities.Any(u=>initial.ContainsKey(u.Id)&&!initial[u.Id].Equals(u.Position));
                    if(phase==AiArmyPhase.Retreating&&!pendingRestore&&(long)Field(planner,"pendingId")!=0)
                    {twin=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);ExactByteAssert.AreEqual(a.CaptureBytes(),twin.CaptureBytes());pendingRestore=true;}
                    if(phase==AiArmyPhase.Retreating&&!effectiveRestore&&view.Entities.Any(u=>registry.ArmyFor(u.Id)==army&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai))
                    {var copy=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);ExactByteAssert.AreEqual(a.CaptureBytes(),copy.CaptureBytes());effectiveRestore=true;long tick=a.Tick;long progress=planner.Capture().ProgressTick;for(int n=0;n<3;n++){Assert.True(a.TryAdvance(paused:true));if(twin!=null)Assert.True(twin.TryAdvance(paused:true));}Assert.AreEqual(tick,a.Tick);Assert.AreEqual(progress,planner.Capture().ProgressTick);if(twin!=null)ExactByteAssert.AreEqual(a.CaptureBytes(),twin.CaptureBytes());}
                    if(i%60==0)TestContext.WriteLine("A5 actual lifecycle "+Newtonsoft.Json.JsonConvert.SerializeObject(new{a.Tick,Mission=planner.Capture(),Army=registry.Capture().Armies.FirstOrDefault(x=>x.Id==army),Units=view.Entities.Where(u=>initial.ContainsKey(u.Id)),Records=a.CaptureDiagnosticCheckpoints().Single().Records.TakeLast(8)}));
                }
                TestContext.WriteLine("A5 final lifecycle "+Newtonsoft.Json.JsonConvert.SerializeObject(new{a.Tick,Mission=planner.Capture(),Registry=registry.Capture(),InjectedIds=injectedIds.ToArray(),Snapshot=a.ParticipantView("player-1").Entities,Records=a.CaptureDiagnosticCheckpoints().Single().Records.TakeLast(12)}));
                Assert.True(retreat);Assert.True(moved);Assert.True(pendingRestore);Assert.True(effectiveRestore);Assert.True(recover,"Physical safe native arrival is required");if(injectReinforcements)
                {
                    var g=registry.Capture().Armies.Single(x=>x.Id==army);Assert.True(injectedIds.All(id=>g.Members.Contains(id)&&!g.Reinforcements.Contains(id)),"All three healthy fixture reinforcements must actually join through strict native assembly");
                    var view=PlayableAiObservation.From(a.ParticipantView("player-1"));var line=view.Entities.Where(u=>g.Members.Contains(u.Id)&&!g.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.For(u.Kind).lineWeight>0).ToArray();
                    Assert.GreaterOrEqual(AiDefensePlanner.Force(line,c.Profile,AiProfile.Initial),AiDefensePlanner.Force(new[]{new PlayableEntitySnapshot(999,view.Owner,PlayableEntityKind.Tank,default,c.Profile.TankHealth,false,0,0,0)},c.Profile,AiProfile.Initial)*AiProfile.Initial.Value("armies.minimumUnits"));
                    if(visibleObjective)
                    {
                        Assert.True(resume,"Recovered original army must resume pressure/retask after native reinforcement assembly");Assert.True(view.Vision.IsVisible(planner.Capture().Target));Assert.True(view.Buildings.Any(b=>b.Id==planner.Capture().TargetId&&view.IsHostile(b.Owner)&&b.Health>0),"The original objective is genuinely owner-visible at native resume");
                    }
                    else
                    {
                        Assert.False(resume);Assert.AreEqual("recovered",planner.Capture().Reason);Assert.AreEqual(AiArmyPhase.Regrouping,planner.Capture().Phase);Assert.False(planner.Capture().TargetVisible);Assert.AreEqual(lastSeenAtRetreat,planner.Capture().LastSeenTick);Assert.GreaterOrEqual(recoveredTick,0);
                        Assert.False(a.CaptureDiagnosticCheckpoints().Single().Records.Any(r=>r.Policy==AiArmyPlanner.Policy&&r.Kind==PlayableCommandKind.Attack&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>=recoveredTick),"Recovery cannot issue a new attack on an occluded objective");
                    }
                }
                else
                {
                    Assert.False(planner.Active,"No fixture reinforcements: underpowered native recovery must release within the existing stall bound");
                    Assert.AreEqual("withdrawal no meaningful progress deadline",planner.Capture().Reason);
                }
                a.Stop();Assert.False(planner.Active);Assert.True(registry.Capture().Armies.All(x=>x.Phase==AiArmyPhase.Disbanded));twin?.Stop();
            }
        }
        [TestCase("cleared",true)][TestCase("unphysical",false)][TestCase("missing",false)][TestCase("human",false)][TestCase("unapplied",false)]
        public void Retreat_NativeArrivalClearsOrderButStillRequiresAppliedPhysicalProof(string change,bool expected)
        {
            var r=Registry();A4Mission(r);var o=A5Observation(change,101,move:true,arrived:true);var army=r.Capture().Armies.Single();
            bool actual=(bool)typeof(AiTacticalExecutor).GetMethod("EffectiveWithdrawal",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{o,army,o.Entities.First(),new[]{A5Receipt(change)},PlayableProfile.Default,true});Assert.AreEqual(expected,actual);
        }
        [Test] public void Retreat_EscapeCannotEnterAnInitiallyUnexposedSeparateEnemyEnvelope()
        {
            var original=A5Observation();var p=PlayableProfile.Default;var e=new PlayableEntitySnapshot(200,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-25,0),p.TankHealth,false,0,0,0);
            Assert.Greater(Math.Abs(e.Position.X),p.TankRange+2*p.TankCollisionRadius,"New threat is genuinely outside the origin envelope");
            var o=PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,original.Seed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,original.Entities.Concat(new[]{e}).ToArray(),original.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:original.Vision,routeProofs:original.RouteProofs.ToArray()));
            var escape=new[]{new NavPoint(-12,0),new NavPoint(-40,0)};
            var method=typeof(AiTacticalExecutor).GetMethod("WithdrawalPath",BindingFlags.NonPublic|BindingFlags.Static);
            Assert.True((bool)method.Invoke(null,new object[]{original,default(NavPoint),escape,p.TankCollisionRadius,p}),"This departure is safe against the initial frontline");
            Assert.False((bool)method.Invoke(null,new object[]{o,default(NavPoint),escape,p.TankCollisionRadius,p}),"A separate initially unexposed weapon envelope cannot be crossed");
        }
        private static OfflineMatchConfiguration A5DeliveryConfig()
        {
            var c=A4NativeConfig();var home=new NavPoint(-28,0);var factory=new NavPoint(-28,-9);var refinery=new NavPoint(-28,10);
            // Authored post-opening battle: economic anchors stay behind both the frontline and crossing envelope.
            var starts=c.Starts.Select((start,i)=>i==0?new OfflineStart(start.Id,start.SiteId,home,new NavPoint(-28,6),start.Heading,start.PinnedLogicalPlayer):start).ToArray();
            var sites=c.Sites.Select(site=>site.Id!=1?site:new TerritorySite(site.Id,site.Kind,home,new[]{new TerritorySlot(1,factory,0),new TerritorySlot(2,refinery,0)})).ToArray();
            var buildings=c.ScenarioBuildings.Select(b=>b.LogicalPlayer!=1?b:new OfflineScenarioBuilding(1,b.Kind,b.SiteId,b.SlotId,b.Kind==PlayableBuildingKind.Factory?factory:refinery,b.Heading)).ToArray();
            return new OfflineMatchConfiguration(c.Profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),starts,sites,c.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},scenario:c.ScenarioUnits.ToArray(),scenarioBuildings:buildings);
        }
        [TestCase(false)][TestCase(true)] public void Retreat_ActualPendingWithdrawalRechecksMovingVisibleEnemyBeforeSequence(bool intrusion)
        {
            var c=A5DeliveryConfig();var a=new PlayableAuthorityTick(c,71);var owner=Owners(a).Single();var domain=Field(a,"domain");var registry=(AiArmyRegistry)Field(owner,"armies");var planner=(AiArmyPlanner)Field(owner,"armyPlanner");
            typeof(AiOpeningExecutor).GetField("phase",F).SetValue(Field(owner,"openingExecutor"),PlayableAiOpeningPhase.Aborted);
            using(var routes=new UnityHostRouteService())
            {
                for(int i=0;i<300&&(!planner.Active||planner.Capture().Phase!=AiArmyPhase.Advancing&&planner.Capture().Phase!=AiArmyPhase.Engaging);i++)NativeStep(a,routes);
                Assert.True(planner.Active);var front=a.ParticipantView("player-1").Entities.Where(u=>registry.ArmyFor(u.Id)==planner.Capture().ArmyId).OrderByDescending(u=>u.Position.X).ThenBy(u=>u.Id).First().Position;
                for(int i=0;i<6;i++)domain.GetType().GetMethod("SpawnUnit",F).Invoke(domain,new object[]{new NavPoint(front.X+c.Profile.TankRange/2,front.Z+(i-3)*2),PlayableOwner.Enemy,PlayableEntityKind.Tank});
                object pending=null;
                for(int i=0;i<150&&pending==null;i++)
                {NativeStep(a,routes);if(planner.Capture().Phase==AiArmyPhase.Retreating)pending=((System.Collections.IEnumerable)Field(owner,"pendingItems")).Cast<object>().FirstOrDefault(x=>(string)Field(x,"PolicyName")==AiArmyPlanner.Policy&&((PlayableAiAction)Field(x,"Action")).Kind==PlayableCommandKind.Move);}
                Assert.NotNull(pending,"A genuine accepted withdrawal candidate is required");var action=(PlayableAiAction)Field(pending,"Action");long due=(long)Field(pending,"DueTick");long army=planner.Capture().ArmyId;
                var actor=a.ParticipantView("player-1").Entities.Single(u=>u.Id==action.EntityIds.Single());var origin=actor.Position;var delta=new NavPoint(action.Target.X-origin.X,action.Target.Z-origin.Z);double length=Math.Sqrt(delta.X*delta.X+delta.Z*delta.Z);var middle=new NavPoint(origin.X+delta.X*.1,origin.Z+delta.Z*.1);var normal=new NavPoint(-delta.Z/length,delta.X/length);
                var start=new NavPoint(middle.X+normal.X*(c.Profile.TankRange+2*c.Profile.TankCollisionRadius+1),middle.Z+normal.Z*(c.Profile.TankRange+2*c.Profile.TankCollisionRadius+1));
                int enemy=(int)domain.GetType().GetMethod("SpawnUnit",F).Invoke(domain,new object[]{start,PlayableOwner.Enemy,PlayableEntityKind.Tank});
                Assert.True(a.Domain.Navigation.Crowd.TryGet(enemy,out var intruderActor));
                typeof(NavUnit).GetProperty("Heading").SetValue(intruderActor,Math.Atan2(-normal.Z,-normal.X));
                Assert.True((bool)domain.GetType().GetMethod("ArmyWithdrawalSafe",F).Invoke(domain,new object[]{action}),"The newly visible intruder starts outside this accepted escape path");
                if(intrusion)
                {
                    var target=new NavPoint(middle.X+normal.X*c.Profile.TankRange*.8,middle.Z+normal.Z*c.Profile.TankRange*.8);
                    Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,"enemy-1",PlayableCommandKind.Move,new[]{enemy},target)).Status);
                }
                var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                while(a.Tick<due){NativeStep(a,routes);NativeStep(b,routes);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                var before=PlayableAiObservation.From(a.ParticipantView("player-1"));Assert.True(before.Entities.Any(u=>u.Id==enemy));Assert.IsEmpty(AiDefensePlanner.Threats(before,c.Profile),"The path intrusion must remain outside base-defense preemption");
                TestContext.WriteLine("INTRUSION_PROOF "+Newtonsoft.Json.JsonConvert.SerializeObject(new{intrusion,due,a.Tick,origin,action.Target,start,middle,Enemy=before.Entities.Single(u=>u.Id==enemy),Actor=before.Entities.Single(u=>u.Id==actor.Id)}));
                Assert.AreEqual(!intrusion,(bool)domain.GetType().GetMethod("ArmyWithdrawalSafe",F).Invoke(domain,new object[]{action}));
                while(a.Tick<=due+1){NativeStep(a,routes);NativeStep(b,routes);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                var terminal=a.CaptureDiagnosticCheckpoints().Single().Records.Single(r=>r.ActionId==action.ActionId&&r.Policy==AiArmyPlanner.Policy&&r.Status!=PlayableAiDeliveryStatus.Scheduled);
                Assert.AreEqual(intrusion?PlayableAiDeliveryStatus.Cancelled:PlayableAiDeliveryStatus.Applied,terminal.Status);Assert.AreEqual(army,registry.ArmyFor(actor.Id));
                if(intrusion){Assert.Zero(terminal.CommandSequence);StringAssert.Contains("Current observed army path",terminal.Message);}
                else
                {
                    for(int n=0;n<120&&a.ParticipantView("player-1").Entities.Single(u=>u.Id==actor.Id).Position.Equals(origin);n++){NativeStep(a,routes);NativeStep(b,routes);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                    Assert.False(a.ParticipantView("player-1").Entities.Single(u=>u.Id==actor.Id).Position.Equals(origin),"Safe control must physically move through native navigation");
                }
                TestContext.WriteLine("A5 withdrawal due "+Newtonsoft.Json.JsonConvert.SerializeObject(new{intrusion,due,a.Tick,Action=action,Terminal=terminal,Before=before.Entities}));b.Stop();
            }
            a.Stop();
        }
        [Test] public void Retreat_FlankDeliveryUsesItsOwnWithdrawalMission()
        {
            var c=A5DeliveryConfig();var a=new PlayableAuthorityTick(c,71);var owner=Owners(a).Single();var domain=Field(a,"domain");
            var registry=(AiArmyRegistry)Field(owner,"armies");var primary=(AiArmyPlanner)Field(owner,"armyPlanner");var flank=(AiArmyPlanner)Field(owner,"flankPlanner");
            // This delivery fixture starts after opening; the three scenario Tanks are its full combat force.
            typeof(AiOpeningExecutor).GetField("phase",F).SetValue(Field(owner,"openingExecutor"),PlayableAiOpeningPhase.Aborted);
            using(var routes=new UnityHostRouteService())
            {
                for(int i=0;i<300&&(!primary.Active||primary.Capture().Phase!=AiArmyPhase.Advancing&&primary.Capture().Phase!=AiArmyPhase.Engaging);i++)NativeStep(a,routes);
                Assert.True(primary.Active);var front=a.ParticipantView("player-1").Entities.Where(u=>registry.ArmyFor(u.Id)==primary.Capture().ArmyId).OrderByDescending(u=>u.Position.X).ThenBy(u=>u.Id).First().Position;
                for(int i=0;i<6;i++)domain.GetType().GetMethod("SpawnUnit",F).Invoke(domain,new object[]{new NavPoint(front.X+c.Profile.TankRange/2,front.Z+(i-3)*2),PlayableOwner.Enemy,PlayableEntityKind.Tank});
                object pending=null;
                for(int i=0;i<150&&pending==null;i++)
                {NativeStep(a,routes);if(primary.Capture().Phase==AiArmyPhase.Retreating)pending=((System.Collections.IEnumerable)Field(owner,"pendingItems")).Cast<object>().FirstOrDefault(x=>(string)Field(x,"PolicyName")==AiArmyPlanner.Policy&&((PlayableAiAction)Field(x,"Action")).Kind==PlayableCommandKind.Move);}
                Assert.NotNull(pending,"Use a genuinely accepted native escape, then bind its delivery to the second planner");
                var action=(PlayableAiAction)Field(pending,"Action");var intent=(AiIntent)Field(pending,"Intent");var mission=primary.Capture();
                Assert.True((bool)domain.GetType().GetMethod("ArmyWithdrawalSafe",F).Invoke(domain,new object[]{action}));
                Assert.False((bool)domain.GetType().GetMethod("ArmyMoveSafe",F).Invoke(domain,new object[]{action}),"This regression needs an escape from an existing threat envelope");
                Assert.True(registry.TryClaim(mission.ArmyId,AiArmyPlanner.Policy,AiArmyPlanner.FlankPolicy));
                typeof(AiArmyPlanner).GetField("state",F).SetValue(flank,mission);
                typeof(AiArmyPlanner).GetField("state",F).SetValue(primary,new AiArmyMissionState{Phase=AiArmyPhase.Disbanded,Reason="fixture transferred to flank"});
                pending.GetType().GetField("PolicyName",F).SetValue(pending,AiArmyPlanner.FlankPolicy);
                pending.GetType().GetField("Intent",F).SetValue(pending,new AiIntent(intent.Id,AiArmyPlanner.FlankPolicy,action,intent.ExpiresTick,intent.Credits,intent.Population,intent.Claims,intent.Priority,intent.Utility,intent.Conditions,intent.Reason,intent.StartupFund,intent.ReservationId,intent.Overdue));
                pending.GetType().GetField("Receipt",F).SetValue(pending,new Action<PlayableAiTraceRecord>(_=>{}));
                pending.GetType().GetField("DueTick",F).SetValue(pending,a.Tick);
                owner.GetType().GetMethod("DeliverOne",F).Invoke(owner,new object[]{domain,0L,false});
                var terminal=a.CaptureDiagnosticCheckpoints().Single().Records.Single(r=>r.ActionId==action.ActionId&&r.Policy==AiArmyPlanner.FlankPolicy&&r.Status!=PlayableAiDeliveryStatus.Scheduled);
                Assert.AreEqual(PlayableAiDeliveryStatus.Applied,terminal.Status);Assert.Greater(terminal.CommandSequence,0);
            }
            a.Stop();
        }
        [Test] public void Retreat_DueProofSubsetKeepsCoreHomeAndExactCurrentLegWithoutUnrelatedGather()
        {
            var r=Registry();var mission=A4Mission(r);var o=A5Observation("local",101,move:true);mission.SetPhase(r,AiArmyPhase.Retreating,101);
            var state=r.Capture();var method=typeof(AiTacticalExecutor).GetMethod("RouteRequests",BindingFlags.NonPublic|BindingFlags.Static);
            var requests=(PlayableRouteRequest[])method.Invoke(null,new object[]{o.Entities,o.Buildings,state,o.Owner,o.Generation,o.Tick,PlayableProfile.Default});
            Assert.True(requests.Any(x=>x.UnitId==1&&x.TargetId==50),"Live ready home is available for a core retreat");
            Assert.True(requests.Any(x=>x.UnitId==1&&x.TargetId==1&&x.Target.Equals(o.Entities.First().CurrentOrder.Destination)),"The exact effective current core leg is re-certified");
            Assert.False(requests.Any(x=>x.UnitId==1&&x.TargetId==2),"An existing core is not a newly gathering reinforcement");
            Assert.True(state.Armies.Single().Members.All(id=>requests.Where(x=>x.UnitId==id).All(x=>x.TargetId==id||o.Buildings.Any(b=>b.Id==x.TargetId))));
            Assert.IsEmpty((PlayableRouteRequest[])method.Invoke(null,new object[]{o.Entities,o.Buildings,state,PlayableOwner.Enemy,o.Generation,o.Tick,PlayableProfile.Default}),"Another participant cannot route these members");
            state.Generation++;
            Assert.IsEmpty((PlayableRouteRequest[])method.Invoke(null,new object[]{o.Entities,o.Buildings,state,o.Owner,o.Generation,o.Tick,PlayableProfile.Default}),"A stale registry generation cannot request current certificates");
        }
        [TestCase("ready",true)][TestCase("applied",false)][TestCase("unapplied",true)][TestCase("human",true)][TestCase("cadence",false)][TestCase("missing",false)][TestCase("wrong-goal",false)][TestCase("stale-proof",false)][TestCase("unsafe",false)][TestCase("foreign-leader",false)]
        public void Retreat_RecoveryAssemblyBootstrapsOnlyCurrentCertifiedOwnFollow(string mode,bool expected)
        {
            var p=PlayableProfile.Default;var r=Registry();var mission=A4Mission(r);var old=A4Observation(400,wave:true,near:true,danger:mode=="unsafe");long id=mission.Capture().ArmyId;AiTacticalExecutor.Observe(old,r,id,p);mission.SetPhase(r,AiArmyPhase.Recovering,400);
            var goal=PlayableUnitRules.FollowGoal(p,old.Entities.Single(u=>u.Id==1).Position,0);bool order=mode=="applied"||mode=="unapplied"||mode=="human"||mode=="cadence";long issued=mode=="cadence"?399:10;
            var units=old.Entities.Select(u=>u.Id==4?new PlayableEntitySnapshot(4,u.Owner,u.Kind,u.Position,u.Health,false,0,0,0,order?new PlayableTacticalOrderSnapshot(4,u.Owner,71,2,issued,PlayableTacticalOrderKind.Follow,default,1):null,navigationOutcome:NavigationOutcome.Arrived,orderStamp:order?new PlayableOrderStamp(4,u.Owner,71,2,2,issued,mode=="human"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Follow,PlayableAiOpeningComposition.SourceIdentity,2,2):null):u.Id==1&&mode=="foreign-leader"?new PlayableEntitySnapshot(u.Id,PlayableOwner.Enemy,u.Kind,u.Position,u.Health,false,0,0,0):u).ToArray();
            var proof=new PlayableRouteProof(4,PlayableOwner.Player,mode=="stale-proof"?70:71,400,1,units.Single(u=>u.Id==4).Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,1,goal,mode=="wrong-goal"?new NavPoint(goal.X-1,goal.Z):goal,new[]{mode=="wrong-goal"?new NavPoint(goal.X-1,goal.Z):goal});
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,400,400,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:old.RouteProofs.Concat(mode=="missing"?Array.Empty<PlayableRouteProof>():new[]{proof}).ToArray(),activeProfile:p));
            var records=new[]{new PlayableAiTraceRecord(o.Identity,2,issued,2,issued,mode=="unapplied"?PlayableAiDeliveryStatus.Cancelled:PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"fixture",ownerId:o.OwnerId,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity,receiptIdentity:new AiReceiptIdentity(71,o.OwnerId,1,2),policy:AiArmyPlanner.Policy,kind:PlayableCommandKind.Follow)};
            var army=r.Capture().Armies.Single();var before=Newtonsoft.Json.JsonConvert.SerializeObject(r.Capture());long repeat=AiProfile.SecondsToTicks(AiProfile.Initial.Value("decision.repeatOrderSeconds"),30);
            var action=(PlayableAiAction)typeof(AiTacticalExecutor).GetMethod("RecoveryAssembly",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{o,army,p,99L,repeat,records});
            Assert.AreEqual(expected,action!=null);if(expected){Assert.AreEqual(PlayableCommandKind.Follow,action.Kind);Assert.AreEqual(1,action.TargetId);CollectionAssert.AreEqual(new[]{4},action.EntityIds);}
            Assert.AreEqual(before,Newtonsoft.Json.JsonConvert.SerializeObject(r.Capture()),"No fake promotion before applied native assembly");
            if(mode=="ready")Assert.True(AiTacticalExecutor.Ready(o,army,p).Any(u=>u.Id==4),"Physical Ready alone still needs an effective assembly order during recovery");
        }
    }
}
