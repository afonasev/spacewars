using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiLastHeadquartersDefenseTests
    {
        private static readonly PlayableProfile P=PlayableProfile.Default;
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static object Field(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);
        private static AiArmyRegistry Registry()=>new AiArmyRegistry("player-1",71,AiProfile.Initial,AiDifficulty.Fighter);
        private static PlayableAiObservation Observation(int own=1,int headquarters=1,bool damage=true,bool visible=true,bool routes=true,bool held=false,bool humanMove=false,bool outpost=false)
        {
            var target=new NavPoint(8,0);
            var entities=Enumerable.Range(1,own).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-12,id*2),100,false,0,0,0,
                humanMove?new PlayableTacticalOrderSnapshot(id,PlayableOwner.Player,71,1,1,PlayableTacticalOrderKind.Move,new NavPoint(-20,0),0):null,held:held,
                orderStamp:humanMove?new PlayableOrderStamp(id,PlayableOwner.Player,71,1,1,1,PlayableOrderOrigin.Human,PlayableCommandKind.Move,null,0,0):null))
                .Concat(Enumerable.Range(101,4).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(8,(id-101)*2),100,false,0,0,0))).ToArray();
            var buildings=Enumerable.Range(60,headquarters).Select(id=>new PlayableBuildingSnapshot(id,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(0,(id-60)*20),P.HeadquartersHealth-8,1,0,0,default))
                .Concat(outpost?new[]{new PlayableBuildingSnapshot(80,PlayableOwner.Player,PlayableBuildingKind.Outpost,new NavPoint(-20,-10),100,1,0,0,default)}:Array.Empty<PlayableBuildingSnapshot>()).ToArray();
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),visible?50:3)},Array.Empty<KnownBuilding>());
            var proofs=routes?entities.Where(u=>u.Owner==PlayableOwner.Player).Select(u=>new PlayableRouteProof(u.Id,u.Owner,71,10,1,u.Position,P.TankCollisionRadius,PlayableRouteTargetKind.VisibleEnemy,101,target,target,new[]{target})).ToArray():Array.Empty<PlayableRouteProof>();
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,7,10,10,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,entities,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                vision:vision.Snapshot(),routeProofs:proofs,ownCenterDamage:damage?new[]{new PlayableCenterDamageSnapshot(60,PlayableOwner.Player,101,71,9,8)}:Array.Empty<PlayableCenterDamageSnapshot>()));
        }
        [TestCase(false)][TestCase(true)]
        public void LastHeadquarters_OneDefenderFightsSuperiorForceEvenWithOutpost(bool outpost)
        {
            var o=Observation(outpost:outpost);var r=Registry();var d=new AiDefensePlanner(o.OwnerId,71);
            Assert.Less(AiDefensePlanner.Force(o.Entities.Where(e=>e.Owner==o.Owner).ToArray(),P,AiProfile.Initial),AiDefensePlanner.Force(o.Entities.Where(e=>e.Owner!=o.Owner).ToArray(),P,AiProfile.Initial));
            var action=d.Propose(o,P,AiProfile.Initial,r);Assert.NotNull(action);CollectionAssert.AreEqual(new[]{1},action.EntityIds);Assert.AreEqual(101,action.TargetId);
            d.Commit(o,action,r);Assert.AreEqual(1,r.Capture().Armies.Single().MinimumViableForce);Assert.AreEqual(AiArmyRole.MobileDefense,r.Capture().Armies.Single().Role);
        }
        [TestCase("second-hq")][TestCase("no-damage")][TestCase("hidden")][TestCase("no-route")][TestCase("held")][TestCase("human-move")]
        public void LastHeadquarters_DoesNotBypassOrdinaryPolicyOrAuthority(string control)
        {
            var o=Observation(own:control=="second-hq"||control=="no-damage"?3:1,headquarters:control=="second-hq"?2:1,damage:control!="no-damage",visible:control!="hidden",routes:control!="no-route",held:control=="held",humanMove:control=="human-move");
            var r=Registry();Assert.IsNull(new AiDefensePlanner(o.OwnerId,71).Propose(o,P,AiProfile.Initial,r));Assert.IsEmpty(r.Capture().Armies);
        }
        [Test]
        public void LastHeadquarters_ExistingInsufficientArmyPreemptsAtomically()
        {
            var o=Observation(own:3);var r=Registry();Assert.True(r.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var id));
            var d=new AiDefensePlanner(o.OwnerId,71);var action=d.Propose(o,P,AiProfile.Initial,r);Assert.NotNull(action);CollectionAssert.AreEqual(new[]{1,2,3},action.EntityIds);
            var intent=new AiIntent("survival",AiDefensePlanner.Policy,action,100,0,0,action.EntityIds.Select(i=>"recipient:"+i),2,1,"last HQ");Assert.Null(r.Reject(intent));
            d.Commit(o,action,r);Assert.AreEqual(id,d.ArmyId);Assert.AreEqual(AiDefensePlanner.Policy,r.Capture().Armies.Single().TacticalOwner);
        }
        [Test]
        public void LastHeadquarters_ActiveDefenseRetainsHumanOrders()
        {
            var o=Observation();var r=Registry();var d=new AiDefensePlanner(o.OwnerId,71);var first=d.Propose(o,P,AiProfile.Initial,r);d.Commit(o,first,r);
            d.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,first.ActionId,10,1,10,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",o.OwnerId,first.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,1,1)));
            Assert.IsNull(d.Propose(Observation(humanMove:true),P,AiProfile.Initial,r),"Survival must not replace a human command even on an existing Defense member");
        }
        [Test]
        public void LastHeadquarters_ReinforcementsEnterCombatWithoutSafeAssembly()
        {
            var o=Observation();var r=Registry();var d=new AiDefensePlanner(o.OwnerId,71);var first=d.Propose(o,P,AiProfile.Initial,r);d.Commit(o,first,r);
            d.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,first.ActionId,10,1,10,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",o.OwnerId,first.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,1,1)));
            var wave=Observation(own:3);Assert.True(r.TryReinforce(wave,d.ArmyId,AiDefensePlanner.Policy,new[]{2}));
            Assert.False(wave.RouteProofs.Any(route=>route.Kind==PlayableRouteTargetKind.FriendlyAnchor));
            var action=d.Propose(wave,P,AiProfile.Initial,r);Assert.NotNull(action);CollectionAssert.AreEqual(new[]{1,2,3},action.EntityIds);
            d.Commit(wave,action,r);Assert.IsEmpty(r.Capture().Armies.Single().Reinforcements);CollectionAssert.AreEqual(new[]{1,2,3},r.Capture().Armies.Single().Members);
        }
        [Test]
        public void LastHeadquarters_ActualOppositeFactoryDefendersAdvanceAndFire()
        {
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-4,0),new NavPoint(-4,6),pin:1),new OfflineStart("east",2,new NavPoint(24,24),new NavPoint(18,24),pin:2)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,new[]{new TerritorySlot(1,new NavPoint(-14,0),0),new TerritorySlot(2,new NavPoint(-4,-10),0)}),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())};
            var roster=new[]{new OfflineParticipant("west-owner",1,1,OfflineControl.Ai),new OfflineParticipant("east-owner",2,2,OfflineControl.Human)};
            var scenario=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-14,-5))}.Concat(Enumerable.Range(0,3).Select(i=>new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(5,(i-1)*3)))).ToArray();
            var c=new OfflineMatchConfiguration(P,"last-hq-defense","opposite-factory","UnityHostRouteService",7,roster,starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:scenario,
                scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Factory,1,1,new NavPoint(-14,0)),new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,2,new NavPoint(-4,-10))});
            var a=new PlayableAuthorityTick(c,71);
            try
            {
                var initial=a.ParticipantView("west-owner");var defender=initial.Entities.Single(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank);var hq=initial.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
                var attackers=a.ParticipantView("east-owner").Entities.Where(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank).Select(u=>u.Id).ToArray();
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,"east-owner",PlayableCommandKind.Attack,attackers,default,targetId:hq.Id)).Status);
                int wave=0;bool observedDamage=false,advanced=false,coreShot=false,waveShot=false,enemyDamaged=false;
                long damageTick=-1,coreShotTick=-1,waveShotTick=-1;
                using(var host=new UnityHostRouteService())
                for(int tick=0;tick<900;tick++)
                {
                    for(int attempts=0;!a.TryAdvance();attempts++){Assert.Less(attempts,4096);host.Service(a,64);}
                    var view=a.ParticipantView("west-owner");
                    if(view.OwnCenterDamage.Any(d=>d.CenterId==hq.Id)){observedDamage=true;if(damageTick<0)damageTick=a.Tick;}
                    var unit=view.Entities.FirstOrDefault(u=>u.Id==defender.Id);advanced|=observedDamage&&unit!=null&&unit.Position.X>defender.Position.X+P.TankCollisionRadius*2;
                    coreShot|=observedDamage&&view.Projectiles.Any(p=>p.OwnerId==defender.Id&&attackers.Contains(p.TargetId));
                    waveShot|=wave!=0&&view.Projectiles.Any(p=>p.OwnerId==wave&&attackers.Contains(p.TargetId));
                    if(coreShot&&coreShotTick<0)coreShotTick=a.Tick;if(waveShot&&waveShotTick<0)waveShotTick=a.Tick;
                    enemyDamaged|=observedDamage&&view.Entities.Any(u=>attackers.Contains(u.Id)&&u.Health<P.TankHealth);
                    if(observedDamage&&wave==0)
                    {
                        var domain=Field(a,"domain");var before=view.Entities.Select(u=>u.Id).ToArray();
                        domain.GetType().GetMethod("SpawnProduced",F).Invoke(domain,new object[]{new NavPoint(-14,-7),(NavPoint?)new NavPoint(-25,-10),PlayableOwner.Player,PlayableEntityKind.Tank});
                        wave=a.ParticipantView("west-owner").Entities.Single(u=>u.Owner==PlayableOwner.Player&&!before.Contains(u.Id)).Id;
                        Assert.Null(a.ParticipantView("west-owner").Entities.Single(u=>u.Id==wave).CurrentOrder,"Production must await army admission, not factory rally");
                    }
                    if(observedDamage&&advanced&&coreShot&&waveShot&&enemyDamaged)break;
                }
                TestContext.WriteLine($"lastHQ damageTick={damageTick} coreShotTick={coreShotTick} waveShotTick={waveShotTick} finalTick={a.Tick} advanced={advanced} enemyDamaged={enemyDamaged}");
                Assert.True(observedDamage,"Attackers must actually damage the sole HQ");Assert.True(advanced,"Rear defender must physically advance");Assert.True(coreShot,"Original rear defender must actually fire on an attacker");Assert.True(waveShot,"Production-side reinforcement must actually fire, not merely receive a receipt");Assert.True(enemyDamaged,"Defenders must actually damage an attacker");
                var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);try{CollectionAssert.AreEqual(bytes,restored.CaptureBytes());}finally{restored.Stop();}
            }
            finally{a.Stop();}
        }
    }
}
