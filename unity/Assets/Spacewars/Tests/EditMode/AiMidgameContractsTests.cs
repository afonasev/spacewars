using System;
using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiMidgameContractsTests
    {
        [Test]
        public void NoOutpostAllowsBothBranchesAndOutpostIncreasesProductionPreference()
        {
            const string owner="midgame-owner";
            var ai=AiProfile.Initial;
            var opening=Enumerable.Range(0,200).Select(seed=>new AiOpeningExecutor(
                PlayableAiOpeningComposition.Initialize(seed,owner,aiProfile:ai),71)).ToArray();
            int twoWithout=opening.Count(x=>x.PrefersTwoProducers(ai,false));
            int twoWith=opening.Count(x=>x.PrefersTwoProducers(ai,true));
            Assert.Greater(twoWithout,0);
            Assert.Less(twoWithout,opening.Length);
            Assert.Greater(twoWith,twoWithout);
        }

        [TestCase(FactoryReleaseRules.Opening,null,false)]
        [TestCase(FactoryReleaseRules.Driving,null,false)]
        [TestCase(FactoryReleaseRules.Driving,PlayableDomain.ProducerExitObstructed,true)]
        [TestCase(FactoryReleaseRules.Closed,PlayableDomain.ProducerExitObstructed,true)]
        [TestCase(FactoryReleaseRules.Closed,PlayableDomain.ProducerPopulationExceeded,false)]
        public void PaidFactoryHeadDistinguishesNormalReleaseFromObstruction(int phase,string reason,bool blocked)
        {
            var order=new PlayableProductionOrderSnapshot(1,PlayableEntityKind.Tank,150,3,15,0,true,blockedReason:reason);
            var factory=new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Factory,default,200,1,1,1,default,
                launchPhase:phase,orders:new[]{order});
            Assert.AreEqual(blocked,AiProductionDemand.Blocked(factory));
            Assert.AreEqual(!blocked&&reason!=PlayableDomain.ProducerPopulationExceeded,AiProductionDemand.Productive(factory));
        }

        [TestCase(PlayableCommandKind.Move)]
        [TestCase(PlayableCommandKind.AttackMove)]
        public void MossHighlandFactoryBlocksOriginalDestinationButProofFindsObservedApproach(PlayableCommandKind commandKind)
        {
            const int seed=-1313779786;
            const long generation=71;
            var map=new MossHighlandMap();
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());
            while(root!=null&&!File.Exists(Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;
            Assert.NotNull(root);
            var source=JObject.Parse(File.ReadAllText(Path.Combine(root.FullName,"unity/Tests/Fixtures/MossHighlandMidgameWitness.json")));
            var p=PlayableProfile.Create(source["Profile"].ToObject<PlayableProfileData>(),map);
            var roster=new[]{
                new OfflineParticipant("player-1",1,2,OfflineControl.Human),
                new OfflineParticipant("foundry-2",2,1,OfflineControl.Human),
                new OfflineParticipant("foundry-3",3,2,OfflineControl.Human),
                new OfflineParticipant("foundry-4",4,1,OfflineControl.Human)
            };
            var basis=map.Configuration(p,seed,roster);
            var target=basis.Sites.Single(s=>s.Id==2);
            var defender=roster[basis.Assignments.ToList().IndexOf(basis.Starts.ToList().FindIndex(s=>s.SiteId==target.Id))];
            var slot=target.Slots.Single(s=>s.Id==2);
            var config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,seed,roster,
                basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),
                Costs(basis),scenario:new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(24,-36))},terrain:map.Id,
                scenarioBuildings:new[]{new OfflineScenarioBuilding(defender.LogicalPlayer,PlayableBuildingKind.Factory,target.Id,slot.Id,slot.Position,slot.Heading)});
            var domain=new PlayableDomain(p,generation,config);
            try
            {
                var metrics=new PlayableRuntimeMetrics(0,0,0,0,0);
                var view=domain.PlayerSnapshot(0,RuntimeStatus.Running,false,metrics,null,seed,PlayableOwner.Enemy);
                var tank=view.Entities.Single(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Tank);
                var objective=view.PublicScoutObjectives.Single(t=>t.SiteId==target.Id);
                var original=objective.Approach;
                Assert.That(original.X,Is.EqualTo(31.84670163809229).Within(1e-9));
                Assert.That(original.Z,Is.EqualTo(-37.02541328279936).Within(1e-9));
                Assert.True(view.Buildings.Any(b=>b.Kind==PlayableBuildingKind.Factory&&b.SiteId==target.Id),"The blocker is known to this owner.");
                double radius=PlayableUnitRules.Radius(p,tank.Kind);
                Assert.True(domain.Navigation.Crowd.Locate(original,0,out _),"Original point has a support surface.");
                Assert.False(domain.Geometry.IsFree(original,radius),"The recipient footprint intersects the ready factory.");
                PlayableCommand Command(NavPoint point,long sequence)=>new PlayableCommand(generation,sequence,view.OwnerId,commandKind,
                    new[]{tank.Id},point,siteId:target.Id,origin:PlayableOrderOrigin.Ai,
                    source:PlayableAiOpeningComposition.SourceIdentity,jobId:sequence,actionId:sequence);
                Assert.AreEqual(PlayableCommandStatus.InvalidTarget,domain.Apply(Command(original,1),out var rejection));
                Assert.AreEqual("Invalid destination.",rejection);
                var proof=domain.PlayerSnapshotWithRoutes(0,RuntimeStatus.Running,false,metrics,null,seed,view.Owner,
                    new[]{new PlayableRouteRequest(tank.Id,PlayableRouteTargetKind.PublicObjective,target.Id,generation,domain.Tick,tank.Position,radius,original)})
                    .RouteProofs.Single();
                Assert.AreEqual(original,proof.Target);
                Assert.AreNotEqual(original,proof.Goal);
                Assert.AreEqual(proof.Goal,proof.Path.Last());
                Assert.True(domain.Navigation.Crowd.Locate(proof.Goal,radius,out _));
                Assert.True(domain.Geometry.IsFree(proof.Goal,radius));
                Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(Command(proof.Goal,2),out var result),result);
            }
            finally{domain.DestroyOwnedNavigation();}
        }

        private static double[,] Costs(OfflineMatchConfiguration config)
        {
            var costs=new double[config.Starts.Count,config.Starts.Count];
            for(int from=0;from<config.Starts.Count;from++)
            for(int to=0;to<config.Starts.Count;to++)costs[from,to]=config.RouteCost(from,to);
            return costs;
        }
    }
}
