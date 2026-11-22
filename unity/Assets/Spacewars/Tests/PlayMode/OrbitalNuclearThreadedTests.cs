using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class OrbitalNuclearThreadedTests
    {
        [Test]
        public void StockProfileAndSavedNuclearParametersAreReadable()
        {
            var directory=Path.Combine(Path.GetTempPath(),"spacewars-nuclear-lab-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path=Path.Combine(directory,"balance.json");
                var baseline=PlayableProfile.Default;
                var store=new NativeBalanceStore(baseline,path);
                store.Select(NativeBalanceStore.CorridorExperimentId,NativeBalanceStore.CorridorExperimentVersion);
                var terms=store.Selected.CopyData();
                TestContext.WriteLine($"{store.SelectedId} v{store.SelectedVersion}");
                Assert.AreEqual(85.5,terms.orbitalNuclearDamage);
                Assert.AreEqual(60,terms.hawkHealth);
                Assert.AreEqual(50,terms.defensiveSystemsLevel3BonusPercent);
                Assert.AreEqual(4.5,terms.hawkHealth*(1+terms.defensiveSystemsLevel3BonusPercent/100)-terms.orbitalNuclearDamage);
                var labels=typeof(NativeBalanceStore).Assembly.GetType("Spacewars.Presentation.NativeBalanceLabels");
                var entity=labels.GetMethod("Entity",BindingFlags.Static|BindingFlags.NonPublic);
                foreach(var field in OrbitalNuclearRules.Fields)
                    Assert.AreEqual("Ядерный удар",entity.Invoke(null,new object[]{field}));
                terms.orbitalNuclearDamage+=.1;
                terms.orbitalNuclearFlightSeconds=.6;
                store.Save("Проверка ядерного удара",terms);
                var reopened=new NativeBalanceStore(baseline,path);
                reopened.Select(store.SelectedId,store.SelectedVersion);
                Assert.AreEqual(85.6,reopened.Selected.CopyData().orbitalNuclearDamage,1e-9);
                Assert.AreEqual(.6,reopened.Selected.CopyData().orbitalNuclearFlightSeconds);
            }
            finally { Directory.Delete(directory,true); }
        }

        [UnityTest,Timeout(65000)]
        public IEnumerator VeteranReceivesEvacuationAndStartsMovingOnTheThreadedHost()
        {
            var setup=new ProvingGroundSetup(PlayableProfile.Default);
            var center=new NavPoint(12,6);
            Assert.IsNull(setup.PlaceUnit(1,PlayableEntityKind.Tank,center));
            var basis=setup.Configuration();
            var roster=new[]
            {
                new OfflineParticipant("player-1",1,1,OfflineControl.Human),
                new OfflineParticipant("enemy-1",2,2,OfflineControl.Ai)
            };
            var config=new OfflineMatchConfiguration(basis.Profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,
                basis.Seed,roster,basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),
                new double[,]{{0,80},{80,0}},scenario:basis.ScenarioUnits.ToArray(),terrain:basis.Profile.AuthoredMap.Id,
                scenarioBuildings:basis.ScenarioBuildings.ToArray(),provingGround:setup);
            var runtime=new PlayableRuntime(config,122,startPaused:true,aiDifficulty:AiDifficulty.Veteran);
            var routes=new UnityHostRouteService();
            try
            {
                float deadline=Time.realtimeSinceStartup+45;
                while(!runtime.IsPrepared&&Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);
                    Assert.IsNull(runtime.Latest?.Failure);
                    yield return null;
                }
                Assert.True(runtime.IsPrepared,runtime.Latest?.Failure);
                var id=runtime.ParticipantView("enemy-1").Entities.Single(unit=>unit.Position.Equals(center)).Id;
                runtime.RequestPause(false);
                Assert.True(runtime.TrySubmit(new PlayableCommand(122,1,"player-1",PlayableCommandKind.UseOrbital,null,
                    center,targetId:OrbitalAbilities.NuclearStrike)).Accepted);
                bool moved=false;
                long orderTick=-1, launchTick=-1;
                while(Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);
                    var frame=runtime.ParticipantView("enemy-1");
                    Assert.IsNull(frame.Failure);
                    var alert=frame.MatchEvents.LastOrDefault(fact=>fact.Kind==MatchEventKind.NuclearLaunch);
                    if(alert!=null)launchTick=alert.Tick;
                    var unit=frame.Entities.FirstOrDefault(candidate=>candidate.Id==id);
                    if(unit?.OrderStamp?.Source=="orbital-nuclear-evacuation")
                    {
                        if(orderTick<0)orderTick=unit.OrderStamp.Tick;
                        if(!unit.Position.Equals(center)){moved=true;break;}
                    }
                    if(launchTick>=0&&frame.Tick>launchTick+110)break;
                    yield return null;
                }
                Assert.GreaterOrEqual(launchTick,0);
                Assert.That(orderTick-launchTick,Is.InRange(30,33));
                Assert.True(moved,"Evacuation must physically move the actor.");
            }
            finally { runtime.RequestStop(); routes.Dispose(); runtime.Dispose(); }
        }

        [UnityTest,Timeout(65000)]
        public IEnumerator ThreadedProvingGroundPublishesWarningDescentAndOneHit()
        {
            var setup=new ProvingGroundSetup(PlayableProfile.Default);
            var point=new NavPoint(-12,6);
            Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,point));
            var runtime=new PlayableRuntime(setup.Configuration(),121,startPaused:true);
            var routes=new UnityHostRouteService();
            try
            {
                float deadline=Time.realtimeSinceStartup+45;
                while(!runtime.IsPrepared&&Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);
                    Assert.IsNull(runtime.Latest?.Failure);
                    yield return null;
                }
                Assert.True(runtime.IsPrepared,runtime.Latest?.Failure);
                var unit=runtime.ParticipantView("player-1").Entities.Single(entity=>entity.Position.Equals(point));
                Assert.AreEqual(100,unit.ExactHealth);
                runtime.RequestPause(false);
                Assert.True(runtime.TrySubmit(new PlayableCommand(121,1,"player-1",PlayableCommandKind.UseOrbital,null,
                    point,targetId:OrbitalAbilities.NuclearStrike)).Accepted);
                bool sawWarning=false,sawDescent=false,sawImpact=false;
                while(Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);
                    var frame=runtime.ParticipantView("player-1");
                    Assert.IsNull(frame.Failure);
                    var strike=frame.Orbital.NuclearStrikes.SingleOrDefault();
                    if(strike?.Phase==OrbitalNuclearPhase.Warning)sawWarning=true;
                    if(strike?.Phase==OrbitalNuclearPhase.Descent)sawDescent=true;
                    if(strike?.Phase==OrbitalNuclearPhase.Impact)
                    {
                        Assert.AreEqual(14.5,frame.Entities.Single(entity=>entity.Id==unit.Id).ExactHealth);
                        sawImpact=true;
                    }
                    if(sawImpact&&strike==null)break;
                    yield return null;
                }
                Assert.True(sawWarning);
                Assert.True(sawDescent);
                Assert.True(sawImpact);
                Assert.AreEqual(PlayableCommandStatus.Applied,runtime.ReceiptsAfter(0).Single(receipt=>receipt.Sequence==1).Status);
                Assert.AreEqual(14.5,runtime.ParticipantView("player-1").Entities.Single(entity=>entity.Id==unit.Id).ExactHealth);
            }
            finally { runtime.RequestStop(); routes.Dispose(); runtime.Dispose(); }
        }
    }
}
