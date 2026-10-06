using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableAiMidgameStrategyTests
    {
        private readonly PlayableProfile profile=PlayableProfile.Default;
        private PlayableEntitySnapshot Unit(int id,PlayableOwner owner,PlayableEntityKind kind,double x=0,double z=0)
            =>new PlayableEntitySnapshot(id,owner,kind,new NavPoint(x,z),kind==PlayableEntityKind.Explorer?profile.ExplorerHealth:kind==PlayableEntityKind.Shkval?profile.ShkvalHealth:profile.TankHealth,false,0,0,0);
        private PlayableBuildingSnapshot Building(int id,PlayableOwner owner,PlayableBuildingKind kind,double x=0,double z=0)
            =>new PlayableBuildingSnapshot(id,owner,kind,new NavPoint(x,z),kind==PlayableBuildingKind.Factory?profile.FactoryHealth:profile.HeadquartersHealth,1,0,0,default(NavPoint),includePrivateState:owner==PlayableOwner.Player);
        private PlayableAiObservation Observation(int seed,long tick,IEnumerable<PlayableEntitySnapshot> units=null,IEnumerable<PlayableBuildingSnapshot> buildings=null,IEnumerable<TerritorySiteSnapshot> sites=null,long generation=1)
            =>PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,generation,seed,tick+1,tick,RuntimeStatus.Running,false,
                PlayableMatchOutcome.Playing,5000,null,(units??new[]{Unit(1,PlayableOwner.Player,PlayableEntityKind.Explorer)}).ToArray(),
                (buildings??new[]{Building(20,PlayableOwner.Player,PlayableBuildingKind.Headquarters)}).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),
                new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:sites?.ToArray(),population:new PlayablePopulationSnapshot(1,0,40)));
        private static int SeedFor(PlayableAiOpening desired)
        {for(int seed=0;seed<10000;seed++)if(PlayableAiOpeningComposition.Initialize(seed,"player-1").Opening==desired)return seed;throw new Exception("Missing seed");}

        [Test] public void EconomicOpeningSelectsCommittedStrategyFromOwnerFacts()
        {
            var seed=SeedFor(PlayableAiOpening.Safe);var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1");
            var buildings=new[]{Building(20,PlayableOwner.Player,PlayableBuildingKind.Headquarters),Building(21,PlayableOwner.Player,PlayableBuildingKind.Factory),Building(22,PlayableOwner.Player,PlayableBuildingKind.Refinery)};
            var policy=new PlayableAiMidgameStrategyPolicy(profile);var first=policy.Review(Observation(seed,1,buildings:buildings),opening);
            Assert.AreEqual(PlayableAiOpeningPhase.Complete,first.Phase);Assert.False(first.Strategy.HasValue);Assert.AreEqual(1,first.DecisionSequence);
            Assert.AreSame(first,policy.Review(Observation(seed,1,buildings:buildings),opening));
            var restored=new PlayableAiMidgameStrategyPolicy(profile);restored.Restore(policy.Capture());
            var next=restored.Review(Observation(seed,46,buildings:buildings),opening);
            Assert.True(next.Strategy.HasValue);Assert.AreEqual(2,next.DecisionSequence);
        }

        [Test] public void RushRequiresAppliedMissionAndWaitsForOpeningCommitment()
        {
            var seed=SeedFor(PlayableAiOpening.BlindRush);var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1");
            var units=new[]{Unit(1,PlayableOwner.Player,PlayableEntityKind.Explorer),Unit(2,PlayableOwner.Player,PlayableEntityKind.Tank),Unit(50,PlayableOwner.Enemy,PlayableEntityKind.Tank,80,0)};
            var policy=new PlayableAiMidgameStrategyPolicy(profile);
            var missionPolicy=new PlayableAiMissionDefensePolicy(profile);
            var firstObservation=Observation(seed,1,units);
            var rush=missionPolicy.TryPlan(firstObservation,opening);Assert.NotNull(rush);
            Assert.AreEqual(PlayableAiOpeningPhase.Active,policy.Review(firstObservation,opening,missionPolicy.Mission).Phase);
            Assert.AreEqual(PlayableAiOpeningPhase.Active,policy.Review(Observation(seed,46,units),opening,missionPolicy.Mission).Phase);
            missionPolicy.ObserveReceipt(new PlayableAiTraceRecord(firstObservation.Identity,rush.ActionId,1,1,47,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));
            var deployed=missionPolicy.Mission;Assert.True(deployed.Deployed);
            var completed=policy.Review(Observation(seed,91,units),opening,deployed);
            Assert.AreEqual(PlayableAiOpeningPhase.Complete,completed.Phase);Assert.False(completed.Strategy.HasValue);
            Assert.False(policy.Review(Observation(seed,91+45*30-45,units),opening,deployed).Strategy.HasValue);
            Assert.True(policy.Review(Observation(seed,91+45*30,units),opening,deployed).Strategy.HasValue);
            Assert.Throws<ArgumentException>(()=>policy.Review(Observation(seed,91+45*30+1,units),opening,new PlayableAiCombatMission(2,2,2,3,0,2,new NavPoint(10,0),false,true,false,"",new[]{1,2})));
        }

        [Test] public void DeadlineAbortsAndVisibleCenterEmergencyHoldsStrategy()
        {
            var seed=SeedFor(PlayableAiOpening.Safe);var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1");var policy=new PlayableAiMidgameStrategyPolicy(profile);
            var aborted=policy.Review(Observation(seed,105*30),opening);
            Assert.AreEqual(PlayableAiOpeningPhase.Aborted,aborted.Phase);
            var own=new[]{Unit(1,PlayableOwner.Player,PlayableEntityKind.Explorer)};
            var enemy=new[]{Unit(2,PlayableOwner.Enemy,PlayableEntityKind.Tank,1,0)};
            var held=policy.Review(Observation(seed,105*30+45,own.Concat(enemy)),opening);
            Assert.False(held.Strategy.HasValue);
            Assert.True(policy.Review(Observation(seed,105*30+90,own),opening).Strategy.HasValue);
        }

        [Test] public void VisibleEconomyAndForceCanChangeStrategyAfterCommitment()
        {
            var seed=SeedFor(PlayableAiOpening.Safe);var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1");
            var baseBuildings=new[]{Building(20,PlayableOwner.Player,PlayableBuildingKind.Headquarters),Building(21,PlayableOwner.Player,PlayableBuildingKind.Factory),Building(22,PlayableOwner.Player,PlayableBuildingKind.Refinery)};
            var policy=new PlayableAiMidgameStrategyPolicy(profile);policy.Review(Observation(seed,1,buildings:baseBuildings),opening);
            var first=policy.Review(Observation(seed,46,buildings:baseBuildings),opening);
            var battle=Enumerable.Range(1,8).Select(i=>Unit(i,PlayableOwner.Player,PlayableEntityKind.Tank,30+i,0)).ToArray();
            var visibleEnemy=Unit(50,PlayableOwner.Enemy,PlayableEntityKind.Explorer,70,0);
            var next=policy.Review(Observation(seed,91,battle.Concat(new[]{visibleEnemy}),baseBuildings),opening);
            Assert.AreEqual(first.Strategy,next.Strategy,"Source commitment must prevent early switches.");
            var after=policy.Review(Observation(seed,46+360,battle.Concat(new[]{visibleEnemy}),baseBuildings),opening);
            Assert.AreEqual(PlayableAiMidgameStrategy.MassAssault,after.Strategy);
            Assert.AreEqual(after.Strategy,policy.Review(Observation(seed,46+361,battle.Concat(new[]{visibleEnemy}),baseBuildings),opening).Strategy);
            Assert.Throws<ArgumentException>(()=>new PlayableAiMidgameStrategyPolicy(profile).Review(Observation(seed,1),PlayableAiOpeningComposition.Initialize(seed,"foreign-owner")));
        }

        [Test] public void PublicOpportunitiesAndVisibleEconomyCanSelectAllThreeStrategies()
        {
            var own=new[]{Building(20,PlayableOwner.Player,PlayableBuildingKind.Headquarters),Building(21,PlayableOwner.Player,PlayableBuildingKind.Factory),Building(22,PlayableOwner.Player,PlayableBuildingKind.Refinery)};
            var sites=new[]{new TerritorySiteSnapshot(new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(8,8),profile),null,null,0,false,0,false),
                new TerritorySiteSnapshot(new TerritorySite(4,PlayableBuildingKind.Mine,new NavPoint(9,9),profile),null,null,0,false,0,false),
                new TerritorySiteSnapshot(new TerritorySite(5,PlayableBuildingKind.Mine,new NavPoint(10,10),profile),null,null,0,false,0,false)};
            var vulnerable=Enumerable.Range(30,3).Select(i=>Building(i,PlayableOwner.Enemy,PlayableBuildingKind.Refinery,20+i,10)).ToArray();
            var selected=new HashSet<PlayableAiMidgameStrategy>();
            for(int seed=1;seed<5000&&selected.Count<3;seed++)
            {
                var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1");if(opening.Opening!=PlayableAiOpening.Safe)continue;
                foreach(var sample in new[]{Observation(seed,1,buildings:own,sites:sites),Observation(seed,1,buildings:own.Concat(vulnerable))})
                {
                    var policy=new PlayableAiMidgameStrategyPolicy(profile);policy.Review(sample,opening);
                    var result=policy.Review(Observation(seed,46,buildings:sample.Buildings,sites:sample.Sites),opening);
                    if(result.Strategy.HasValue)selected.Add(result.Strategy.Value);
                }
                var battle=Enumerable.Range(1,8).Select(i=>Unit(i,PlayableOwner.Player,PlayableEntityKind.Tank,30+i,0)).Concat(new[]{Unit(50,PlayableOwner.Enemy,PlayableEntityKind.Explorer,70,0)});
                var aggressive=new PlayableAiMidgameStrategyPolicy(profile);aggressive.Review(Observation(seed,1,battle,own),opening);
                var force=aggressive.Review(Observation(seed,46,battle,own),opening);if(force.Strategy.HasValue)selected.Add(force.Strategy.Value);
            }
            CollectionAssert.AreEquivalent(new[]{PlayableAiMidgameStrategy.MapControl,PlayableAiMidgameStrategy.Raids,PlayableAiMidgameStrategy.MassAssault},selected);
        }

        [Test] public void RealDomainProjectionDoesNotOfferHiddenEnemyEconomy()
        {
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var domain=Activator.CreateInstance(type,flags,null,new object[]{profile,1L},null);
            var player=(PlayableSnapshot)type.GetMethod("PlayerSnapshot",flags).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player});
            var enemy=(PlayableSnapshot)type.GetMethod("PlayerSnapshot",flags).Invoke(domain,new object[]{2L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Enemy});
            var observation=PlayableAiObservation.From(player);
            Assert.False(observation.Buildings.Any(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.Refinery));
            Assert.False(observation.Entities.Any(e=>e.Owner==PlayableOwner.Enemy&&enemy.Entities.Any(other=>other.Id==e.Id)));
            Assert.AreEqual(14,PlayableAiObservation.CurrentSchemaVersion);
        }

        [Test] public void ArtilleryDoctrineReceivesCommittedContextAfterItsOwnReviewWindow()
        {
            var seed=SeedFor(PlayableAiOpening.Safe);var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1");
            var buildings=new[]{Building(20,PlayableOwner.Player,PlayableBuildingKind.Headquarters),Building(21,PlayableOwner.Player,PlayableBuildingKind.Factory),Building(22,PlayableOwner.Player,PlayableBuildingKind.Refinery)};
            var strategy=new PlayableAiMidgameStrategyPolicy(profile);var artillery=new PlayableAiArtillerySupportPolicy(profile);
            var before=Observation(seed,1,buildings:buildings);artillery.TryPlan(before,opening);var openingContext=artillery.Capture().Context;
            strategy.Review(before,opening);var selected=strategy.Review(Observation(seed,46,buildings:buildings),opening);Assert.True(selected.Strategy.HasValue);
            artillery.TryPlan(Observation(seed,899,buildings:buildings),opening,selected);Assert.AreEqual(openingContext,artillery.Capture().Context);
            var current=Observation(seed,901,buildings:buildings);var currentStrategy=strategy.Review(current,opening);
            artillery.TryPlan(current,opening,currentStrategy);StringAssert.StartsWith(PlayableAiMidgameStrategyPolicy.Name(currentStrategy.Strategy.Value)+":",artillery.Capture().Context);
            Assert.Throws<ArgumentException>(()=>artillery.TryPlan(current,opening,new PlayableAiMidgameCheckpoint(seed,2,901,901,1,1,PlayableAiOpeningPhase.Complete,PlayableAiMidgameStrategy.Raids,"player-1",opening.SourceIdentity,profile.ProfileId,profile.Revision)));
        }
    }
}
