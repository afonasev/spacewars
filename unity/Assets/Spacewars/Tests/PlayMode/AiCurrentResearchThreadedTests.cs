using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class AiCurrentResearchThreadedTests
    {
        [UnityTest,Timeout(150000)] public IEnumerator OrdinaryScienceProducesUnupgradedShkvalWithAppliedMotionAndExactRestore()
        {
            const int seed=7;const long generation=115;var p=PlayableProfile.Default;var ai=NativeBalanceFields.InitialAiOpeningProfile();
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);
            var reserve=new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(25,-24),Array.Empty<TerritorySlot>());
            var rear=new TerritorySite(4,PlayableBuildingKind.Outpost,new NavPoint(-25,24),Array.Empty<TerritorySlot>());
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery};
            // A developed legal home retains its repair support after science loss.
            // This separates the production unlock from first-repair investment.
            // Normal enemy reserve centers keep the match alive through paid output;
            // no health, credits, unit timing or AI profile values are changed.
            var repair=home.DefenseSlots(p).First();
            var buildings=kinds.Select((k,i)=>new OfflineScenarioBuilding(1,k,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).Concat(new[]{
                new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,1,repair.Id,repair.Position,repair.Heading),
                new OfflineScenarioBuilding(2,PlayableBuildingKind.Outpost,3,0,reserve.Position),new OfflineScenarioBuilding(2,PlayableBuildingKind.Outpost,4,0,rear.Position)}).ToArray();
            var units=Enumerable.Range(0,8).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-14+(i%4)*3,-2+(i/4)*3),upgraded:true))
                .Concat(Enumerable.Range(0,3).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-14+i*3,5),upgraded:true))).ToArray();
            var config=new OfflineMatchConfiguration(p,"ai-current-research-developed-home-v1","flat-public-active-v1","UnityHostRouteService",seed,new[]{new OfflineParticipant("active-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy,reserve,rear},PlayableMap.StaticObstacles(p),new double[,]{{0,100},{100,0}},scenario:units,scenarioBuildings:buildings,spectators:new[]{PlayableRuntime.LocalSpectatorId});
            string source=Environment.GetEnvironmentVariable("AI_CURRENT_RESEARCH_SOURCE_SHA");Assert.IsNotEmpty(source);
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/ai-current-research/ordinary-"+source+"-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"source-binding.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,seed,generation,config.SourceIdentity,config.MapIdentity,p.ProfileId,p.Revision,ai.Id,ai.Hash,horizon=3600,wallSeconds=120}));
            Assert.IsNull(TestMatchLogSession.CurrentDirectory);TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            var runtime=new PlayableRuntime(config,generation,participantDifficulties:new[]{AiDifficulty.Veteran,AiDifficulty.Fighter},aiProfile:ai);var routes=new UnityHostRouteService();
            PlayableRuntime restored=null;PlayableAuthorityTick restoredAuthority=null;UnityHostRouteService restoreRoutes=null;bool originalStopped=false;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void StopOriginal()
            {
                if(originalStopped)return;var worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",flags).GetValue(routes);
                runtime.RequestStop();routes.Dispose();runtime.Dispose();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));
                var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();originalStopped=true;
            }
            int maxCenters=0,born=0;bool readyScience=false,guidance=false,physical=false;long scienceTick=0,birthTick=0,motionTick=0,motionAction=0;var motions=new Dictionary<int,(NavPoint point,long tick,long action)>();
            try
            {
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);float deadline=Time.realtimeSinceStartup+120;
                while(runtime.Latest.Tick<3600&&!runtime.IsStopped&&runtime.Latest.Outcome==PlayableMatchOutcome.Playing&&Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var own=frame.Players.Single(v=>v.Owner==PlayableOwner.Player);
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    var centers=own.Buildings.Where(b=>b.Owner==own.Owner&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Health>0).ToArray();maxCenters=Math.Max(maxCenters,centers.Length);
                    if(!readyScience&&centers.Any(b=>b.Phase==ConstructionPhase.Ready)){readyScience=true;scienceTick=own.Tick;}
                    guidance|=own.OwnerResearch.Any(r=>r.Kind==PlayableResearchKind.ShkvalGuidance);
                    foreach(var u in own.Entities.Where(u=>u.Owner==own.Owner&&u.Kind==PlayableEntityKind.Shkval&&u.Health>0))
                    {
                        Assert.False(u.Upgraded,"Guidance is not the production unlock");if(born==0){born=u.Id;birthTick=own.Tick;}
                        if(u.Id!=born||u.CurrentOrder==null||u.OrderStamp?.Origin!=PlayableOrderOrigin.Ai||u.CurrentOrder.CommandSequence!=u.OrderStamp.Sequence)continue;
                        if(!motions.TryGetValue(u.Id,out var previous)||previous.action!=u.OrderStamp.ActionId)motions[u.Id]=(u.Position,own.Tick,u.OrderStamp.ActionId);
                        else if(own.Tick>previous.tick&&AiOpponentSelector.Distance(previous.point,u.Position)>PlayableUnitRules.Radius(p,u.Kind))
                        {physical=true;motionTick=own.Tick;motionAction=u.OrderStamp.ActionId;}
                    }
                    if(physical)break;yield return null;
                }
                File.WriteAllText(Path.Combine(output,"gameplay.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,runtime.Latest.Tick,runtime.Latest.Outcome,readyScience,scienceTick,maxCenters,guidance,born,birthTick,physical,motionTick,motionAction}));
                Assert.False(runtime.IsStopped);Assert.AreEqual(PlayableMatchOutcome.Playing,runtime.Latest.Outcome,"Ordinary match stays alive through production and capture");var capture=runtime.RequestCaptureBytes();while(!capture.IsCompleted&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.True(capture.IsCompleted,"Same gameplay deadline includes capture");Assert.False(capture.IsFaulted,capture.Exception?.ToString());File.WriteAllBytes(Path.Combine(output,"current.bin"),capture.Result);
                long tick;using(var envelope=PlayableWorldState.Decode(capture.Result)){tick=envelope.Tick;Assert.Greater(envelope.RuntimeTransport.Length,0);}
                StopOriginal();byte[] roundtrip=null;Exception restoreError=null;PlayableAiOwnerCheckpoint checkpoint=null;AiBudgetEntry sciencePayment=null,shkvalPayment=null;
                restoreRoutes=new UnityHostRouteService();restored=PlayableRuntime.RestoreBytesForTests(capture.Result,config,ai,r=>
                {
                    try
                    {
                        roundtrip=r.CaptureBeforeStartForTests();restoredAuthority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);checkpoint=restoredAuthority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="active-owner");
                        var scheduler=typeof(PlayableAuthorityTick).GetField("scheduler",flags).GetValue(restoredAuthority);
                        var owner=((IEnumerable)scheduler.GetType().GetField("owners",flags).GetValue(scheduler)).Cast<object>().Single();var ledger=(AiBudgetLedger)owner.GetType().GetField("budget",flags).GetValue(owner);
                        sciencePayment=ledger.Capture().Paid.FirstOrDefault(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.ScientificCenter);
                        shkvalPayment=ledger.Capture().Paid.FirstOrDefault(e=>e.Action.Kind==PlayableCommandKind.QueueShkval);
                    }
                    catch(Exception e){restoreError=e;r.RequestStop();}
                });
                Assert.IsNull(restoreError);bool exact=capture.Result.SequenceEqual(roundtrip);
                var paid=checkpoint.Records.Where(r=>r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.Generation==generation&&r.OwnerId=="active-owner").ToArray();
                bool paidScience=sciencePayment?.Amount==p.ScienceCreditCost&&paid.Any(r=>r.Kind==PlayableCommandKind.BuildAt&&r.ReceiptIdentity?.Id==sciencePayment.Receipt?.Id),
                    paidShkval=shkvalPayment?.Amount==p.ShkvalCreditCost&&paid.Any(r=>r.Kind==PlayableCommandKind.QueueShkval&&r.ReceiptIdentity?.Id==shkvalPayment.Receipt?.Id),appliedMotion=paid.Any(r=>r.ActionId==motionAction);
                float restoreDeadline=Time.realtimeSinceStartup+10;while(restored.Latest.Tick<tick+30&&!restored.IsStopped&&Time.realtimeSinceStartup<restoreDeadline){restoreRoutes.Service(restored,p.NavigationRequestsPerFrame);yield return null;}
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,seed,generation,tick,readyScience,scienceTick,maxCenters,guidance,born,birthTick,physical,motionTick,motionAction,paidScience,paidShkval,appliedMotion,sciencePayment,shkvalPayment,exact,continued=restored.Latest.Tick>=tick+30,checkpoint.Records},Newtonsoft.Json.Formatting.Indented));
                Assert.True(readyScience&&paidScience,"Ordinary paid construction produced a ready center");Assert.AreEqual(1,maxCenters,"One center is sufficient");Assert.Greater(born,0);Assert.True(paidShkval&&physical&&appliedMotion,"Paid unupgraded Shkval physically follows an applied AI command");Assert.False(guidance);Assert.True(exact);Assert.IsNull(restored.Latest.Failure);Assert.GreaterOrEqual(restored.Latest.Tick,tick+30);
            }
            finally
            {
                if(!originalStopped)StopOriginal();if(restored!=null){restored.RequestStop();restored.Dispose();Assert.True(System.Threading.SpinWait.SpinUntil(()=>restored.IsStopped,5000));}
                routes.Dispose();restoreRoutes?.Dispose();restoredAuthority?.Domain.Navigation.ReleaseCommittedHostRoot();restoredAuthority?.Domain.DestroyOwnedNavigation();TestMatchLogSession.Shutdown();
            }
        }
    }
}
