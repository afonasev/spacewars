using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class AiProductionRecoveryThreadedTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        [UnityTest,Timeout(510000)] public IEnumerator LostFactoryIsRebuiltBeforeMoreIncomeAndProducesHeavyCore()=>Run(true);
        [UnityTest,Timeout(510000)] public IEnumerator AirDoctrineActuallyProducesItsMissingCore()=>Run(false);
        static IEnumerator Run(bool recovery)
        {
            const string owner="production-owner";const long generation=131;const int seed=41;
            var data=PlayableProfile.Default.CopyData();data.startingCredits=recovery?238:150;
            var p=PlayableProfile.Create(data);var ai=AiProfile.Initial;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);
            // Sustained human opponent: a legitimate early HQ kill must not end
            // the match before recovery and paid production can be observed.
            var reserveWest=new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(-24,24),Array.Empty<TerritorySlot>());
            var reserveEast=new TerritorySite(4,PlayableBuildingKind.Outpost,new NavPoint(24,-24),Array.Empty<TerritorySlot>());
            var kinds=recovery?new[]{PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter}:new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter,PlayableBuildingKind.Hangar};
            var buildings=kinds.Select((k,i)=>new OfflineScenarioBuilding(1,k,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).Concat(new[]{new OfflineScenarioBuilding(2,PlayableBuildingKind.Outpost,3,0,reserveWest.Position),new OfflineScenarioBuilding(2,PlayableBuildingKind.Outpost,4,0,reserveEast.Position)}).ToArray();
            var units=Enumerable.Range(0,4).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-27+i*3,-4)))
                .Concat(Enumerable.Range(0,4).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-9+i*3,-26)))).ToArray();
            // Static human guards keep this a continuing production scenario.
            // All health, weapons and timers remain the ordinary profile values.
            units=units.Concat(new[]{new NavPoint(-28,22),new NavPoint(-24,28),new NavPoint(-20,22),new NavPoint(20,-26),new NavPoint(24,-20),new NavPoint(28,-26)}
                .Select(at=>new OfflineScenarioUnit(2,PlayableEntityKind.Tank,at))).ToArray();
            var config=new OfflineMatchConfiguration(p,"post-opening-production-recovery-v1","flat-production-recovery-v2-sustained-opponent","UnityHostRouteService",seed,new[]{new OfflineParticipant(owner,1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy,reserveWest,reserveEast},Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},spectators:new[]{PlayableRuntime.LocalSpectatorId},scenario:units,scenarioBuildings:buildings);
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/repair-ai-production-recovery/ordinary-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
            Assert.IsNull(TestMatchLogSession.CurrentDirectory);TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            var runtime=new PlayableRuntime(config,generation,startPaused:true,aiProfile:ai);var routes=new UnityHostRouteService();
            bool produced=false,moved=false,factoryPaid=!recovery;int refineryBaseline=recovery?1:2;long firstFactory=0,firstExtraIncome=0;
            try
            {
                // Authored current post-opening state before starting the threaded
                // owner. No fake purchases, receipts, production or navigation.
                var scheduler=typeof(PlayableRuntime).GetField("aiScheduler",F).GetValue(runtime);
                var loop=((IEnumerable)scheduler.GetType().GetField("owners",F).GetValue(scheduler)).Cast<PlayableAiOwnerLoop>().Single();
                var executor=(AiOpeningExecutor)typeof(PlayableAiOwnerLoop).GetField("openingExecutor",F).GetValue(loop);
                void Set(string field,object value)=>typeof(AiOpeningExecutor).GetField(field,F).SetValue(executor,value);
                Set("producerChosen",true);Set("air",false);Set("initialForceFulfilled",true);Set("reinforceChosen",true);Set("reinforce",false);Set("reinforcementFulfilled",true);Set("phase",PlayableAiOpeningPhase.Complete);
                var completed=(System.Collections.Generic.HashSet<string>)typeof(AiOpeningExecutor).GetField("completed",F).GetValue(executor);foreach(string id in new[]{"income","factory","force","pressure"})completed.Add(id);
                var doctrine=(AiDoctrinePolicy)typeof(PlayableAiOwnerLoop).GetField("doctrine",F).GetValue(loop);
                typeof(AiDoctrinePolicy).GetField("chosen",F).SetValue(doctrine,true);typeof(AiDoctrinePolicy).GetField("doctrine",F).SetValue(doctrine,recovery?AiDoctrine.Heavy:AiDoctrine.HawksExplorers);typeof(AiDoctrinePolicy).GetField("mixed",F).SetValue(doctrine,!recovery);
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);runtime.RequestPause(false);float until=Time.realtimeSinceStartup+480;
                var seen=new System.Collections.Generic.Dictionary<int,NavPoint>();var desired=recovery?PlayableEntityKind.Shkval:PlayableEntityKind.Hawk;
                while(runtime.Latest.Tick<3600&&!runtime.IsStopped&&runtime.Latest.Outcome==PlayableMatchOutcome.Playing&&Time.realtimeSinceStartup<until)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var own=frame.Players.Single(v=>v.Owner==PlayableOwner.Player);
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    if(own.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory))
                    {factoryPaid=true;if(firstFactory==0)firstFactory=runtime.Latest.Tick;}
                    if(own.Buildings.Count(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Refinery)>refineryBaseline&&firstExtraIncome==0)firstExtraIncome=runtime.Latest.Tick;
                    if(recovery&&!factoryPaid)Assert.Zero(firstExtraIncome,"Do not spend the lost Factory quote on cheaper income");
                    produced|=own.Entities.Any(u=>u.Owner==PlayableOwner.Player&&u.Kind==desired&&u.Health>0&&!u.Departing);
                    foreach(var u in own.Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai))
                    {if(seen.TryGetValue(u.Id,out var before)&&AiOpponentSelector.Distance(before,u.Position)>PlayableUnitRules.Radius(p,u.Kind))moved=true;else if(!seen.ContainsKey(u.Id))seen.Add(u.Id,u.Position);}
                    if(produced&&moved)break;yield return null;
                }
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{baseline="c2d2a5427cb091da594dd00e85f68c44f5b4bc50",recovery,seed,generation,runtime.Latest.Tick,runtime.Latest.Outcome,produced,moved,factoryPaid,firstFactory,firstExtraIncome,failure=runtime.Latest.Failure},Newtonsoft.Json.Formatting.Indented));TestContext.WriteLine("PRODUCTION_RESULT "+output);
                Assert.True(factoryPaid);Assert.True(produced,"Actual doctrine core must finish ordinary paid production and release");Assert.True(moved,"Ordinary AI command must produce physical movement in the threaded host");
            }
            finally
            {
                runtime.RequestStop();routes.Dispose();runtime.Dispose();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));
                var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",F).GetValue(runtime);authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();TestMatchLogSession.Shutdown();
            }
        }
    }
}
