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
    public sealed class AiRepairEconomyThreadedTests
    {
        [UnityTest,Timeout(100000)] public IEnumerator NearbyMineCanFundTheOpeningThroughTheThreadedHost()
        {
            const string owner="early-mine-owner";
            const int mineId=3;
            var p=PlayableProfile.Default;
            var ai=NativeBalanceFields.InitialAiOpeningProfile();
            int seed=Enumerable.Range(0,10000).First(n=>PlayableAiOpeningComposition.Initialize(n,owner,aiProfile:ai).Opening==PlayableAiOpening.Safe);
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),
                new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p),
                new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p),
                new TerritorySite(mineId,PlayableBuildingKind.Mine,new NavPoint(-9,-21),Array.Empty<TerritorySlot>())};
            var config=new OfflineMatchConfiguration(p,"early-mine-threaded-v1","flat-nearby-mine-v1","UnityHostRouteService",seed,
                new[]{new OfflineParticipant(owner,1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},
                starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},spectators:new[]{PlayableRuntime.LocalSpectatorId});
            var runtime=new PlayableRuntime(config,126,aiDifficulty:AiDifficulty.Veteran,aiProfile:ai);
            var routes=new UnityHostRouteService();
            long move=-1,motion=-1,mine=-1,producer=-1,income=-1;
            int explorer=0;
            double incomeAtReadiness=0;
            NavPoint origin=default;
            void Stop()
            {
                if(runtime==null)return;
                runtime.RequestStop();routes?.Dispose();runtime.Dispose();
                Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));
                var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(runtime);
                authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();
                runtime=null;routes=null;
            }
            var decisions=new System.Collections.Concurrent.ConcurrentQueue<string>();
            PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=owner;
            PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=r=>
            {
                if(r.Observation.Tick>180||r.ExpansionIntent==null)return;
                r.Rejections.TryGetValue(r.ExpansionIntent.Id,out string rejection);
                decisions.Enqueue($"t={r.Observation.Tick} mineBid={r.ExpansionIntent.Id} selected={string.Join("|",r.Selected)} rejected={rejection}");
            };
            PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=owner;
            PlayableAiOwnerLoop.PreselectionReadbackForTests=r=>
            {
                if(r.Observation.Tick>450)return;
                var explorerView=r.Observation.Entities.FirstOrDefault(e=>e.Owner==r.Observation.Owner&&e.Kind==PlayableEntityKind.Explorer);
                decisions.Enqueue($"t={r.Observation.Tick} credits={r.Observation.Credits} protected={r.ProtectedFunds} site={r.Observation.Sites.Any(x=>x.Site.Id==mineId)} route={r.Observation.RouteProofs.Any(x=>x.TargetId==mineId)} " +
                    $"explorer={explorerView?.Id} moving={explorerView?.Moving} order={explorerView?.CurrentOrder?.Kind} proposal={r.ActualExpansionProposal?.Action?.Kind}");
            };
            try
            {
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                float deadline=Time.realtimeSinceStartup+75;
                while(runtime.Latest.Tick<2100&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);
                    var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);
                    var own=frame.Players.Single(v=>v.Owner==PlayableOwner.Player);
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    foreach(var unit in own.Entities.Where(u=>u.Owner==own.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai))
                    {
                        if(move<0&&unit.CurrentOrder!=null&&AiOpponentSelector.Distance(unit.CurrentOrder.Destination,sites[2].Position)<=p.MineCaptureRadius)
                        {move=own.Tick;explorer=unit.Id;origin=unit.Position;}
                        else if(motion<0&&unit.Id==explorer&&AiOpponentSelector.Distance(unit.Position,origin)>p.ExplorerCollisionRadius)
                            motion=own.Tick;
                    }
                    if(mine<0&&own.Buildings.Any(b=>b.Owner==own.Owner&&b.SiteId==mineId&&b.Kind==PlayableBuildingKind.Mine&&b.Phase==ConstructionPhase.Ready))
                    {mine=own.Tick;incomeAtReadiness=own.SettledIncome;}
                    if(producer<0&&own.Buildings.Any(b=>b.Owner==own.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready))producer=own.Tick;
                    if(income<0&&mine>=0&&own.SettledIncome>incomeAtReadiness&&
                        own.IncomePerSecond>=(p.HeadquartersIncomePerPeriod+p.MineIncomePerPeriod)/p.IncomePeriodSeconds)income=own.Tick;
                    if(income>=0&&producer>=0)break;
                    yield return null;
                }
                foreach(var decision in decisions)TestContext.WriteLine(decision);
                TestContext.WriteLine($"seed={seed} move={move} motion={motion} mine={mine} income={income} producer={producer} last={runtime.Latest.Tick}");
                Assert.GreaterOrEqual(move,0,"An AI Explorer receives a real order toward the known mine.");
                Assert.Greater(motion,move,"The ordered Explorer advances through the threaded host.");
                Assert.Greater(mine,motion,"The mine is captured and construction completes.");
                Assert.GreaterOrEqual(income,mine,"Ready mine income appears on the owner economy.");
                Assert.Greater(producer,mine,"The mine contributes to the opening milestone before the first producer is ready.");

                var capture=runtime.RequestCaptureBytes();
                while(!capture.IsCompleted&&Time.realtimeSinceStartup<deadline)
                {routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.True(capture.IsCompletedSuccessfully,capture.Exception?.ToString());
                long capturedTick;
                using(var envelope=PlayableWorldState.Decode(capture.Result))
                {
                    Assert.AreEqual(seed,envelope.Seed);Assert.Greater(envelope.RuntimeTransport.Length,0);
                    capturedTick=envelope.Tick;
                }
                Stop();
                byte[] roundtrip=null;
                runtime=PlayableRuntime.RestoreBytesForTests(capture.Result,config,ai,r=>roundtrip=r.CaptureBeforeStartForTests());
                routes=new UnityHostRouteService();
                Assert.True(capture.Result.SequenceEqual(roundtrip),"Current opening economy and authority state round-trip exactly.");
                const int continuationTicks=30;
                while(runtime.Latest.Tick<capturedTick+continuationTicks&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
                {routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                Assert.GreaterOrEqual(runtime.Latest.Tick,capturedTick+continuationTicks,"The restored threaded host continues with the new income.");
                TestContext.WriteLine($"exact opening save at {capturedTick}, continued to {runtime.Latest.Tick}");
            }
            finally
            {
                PlayableAiOwnerLoop.PreselectionReadbackForTests=null;PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=null;
                PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=null;
                Stop();
            }
        }

        [UnityTest,Timeout(150000)] public IEnumerator FundedDevelopedOwnerCapturesMineAndDevelopsLastHomeSlot()
        {
            // Authored funded precondition: all productive prerequisites exist,
            // with enough bank for income purchases and ordinary unit cycles.
            var data=PlayableProfile.Default.CopyData();data.startingCredits=2000;
            var p=PlayableProfile.Create(data);var ai=NativeBalanceFields.InitialAiOpeningProfile();const string owner="growth-owner";const long generation=121;
            int seed=Enumerable.Range(0,10000).First(n=>PlayableAiOpeningComposition.Initialize(n,owner,aiProfile:ai).Opening==PlayableAiOpening.Safe);
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);var mine=new TerritorySite(3,PlayableBuildingKind.Mine,new NavPoint(-4,-20),Array.Empty<TerritorySlot>());
            var front=new TerritorySite(4,PlayableBuildingKind.Outpost,new NavPoint(-20,15),p);
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter};
            var buildings=kinds.Select((k,i)=>new OfflineScenarioBuilding(1,k,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).Concat(new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,4,0,front.Position),new OfflineScenarioBuilding(1,PlayableBuildingKind.Hangar,4,front.Slots[0].Id,front.Slots[0].Position,front.Slots[0].Heading),new OfflineScenarioBuilding(1,PlayableBuildingKind.Factory,4,front.Slots[1].Id,front.Slots[1].Position,front.Slots[1].Heading)}).ToArray();
            var units=Enumerable.Range(0,4).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-27+i%4*3,-4+i/4*3))).Concat(Enumerable.Range(0,3).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-9+i%3*3,-26+i/3*3)))).ToArray();
            var config=new OfflineMatchConfiguration(p,"funded-developed-ai-development-v1","flat-funded-growth-v1","UnityHostRouteService",seed,new[]{new OfflineParticipant(owner,1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy,mine,front},Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},spectators:new[]{PlayableRuntime.LocalSpectatorId},scenario:units,scenarioBuildings:buildings);
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/repair-ai-repair-economy/ordinary-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);TestContext.WriteLine("FUNDED_AI_PRECONDITION seed="+seed+" profile="+ai.Hash);
            Assert.IsNull(TestMatchLogSession.CurrentDirectory);TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            object diagnostic=null;
            PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=owner;
            PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=r=>diagnostic=new{r.Observation.Tick,r.FinanceSite,r.FinanceReady,r.Threatened,r.ProtectedFunds,r.ScalingReason,r.ExpansionState,holds=r.Ledger.Reserved.Select(e=>new{e.Purpose,e.Amount,e.Action.Kind,e.Action.ResearchKind}).ToArray(),mines=r.Observation.Sites.Where(t=>t.Site.Kind==PlayableBuildingKind.Mine).Select(t=>new{t.Site.Id,t.Owner,t.Claimant,t.CenterId,t.Ready}).ToArray(),mineRoutes=r.Observation.RouteProofs.Count(t=>t.TargetId==3&&t.Kind==PlayableRouteTargetKind.PublicObjective)};
            var runtime=new PlayableRuntime(config,generation,aiDifficulty:AiDifficulty.Veteran,aiProfile:ai);var routes=new UnityHostRouteService();bool captured=false,developed=false,moved=false;var seen=new System.Collections.Generic.Dictionary<int,NavPoint>();
            try
            {
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);float until=Time.realtimeSinceStartup+120;
                while(runtime.Latest.Tick<3600&&!runtime.IsStopped&&Time.realtimeSinceStartup<until)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);var f=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var own=f.Players.Single(v=>v.Owner==PlayableOwner.Player);
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    captured|=own.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.SiteId==3&&b.Kind==PlayableBuildingKind.Mine&&b.Phase==ConstructionPhase.Ready);
                    developed|=own.Buildings.Count(b=>b.Owner==PlayableOwner.Player&&b.SiteId==1&&b.Kind==PlayableBuildingKind.Refinery&&b.Phase==ConstructionPhase.Ready)>=3;
                    foreach(var u in own.Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Explorer&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai))
                    {if(seen.TryGetValue(u.Id,out var before)&&AiOpponentSelector.Distance(before,u.Position)>PlayableUnitRules.Radius(p,u.Kind))moved=true;else if(!seen.ContainsKey(u.Id))seen.Add(u.Id,u.Position);}
                    if(captured&&developed&&moved)break;yield return null;
                }
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source=Environment.GetEnvironmentVariable("AI_GROWTH_SOURCE_SHA"),seed,generation,runtime.Latest.Tick,captured,developed,moved,failure=runtime.Latest.Failure,diagnostic},Newtonsoft.Json.Formatting.Indented));TestContext.WriteLine("FUNDED_AI_RESULT "+output);
                Assert.True(moved,"Actual AI-issued Explorer movement through the threaded host");Assert.True(captured,"Ordinary owner pays, captures and completes a ready Mine");Assert.True(developed,"Funded developed base uses its final useful slot for ready income");
            }
            finally
            {
                PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=null;
                runtime.RequestStop();routes.Dispose();runtime.Dispose();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));
                var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(runtime);authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();TestMatchLogSession.Shutdown();
            }
        }
    }
}
