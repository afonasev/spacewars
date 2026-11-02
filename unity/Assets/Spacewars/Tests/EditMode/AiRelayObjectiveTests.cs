using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiRelayObjectiveTests
    {
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile Ai=AiProfile.Initial;
        static readonly NavPoint Target=new NavPoint(5,0);
        static readonly TerritorySite Site=new TerritorySite(4,PlayableBuildingKind.RelayTower,new NavPoint(0,0),Array.Empty<TerritorySlot>());
        static PlayableEntitySnapshot Unit(int id,PlayableOwner owner,NavPoint position,PlayableEntityKind kind=PlayableEntityKind.Explorer,int hp=100,bool held=false)=>
            new PlayableEntitySnapshot(id,owner,kind,position,hp,false,0,0,0,held:held);
        static PlayableAiObservation View(long tick=30,PlayableOwner? owner=null,PlayableOwner? claimant=null,bool contested=false,
            PlayableEntitySnapshot[] units=null,bool relay=true,bool visible=true,double progress=0)
        {
            units=units??new[]{Unit(13,PlayableOwner.Player,new NavPoint(-10,0))};
            var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human),new OfflineParticipant("ally",3,1,OfflineControl.Human)};
            var objective=new PlayablePublicScoutObjective(4,Target,relay?PlayablePublicScoutObjectiveRole.Relay:PlayablePublicScoutObjectiveRole.ExpansionIncome,true);
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,units,
                Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                sites:visible?new[]{new TerritorySiteSnapshot(Site,claimant,owner,progress,contested,0,owner.HasValue)}:Array.Empty<TerritorySiteSnapshot>(),
                publicScoutObjectives:new[]{objective},routeProofs:units.Where(u=>u.Owner==PlayableOwner.Player).Select(u=>new PlayableRouteProof(u.Id,PlayableOwner.Player,71,tick,1,u.Position,PlayableUnitRules.Radius(P,u.Kind),PlayableRouteTargetKind.PublicObjective,4,Target,Target,new[]{Target})).ToArray(),
                activeProfile:P,ownerId:"player-1",participants:roster,team:1));
        }
        static AiKnowledgeState Knowledge(PlayableAiObservation o){var k=new AiKnowledgeTracker(o.OwnerId,o.Generation);k.Observe(o,Ai);return k.Capture();}
        static AiArmyRegistry Registry()=>new AiArmyRegistry("player-1",71,Ai,AiDifficulty.Fighter);
        static PlayableAiAction Plan(PlayableAiObservation o,AiArmyRegistry r)=>new AiScoutPlanner().Plan(o,Knowledge(o),r,P,Ai,1);
        [Test] public void NeutralAndEnemyRelaysUseOrdinaryCaptureMission()
        {
            foreach(var owner in new PlayableOwner?[]{null,PlayableOwner.Enemy})
            {var o=View(owner:owner);var r=Registry();var action=Plan(o,r);Assert.NotNull(action);Assert.AreEqual(4,action.SiteId);Assert.AreEqual(PlayableCommandKind.AttackMove,action.Kind);
             new AiScoutPlanner().Commit(o,action,r);Assert.AreEqual("relay:4",r.Capture().Armies.Single().Objective);Assert.AreEqual(0,o.Credits);}
        }
        [Test] public void ArrivalKeepsClaimUntilCaptureAndCurrentRegistryRestores()
        {
            var r=Registry();var planner=new AiScoutPlanner();var start=View();planner.Commit(start,Plan(start,r),r);
            var during=View(tick:60,claimant:PlayableOwner.Player,progress:.5,units:new[]{Unit(13,PlayableOwner.Player,Target)});
            planner.Observe(during,Knowledge(during),r,Ai,1,gameplay:P);Assert.Greater(r.ArmyFor(13),0);Assert.IsNull(Plan(during,r));
            var restored=AiArmyRegistry.Restore(r.Capture(),Ai,AiDifficulty.Fighter,60);Assert.AreEqual(r.ArmyFor(13),restored.ArmyFor(13));
            var done=View(tick:90,owner:PlayableOwner.Player,claimant:PlayableOwner.Player,progress:1,units:new[]{Unit(13,PlayableOwner.Player,Target)});
            planner.Observe(done,Knowledge(done),r,Ai,1,gameplay:P);Assert.Zero(r.ArmyFor(13));Assert.IsNull(Plan(done,r));
        }
        [Test] public void AlliedSafeControlOrClaimIsNotStolenButThreatGetsHelp()
        {
            Assert.IsNull(Plan(View(owner:PlayableOwner.Third,claimant:PlayableOwner.Third),Registry()));
            Assert.IsNull(Plan(View(claimant:PlayableOwner.Third,progress:.5),Registry()));
            var threatened=View(owner:PlayableOwner.Third,claimant:PlayableOwner.Third,contested:true,units:new[]{Unit(13,PlayableOwner.Player,new NavPoint(-10,0)),Unit(20,PlayableOwner.Enemy,new NavPoint(0,2),hp:1),Unit(21,PlayableOwner.Third,new NavPoint(0,-2))});
            Assert.NotNull(Plan(threatened,Registry()),"Known viable allied defense is useful even though the relay cannot change to our ownership");
        }
        [Test] public void LosingContestIsRejectedAndHumanHoldIsPreserved()
        {
            var enemies=Enumerable.Range(20,8).Select(i=>Unit(i,PlayableOwner.Enemy,new NavPoint(i-20,2),PlayableEntityKind.Tank)).ToArray();
            var losing=View(contested:true,units:new[]{Unit(13,PlayableOwner.Player,new NavPoint(-10,0))}.Concat(enemies).ToArray());
            Assert.IsNull(Plan(losing,Registry()));Assert.IsNull(Plan(View(units:new[]{Unit(13,PlayableOwner.Player,new NavPoint(-10,0),held:true)}),Registry()));
        }
        [Test] public void RelayCombatPermissionCannotEscapeItsExactMission()
        {
            var o=View();var r=Registry();var action=Plan(o,r);new AiScoutPlanner().Commit(o,action,r);
            AiIntent Intent(PlayableAiAction a)=>new AiIntent("test","scout",a,100,0,0,new[]{"recipient:13"});
            Assert.IsNull(r.Reject(Intent(action)));
            var foreignSite=new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,71,30,PlayableCommandKind.AttackMove,new[]{13},Target,siteId:9);
            Assert.IsNotNull(r.Reject(Intent(foreignSite)));
        }
        [Test] public void NoRelayMapDoesNotEnableRelayPolicyAndUnknownControlIsScouted()
        {
            var ordinary=View(relay:false);Assert.False(AiRelayObjectives.HasRelays(ordinary));
            Assert.IsNull(AiRelayObjectives.PlanScout(ordinary,Knowledge(ordinary),Registry(),P,Ai,1,0));
            var unknown=View(visible:false);Assert.True(AiRelayObjectives.Needed(unknown,P,4));Assert.NotNull(Plan(unknown,Registry()));
        }
        [Test] public void ProfileBarrierPreservesRestoredRelayMission()
        {
            var o=View();var owner=new PlayableAiOwnerLoop(P,PlayableAiOpeningComposition.Initialize(7,o.OwnerId,forcedOpening:PlayableAiOpening.Safe),71);
            var field=typeof(PlayableAiOwnerLoop).GetField("armies",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var r=(AiArmyRegistry)field.GetValue(owner);new AiScoutPlanner().Commit(o,Plan(o,r),r);
            byte[] Save(PlayableAiOwnerLoop loop){using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))loop.WriteState(writer);return stream.ToArray();}}
            PlayableAiOwnerLoop restored;using(var stream=new System.IO.MemoryStream(Save(owner)))using(var reader=new System.IO.BinaryReader(stream))restored=PlayableAiOwnerLoop.ReadState(reader,P,Ai,71,7,30);
            var data=Ai.CopyData();data.revision++;var next=new AiProfile(data);
            owner.Rebind(next);restored.Rebind(next);
            Assert.Greater(((AiArmyRegistry)field.GetValue(restored)).ArmyFor(13),0,"A valid relay assignment survives without a warm public-map cache");
            CollectionAssert.AreEqual(Save(owner),Save(restored));owner.Stop();restored.Stop();
        }
        [Test] public void ArmySelectsContestedRelayAndRetainsMissionAtArrival()
        {
            var tanks=Enumerable.Range(1,3).Select(i=>Unit(i,PlayableOwner.Player,new NavPoint(-12,i*3),PlayableEntityKind.Tank)).ToArray();
            var o=View(owner:PlayableOwner.Enemy,units:tanks);var r=Registry();var planner=new AiArmyPlanner(o.OwnerId,o.Generation);planner.Rebind(P);
            var opening=PlayableAiOpeningComposition.Initialize(7,o.OwnerId).WithPhase(PlayableAiOpeningPhase.Complete);
            var action=planner.ProposeWithRecords(o,opening,P,Ai,r,Array.Empty<PlayableAiTraceRecord>());Assert.NotNull(action);Assert.AreEqual(4,action.SiteId);
            planner.Commit(o,action,r);Assert.AreEqual(PlayableRouteTargetKind.PublicObjective,planner.Capture().TargetKind);
            var arrived=View(tick:60,owner:PlayableOwner.Enemy,progress:.5,units:tanks.Select((u,i)=>Unit(u.Id,u.Owner,new NavPoint(3,i),u.Kind)).ToArray());
            planner.Observe(arrived,r,Ai,Array.Empty<PlayableAiTraceRecord>(),opening,true);Assert.True(planner.Active,"Relay arrival cannot be treated as an empty enemy start");
        }
    }
}
