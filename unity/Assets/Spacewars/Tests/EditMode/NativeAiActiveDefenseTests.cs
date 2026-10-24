using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiActiveDefenseTests
    {
        private static readonly PlayableProfile P=PlayableProfile.Default;
        private static PlayableAiObservation View(bool scout=false,bool routes=true,int tanks=1,bool offense=false,PlayableTacticalOrderKind? order=null)
        {
            var own=Enumerable.Range(1,tanks).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(id,0),P.TankHealth,false,0,0,0,
                order.HasValue?new PlayableTacticalOrderSnapshot(id,PlayableOwner.Player,71,1,10,order.Value,new NavPoint(18,0),0):null)).ToArray();
            var enemy=scout?new[]{new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Explorer,new NavPoint(21,0),P.ExplorerHealth,false,0,0,0)}:Array.Empty<PlayableEntitySnapshot>();
            var buildings=new[]{new PlayableBuildingSnapshot(60,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(-P.HeadquartersVisionRange,0),P.HeadquartersHealth,1,0,0,default),
                new PlayableBuildingSnapshot(61,PlayableOwner.Player,PlayableBuildingKind.Mine,new NavPoint(20,0),(int)P.MineHealth,1,0,0,default)};
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),50)},Array.Empty<KnownBuilding>());
            var proofs=routes?own.SelectMany(u=>new[]{new PlayableRouteProof(u.Id,u.Owner,71,10,1,u.Position,P.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,61,buildings[1].Position,new NavPoint(18,0),new[]{new NavPoint(18,0)})}
                .Concat(scout?new[]{new PlayableRouteProof(u.Id,u.Owner,71,10,1,u.Position,P.TankCollisionRadius,PlayableRouteTargetKind.VisibleEnemy,101,enemy[0].Position,enemy[0].Position,new[]{enemy[0].Position})}:Array.Empty<PlayableRouteProof>())
                .Concat(offense?new[]{new PlayableRouteProof(u.Id,u.Owner,71,10,1,u.Position,P.TankCollisionRadius,PlayableRouteTargetKind.VisibleEnemy,102,new NavPoint(30,0),new NavPoint(30,0),new[]{new NavPoint(30,0)})}:Array.Empty<PlayableRouteProof>())).ToArray():Array.Empty<PlayableRouteProof>();
            if(offense)buildings=buildings.Concat(new[]{new PlayableBuildingSnapshot(102,PlayableOwner.Enemy,PlayableBuildingKind.Headquarters,new NavPoint(30,0),P.HeadquartersHealth,1,0,0,default)}).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,7,10,10,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,own.Concat(enemy).ToArray(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:proofs));
        }
        private static AiArmyRegistry Registry()=>new AiArmyRegistry("player-1",71,AiProfile.Initial,AiDifficulty.Fighter);
        [Test] public void LoneTankInterceptsVisibleScoutAtMineOutsideHomePerimeter()
        {
            var o=View(scout:true);Assert.Greater(o.Entities.Single(e=>e.Id==101).Position.X-o.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Headquarters).Position.X,P.HeadquartersVisionRange);
            var r=Registry();var d=new AiDefensePlanner(o.OwnerId,o.Generation);var a=d.Propose(o,P,AiProfile.Initial,r);
            Assert.NotNull(a);Assert.AreEqual(101,a.TargetId);CollectionAssert.AreEqual(new[]{1},a.EntityIds);d.Commit(o,a,r);
            Assert.AreEqual(AiArmyRole.MobileDefense,r.Capture().Armies.Single().Role);Assert.AreEqual(1,r.Capture().Armies.Single().MinimumViableForce);Assert.AreEqual(3,r.MajorCap);
        }
        [Test] public void ScoutInterceptionRetainsCurrentCertifiedRouteGuard()
        {var o=View(scout:true,routes:false);Assert.IsNull(new AiDefensePlanner(o.OwnerId,o.Generation).Propose(o,P,AiProfile.Initial,Registry()));}
        [Test] public void QuietGuardUsesOwnedMineAndCurrentStateRoundtripsExactly()
        {
            var o=View();var r=Registry();var p=new AiArmyPlanner(o.OwnerId,o.Generation);p.Rebind(P);
            var a=p.ProposePositioning(o,P,AiProfile.Initial,r);Assert.NotNull(a);Assert.AreEqual(PlayableCommandKind.Move,a.Kind);Assert.AreEqual(61,a.ParentId);
            p.Commit(o,a,r);Assert.AreEqual(PlayableRouteTargetKind.FriendlyAnchor,p.Capture().TargetKind);Assert.AreEqual(AiArmyRole.MobileDefense,r.Capture().Armies.Single().Role);
            byte[] Bytes(AiArmyPlanner planner){using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))planner.WriteState(w);return m.ToArray();}}
            var before=Bytes(p);var restoredRegistry=AiArmyRegistry.Restore(r.Capture(),AiProfile.Initial,AiDifficulty.Fighter,o.Tick);
            var restored=new AiArmyPlanner(o.OwnerId,o.Generation);restored.Rebind(P);using(var m=new MemoryStream(before))using(var reader=new BinaryReader(m))restored.ReadState(reader,o.Tick,restoredRegistry);
            CollectionAssert.AreEqual(before,Bytes(restored));p.Observe(o,r,AiProfile.Initial);restored.Observe(o,restoredRegistry,AiProfile.Initial);CollectionAssert.AreEqual(Bytes(p),Bytes(restored));
            Assert.IsNull(p.Deployment(o,r),"Quiet defensive positioning is not an offensive deployment.");
        }
        [Test] public void QuietGuardDoesNotInventARouteOrReplaceAnEffectiveMove()
        {
            var r=Registry();Assert.IsNull(new AiArmyPlanner("player-1",71).ProposePositioning(View(routes:false),P,AiProfile.Initial,r));
            Assert.IsNull(new AiArmyPlanner("player-1",71).ProposePositioning(View(order:PlayableTacticalOrderKind.Move),P,AiProfile.Initial,r));
        }
        [Test] public void QuietGuardCannotExceedExistingMajorCapOrUseForeignRegistry()
        {
            var o=View(tanks:10);var r=Registry();
            for(int i=0;i<r.MajorCap;i++)Assert.True(r.TryCreate(o,AiArmyRole.Attack,"future-escort",Enumerable.Range(i*3+1,3),out _));
            var planner=new AiArmyPlanner(o.OwnerId,o.Generation);Assert.IsNull(planner.ProposePositioning(o,P,AiProfile.Initial,r));Assert.Zero(r.ArmyFor(10));
            Assert.Throws<ArgumentException>(()=>planner.ProposePositioning(o,P,AiProfile.Initial,new AiArmyRegistry("other",71,AiProfile.Initial,AiDifficulty.Fighter)));
        }
        [Test] public void QuietGuardReleasesHumanOrderAndDoesNotRecruitItAgain()
        {
            var o=View();var r=Registry();var planner=new AiArmyPlanner(o.OwnerId,o.Generation);planner.Rebind(P);
            var a=planner.ProposePositioning(o,P,AiProfile.Initial,r);planner.Commit(o,a,r);
            planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,a.ActionId,10,1,10,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"guard applied",ownerId:o.OwnerId,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity,receiptIdentity:new AiReceiptIdentity(71,o.OwnerId,1,1)));
            var u=o.Entities.Single();var point=new NavPoint(-5,0);
            var order=new PlayableTacticalOrderSnapshot(u.Id,u.Owner,o.Generation,7,10,PlayableTacticalOrderKind.Move,point,0);
            var stamp=new PlayableOrderStamp(u.Id,u.Owner,o.Generation,0,7,10,PlayableOrderOrigin.Human,PlayableCommandKind.Move,null,0,0);
            var human=new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Health,false,0,0,0,order,orderStamp:stamp);
            var next=PlayableAiObservation.From(new PlayableSnapshot(o.ProfileId,o.ProfileRevision,o.Generation,o.Seed,o.Tick,o.Tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,
                new[]{human},o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision,routeProofs:o.RouteProofs.ToArray()));
            var humanOrder=Newtonsoft.Json.JsonConvert.SerializeObject(human.CurrentOrder);var humanStamp=Newtonsoft.Json.JsonConvert.SerializeObject(human.OrderStamp);
            planner.Observe(next,r,AiProfile.Initial);Assert.False(planner.Active);Assert.Zero(r.ArmyFor(u.Id));
            Assert.IsNull(planner.ProposePositioning(next,P,AiProfile.Initial,r));
            Assert.AreEqual(humanOrder,Newtonsoft.Json.JsonConvert.SerializeObject(human.CurrentOrder));Assert.AreEqual(humanStamp,Newtonsoft.Json.JsonConvert.SerializeObject(human.OrderStamp));
        }
        [Test] public void ViableAttackReleasesTheWholeQuietGuardAssignment()
        {
            var o=View();var r=Registry();var p=new AiArmyPlanner(o.OwnerId,o.Generation);p.Rebind(P);var a=p.ProposePositioning(o,P,AiProfile.Initial,r);p.Commit(o,a,r);
            p.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,a.ActionId,10,1,10,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"guard applied",ownerId:o.OwnerId,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity,receiptIdentity:new AiReceiptIdentity(71,o.OwnerId,1,1)));
            var ready=View(tanks:3,offense:true);p.Observe(ready,r,AiProfile.Initial);Assert.False(p.Active);Assert.Zero(r.ArmyFor(1));
            var opening=PlayableAiOpeningComposition.Initialize(7,o.OwnerId,forcedOpening:PlayableAiOpening.BlindRush);
            var attack=p.Propose(ready,opening,P,AiProfile.Initial,r);Assert.NotNull(attack);Assert.AreEqual(PlayableCommandKind.Attack,attack.Kind);p.Commit(ready,attack,r);
            Assert.AreEqual(AiArmyRole.Attack,r.Capture().Armies.Single(g=>g.Phase!=AiArmyPhase.Disbanded).Role);r.AssertInvariants();
        }
    }
}
