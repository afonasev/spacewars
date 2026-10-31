using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

public sealed class AiUtilityReservationTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static readonly PlayableProfile P=PlayableProfile.Default;
    static T Get<T>(object x,string field)=>(T)x.GetType().GetField(field,F).GetValue(x);
    static PlayableAiObservation View(bool routes=true,int explorers=3)
    {
        var targets=new[]{new PlayablePublicScoutObjective(12,new NavPoint(15,0),PlayablePublicScoutObjectiveRole.ExpansionIncome,true),new PlayablePublicScoutObjective(13,new NavPoint(0,15),PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true)};
        var units=new[]{25,27,29,31}.Take(explorers).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Explorer,default,P.ExplorerHealth,false,0,0,0))
            .Concat(new[]{14,16,18,26}.Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Hawk,default,(int)P.HawkHealth,false,0,0,0))).ToArray();
        var proofs=routes?(from u in units where u.Kind==PlayableEntityKind.Explorer from t in targets select new PlayableRouteProof(u.Id,u.Owner,71,100,1,u.Position,P.ExplorerCollisionRadius,PlayableRouteTargetKind.PublicObjective,t.SiteId,t.Approach,t.Approach,new[]{t.Approach})).ToArray():Array.Empty<PlayableRouteProof>();
        return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,41,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,new NavGeometry(32,Array.Empty<NavObstacle>(),1),units,Enumerable.Range(1,3).Select(id=>new PlayableBuildingSnapshot(id,PlayableOwner.Player,PlayableBuildingKind.Refinery,new NavPoint(id*5,0),P.RefineryHealth,1,0,0,default)).Concat(new[]{new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(20,0),P.FactoryHealth,1,0,0,default),new PlayableBuildingSnapshot(6,PlayableOwner.Player,PlayableBuildingKind.Hangar,new NavPoint(25,0),(int)P.HangarHealth,1,0,0,default)}).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,publicScoutObjectives:targets,routeProofs:proofs,population:new PlayablePopulationSnapshot(7,0,100),ownerId:"air-owner"));
    }
    static PlayableAiOwnerLoop Owner(PlayableAiObservation o)
    {
        var loop=new PlayableAiOwnerLoop(P,PlayableAiOpeningComposition.Initialize(41,"air-owner",forcedOpening:PlayableAiOpening.Air),71,null,AiProfile.Initial,AiDifficulty.Veteran);
        var opening=Get<AiOpeningExecutor>(loop,"openingExecutor");opening.SpendableCredits=o.Credits;opening.Observe(o,null,null,AiProfile.Initial);Assert.True(opening.Air);Assert.Zero(opening.ProtectedUnit);
        Get<AiKnowledgeTracker>(loop,"knowledge").Observe(o,AiProfile.Initial);return loop;
    }
    static int[] Bind(PlayableAiOwnerLoop loop,PlayableAiObservation o,bool prospective=false)
    {
        typeof(PlayableAiOwnerLoop).GetMethod("BindUtilityReservations",F).Invoke(loop,new object[]{o,0,prospective});
        return Get<HashSet<int>>(loop,"utilityReserved").OrderBy(id=>id).ToArray();
    }
    static long Army(PlayableAiObservation o,AiArmyRegistry registry)
    {Assert.True(registry.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{14,16,18,26},out var army));return army;}
    [Test] public void UsefulScoutCapacityDoesNotReserveUnassignedCombatExplorers()
    {
        var o=View();var loop=Owner(o);try{
            var registry=Get<AiArmyRegistry>(loop,"armies");long army=Army(o,registry);
            var planner=Get<AiScoutPlanner>(loop,"scoutPlanner");var knowledge=Get<AiKnowledgeTracker>(loop,"knowledge").Capture();
            Assert.True(planner.UsefulProductionDemand(o,knowledge,AiProfile.Initial,3));
            var scout=planner.Plan(o,knowledge,registry,P,AiProfile.Initial,3);Assert.AreEqual(25,scout.EntityIds.Single());planner.Commit(o,scout,registry);
            CollectionAssert.AreEqual(new[]{25},Bind(loop,o));
            AiTacticalExecutor.Observe(o,registry,army,P,Bind(loop,o));
            CollectionAssert.AreEqual(new[]{27,29},registry.Capture().Armies.Single(a=>a.Id==army).Members.Where(id=>id==27||id==29));
            Assert.AreNotEqual(army,registry.ArmyFor(25));registry.AssertInvariants();
        }finally{loop.Stop();}
    }
    [Test] public void OnlyTheCurrentRoutedScoutProposalIsProtectedFromReinforcement()
    {
        var o=View();var loop=Owner(o);try{
            var registry=Get<AiArmyRegistry>(loop,"armies");long army=Army(o,registry);
            var planner=Get<AiScoutPlanner>(loop,"scoutPlanner");var knowledge=Get<AiKnowledgeTracker>(loop,"knowledge").Capture();
            planner.Commit(o,planner.Plan(o,knowledge,registry,P,AiProfile.Initial,3),registry);
            CollectionAssert.AreEqual(new[]{25,27},Bind(loop,o,true));
            AiTacticalExecutor.Observe(o,registry,army,P,Bind(loop,o,true));
            Assert.AreEqual(army,registry.ArmyFor(29));Assert.Zero(registry.ArmyFor(27));
            var next=planner.Plan(o,knowledge,registry,P,AiProfile.Initial,3);Assert.AreEqual(27,next.EntityIds.Single());planner.Commit(o,next,registry);
            CollectionAssert.AreEqual(new[]{25,27},Bind(loop,o));
            Assert.AreEqual(2,registry.Capture().Armies.Count(a=>a.Role==AiArmyRole.Scout&&a.Phase!=AiArmyPhase.Disbanded));registry.AssertInvariants();
        }finally{loop.Stop();}
    }
    [Test] public void MissingRouteOrPendingScoutAdmissionDoesNotReserveAnotherFreeActor()
    {
        var o=View(false);var loop=Owner(o);try{
            Assert.True(Get<AiScoutPlanner>(loop,"scoutPlanner").UsefulProductionDemand(o,Get<AiKnowledgeTracker>(loop,"knowledge").Capture(),AiProfile.Initial,3));
            CollectionAssert.IsEmpty(Bind(loop,o,true),"capacity and useful geography alone cannot claim an actor without a route");
            o=View();Get<AiKnowledgeTracker>(loop,"knowledge").Observe(o,AiProfile.Initial);
            var planner=Get<AiScoutPlanner>(loop,"scoutPlanner");var choice=planner.Plan(o,Get<AiKnowledgeTracker>(loop,"knowledge").Capture(),Get<AiArmyRegistry>(loop,"armies"),P,AiProfile.Initial,3);
            Assert.NotNull(Get<PlayableAiScoutLivenessPolicy>(loop,"scout").Admit(o,choice));
            CollectionAssert.IsEmpty(Bind(loop,o,true),"an already pending scout policy cannot admit a second actor");
        }finally{loop.Stop();}
    }
    [TestCase(3,0)][TestCase(4,3)] public void FlankReservationRequiresAnEntireFreeForce(int explorers,int expectedReserved)
    {
        var o=View(explorers:explorers);var loop=Owner(o);try{
            var registry=Get<AiArmyRegistry>(loop,"armies");long army=Army(o,registry);
            var planner=Get<AiScoutPlanner>(loop,"scoutPlanner");var knowledge=Get<AiKnowledgeTracker>(loop,"knowledge").Capture();
            planner.Commit(o,planner.Plan(o,knowledge,registry,P,AiProfile.Initial,3),registry);
            // This is a completed opening, with four fighters already owned by
            // the primary army. Only unclaimed actors can form the second one.
            typeof(AiOpeningExecutor).GetField("phase",F).SetValue(Get<AiOpeningExecutor>(loop,"openingExecutor"),PlayableAiOpeningPhase.Complete);
            var utility=Bind(loop,o);var reserved=(int[])typeof(PlayableAiOwnerLoop).GetMethod("FlankReservations",F).Invoke(loop,new object[]{o});
            Assert.AreEqual(expectedReserved,reserved.Length);Assert.False(reserved.Contains(25));
            Assert.AreEqual(3,AiProfile.Initial.Value("armies.minimumUnits"));
            AiTacticalExecutor.Observe(o,registry,army,P,utility.Concat(reserved));
            if(expectedReserved==0)
                CollectionAssert.AreEqual(new[]{27,29},registry.Capture().Armies.Single(a=>a.Id==army).Members.Where(id=>id==27||id==29));
            else{
                CollectionAssert.AreEqual(new[]{27,29,31},reserved);
                Assert.True(registry.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.FlankPolicy,reserved,out _),"a full reserved force remains admissible under the existing minimum and caps");
            }
            registry.AssertInvariants();
        }finally{loop.Stop();}
    }
}
