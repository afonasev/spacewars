using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class ExplorerBaseDefenseTests
    {
        private static readonly PlayableProfile P=PlayableProfile.Default;
        [SetUp] public void TraceCase()=>TestContext.Progress.WriteLine("EXPLORER_DEFENSE_CASE "+TestContext.CurrentContext.Test.FullName);
        private static PlayableAiObservation View(string control="",bool survival=false,bool replacement=false,int tanks=3)
        {
            var point=control=="distant"?new NavPoint(50,0):new NavPoint(-4,5);
            var own=Enumerable.Range(1,tanks).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-id,0),P.TankHealth,false,0,0,0));
            var order=control=="human"?new PlayableTacticalOrderSnapshot(10,PlayableOwner.Player,71,1,10,PlayableTacticalOrderKind.Move,new NavPoint(-20,0),0):null;
            var stamp=control=="human"?new PlayableOrderStamp(10,PlayableOwner.Player,71,0,1,10,PlayableOrderOrigin.Human,PlayableCommandKind.Move,null,0,0):null;
            var explorer=new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Explorer,point,P.ExplorerHealth,false,0,0,0,order,held:control=="held",orderStamp:stamp);
            var foe=new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(8,0),P.TankHealth,false,0,0,0);
            var units=own.Concat(new[]{explorer,foe}).ToArray();
            var buildings=new[]{new PlayableBuildingSnapshot(60,PlayableOwner.Player,PlayableBuildingKind.Headquarters,default,P.HeadquartersHealth,1,0,0,default)};
            if(replacement)buildings=buildings.Concat(new[]{new PlayableBuildingSnapshot(61,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(-8,0),P.FactoryHealth,1,0,0,default)}).ToArray();
            var vision=new PlayableVision(0,70,70,1,1);vision.Refresh(new[]{new VisionSource(default,70)},Array.Empty<KnownBuilding>());
            var routes=units.Where(u=>u.Owner==PlayableOwner.Player&&(control!="no-route"||u.Id!=10)).Select(u=>new PlayableRouteProof(u.Id,u.Owner,71,10,1,u.Position,PlayableUnitRules.Radius(P,u.Kind),PlayableRouteTargetKind.VisibleEnemy,101,foe.Position,foe.Position,new[]{foe.Position})).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,7,10,10,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:routes,
                population:replacement?new PlayablePopulationSnapshot(4,0,100):null,ownCenterDamage:survival?new[]{new PlayableCenterDamageSnapshot(60,PlayableOwner.Player,101,71,9,8)}:Array.Empty<PlayableCenterDamageSnapshot>()));
        }
        private static AiArmyRegistry Registry()=>new AiArmyRegistry("player-1",71,AiProfile.Initial,AiDifficulty.Fighter);
        private static AiIntent Intent(PlayableAiAction a)=>new AiIntent("local-defense",AiDefensePlanner.Policy,a,120,0,0,a.EntityIds.Select(id=>"recipient:"+id),2,1,"visible local threat");
        [TestCase(false)][TestCase(true)]
        public void NearbyScoutYieldsOnlyOnAcceptedDefenseAndRestoresExactly(bool survival)
        {
            var o=View(survival:survival);var r=Registry();Assert.True(r.TryCreate(o,AiArmyRole.Scout,AiScoutPlanner.Policy,new[]{10},out var scout));
            var before=JsonConvert.SerializeObject(r.Capture());var d=new AiDefensePlanner(o.OwnerId,71);var a=d.Propose(o,P,AiProfile.Initial,r);
            Assert.NotNull(a);Assert.Contains(10,a.EntityIds.ToArray());Assert.AreEqual(before,JsonConvert.SerializeObject(r.Capture()),"proposal never releases the scout");Assert.IsNull(r.Reject(Intent(a)));
            d.Commit(o,a,r,P);Assert.AreEqual(AiArmyPhase.Disbanded,r.Capture().Armies.Single(x=>x.Id==scout).Phase);
            var army=r.Capture().Armies.Single(x=>x.Id==d.ArmyId);Assert.AreEqual(AiArmyRole.MobileDefense,army.Role);Assert.Contains(10,army.Members);Assert.IsEmpty(army.Reinforcements);r.AssertInvariants();
            var restored=AiArmyRegistry.Restore(r.Capture(),AiProfile.Initial,AiDifficulty.Fighter,o.Tick);Assert.AreEqual(JsonConvert.SerializeObject(r.Capture()),JsonConvert.SerializeObject(restored.Capture()));
            byte[] Bytes(AiDefensePlanner planner){using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))planner.WriteState(w);return m.ToArray();}}
            var bytes=Bytes(d);var next=new AiDefensePlanner(o.OwnerId,71);using(var m=new MemoryStream(bytes))using(var reader=new BinaryReader(m))next.ReadState(reader,o.Tick,restored);CollectionAssert.AreEqual(bytes,Bytes(next));
        }
        [Test] public void FreeNearbyExplorerJoinsEvenWhenOtherDefendersAlreadySuffice()
        {
            var o=View();var r=Registry();var a=new AiDefensePlanner(o.OwnerId,71).Propose(o,P,AiProfile.Initial,r);Assert.NotNull(a);Assert.Contains(10,a.EntityIds.ToArray());
        }
        [Test] public void WholeOffensiveArmyCanAcceptLocalScoutWithoutTakingAnotherArmy()
        {
            var o=View();var r=Registry();Assert.True(r.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var army));Assert.True(r.TryCreate(o,AiArmyRole.Scout,AiScoutPlanner.Policy,new[]{10},out _));
            var d=new AiDefensePlanner(o.OwnerId,71);var a=d.Propose(o,P,AiProfile.Initial,r);CollectionAssert.AreEquivalent(new[]{1,2,3,10},a.EntityIds);Assert.IsNull(r.Reject(Intent(a)));d.Commit(o,a,r,P);Assert.AreEqual(army,d.ArmyId);r.AssertInvariants();
        }
        [Test] public void ActiveDefenseRecruitsNearbyScoutWithoutLeavingItInAssembly()
        {
            var o=View();var r=Registry();var d=new AiDefensePlanner(o.OwnerId,71){ProtectedUnit=10};var first=d.Propose(o,P,AiProfile.Initial,r);d.Commit(o,first,r,P);
            d.ObserveReceipt(new PlayableAiTraceRecord("fixture",first.ActionId,10,1,10,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"fixture",ownerId:o.OwnerId,sourceIdentity:first.SourceIdentity,receiptIdentity:new AiReceiptIdentity(71,o.OwnerId,1,1),policy:AiDefensePlanner.Policy,kind:first.Kind));
            Assert.Zero(d.PendingId);Assert.True(r.TryCreate(o,AiArmyRole.Scout,AiScoutPlanner.Policy,new[]{10},out _));d.ProtectedUnit=0;
            var next=d.Propose(o,P,AiProfile.Initial,r);Assert.NotNull(next);Assert.Contains(10,next.EntityIds.ToArray());d.Commit(o,next,r,P);var army=r.Capture().Armies.Single(x=>x.Id==d.ArmyId);Assert.Contains(10,army.Members);Assert.IsEmpty(army.Reinforcements);r.AssertInvariants();
        }
        [TestCase("distant")][TestCase("held")][TestCase("human")][TestCase("no-route")][TestCase("protected")][TestCase("transport")]
        public void RestrictedScoutKeepsAssignmentAndDoesNotJoinDefense(string control)
        {
            var o=View(control);var r=Registry();Assert.True(r.TryCreate(o,control=="transport"?AiArmyRole.Transport:AiArmyRole.Scout,AiScoutPlanner.Policy,new[]{10},out var scout));
            var d=new AiDefensePlanner(o.OwnerId,71){ProtectedUnit=control=="protected"?10:0};var a=d.Propose(o,P,AiProfile.Initial,r);
            Assert.NotNull(a);Assert.False(a.EntityIds.Contains(10));d.Commit(o,a,r,P);Assert.AreEqual(scout,r.ArmyFor(10));Assert.AreNotEqual(AiArmyPhase.Disbanded,r.Capture().Armies.Single(x=>x.Id==scout).Phase);
        }
        [Test] public void ScoutHandoffCannotConsumeAnExtraMajorSlotAtTheCap()
        {
            var o=View(tanks:6);var r=new AiArmyRegistry(o.OwnerId,71,AiProfile.Initial,AiDifficulty.Recruit);
            Assert.AreEqual(2,r.MajorCap);Assert.True(r.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var army));Assert.True(r.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{4,5,6},out _));Assert.True(r.TryCreate(o,AiArmyRole.Scout,AiScoutPlanner.Policy,new[]{10},out var scout));
            var d=new AiDefensePlanner(o.OwnerId,71);var fake=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,71,o.SnapshotSequence,PlayableCommandKind.Attack,new[]{10},new NavPoint(8,0),targetId:101,seed:o.Seed);
            var before=JsonConvert.SerializeObject(r.Capture());Assert.IsNotNull(r.Reject(Intent(fake)));Assert.Throws<InvalidOperationException>(()=>d.Commit(o,fake,r,P));Assert.AreEqual(before,JsonConvert.SerializeObject(r.Capture()));
            var actual=d.Propose(o,P,AiProfile.Initial,r);Assert.Contains(10,actual.EntityIds.ToArray());Assert.IsNull(r.Reject(Intent(actual)));d.Commit(o,actual,r,P);Assert.AreEqual(army,d.ArmyId);Assert.AreEqual(2,r.Capture().Armies.Count(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded));Assert.AreEqual(AiArmyPhase.Disbanded,r.Capture().Armies.Single(a=>a.Id==scout).Phase);
        }
        [Test] public void AirOpeningCapturerRemainsProtectedUntilReplacementIsAvailable()
        {
            var o=View();var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(o.Seed,o.OwnerId,forcedOpening:PlayableAiOpening.Air),71){PlayableProfileForObservation=P};e.Observe(o,null,null,AiProfile.Initial);Assert.AreEqual(10,e.ProtectedUnit);
            var r=Registry();Assert.True(r.TryCreate(o,AiArmyRole.Scout,AiScoutPlanner.Policy,new[]{10},out _));var d=new AiDefensePlanner(o.OwnerId,71){ProtectedUnit=e.ProtectedUnit};Assert.False(d.Propose(o,P,AiProfile.Initial,r).EntityIds.Contains(10));
            var replacement=View(replacement:true);Assert.True(AiOpeningExecutor.ReplacementAvailable(replacement,P,P.ExplorerCreditCost));e.SpendableCredits=P.ExplorerCreditCost;e.Observe(replacement,null,null,AiProfile.Initial);Assert.Zero(e.ProtectedUnit);d.ProtectedUnit=e.ProtectedUnit;Assert.Contains(10,d.Propose(replacement,P,AiProfile.Initial,r).EntityIds.ToArray());
        }
    }
}
