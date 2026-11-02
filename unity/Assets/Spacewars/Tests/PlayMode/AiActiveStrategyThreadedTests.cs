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
    public sealed class AiActiveStrategyThreadedTests
    {
        [UnityTest,Timeout(150000)] public IEnumerator OrdinaryArmyAppliesPressureMovesDamagesAndRestoresCurrentRuntime()
        {
            const int seed=7;const long generation=107;
            var p=PlayableProfile.Default;var ai=NativeBalanceFields.InitialAiOpeningProfile();
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery};
            var buildings=kinds.Select((kind,i)=>new OfflineScenarioBuilding(1,kind,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).ToArray();
            var units=Enumerable.Range(0,8).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-14+(i%4)*3,-2+(i/4)*3))).ToArray();
            var config=new OfflineMatchConfiguration(p,"ai-active-strategy-ordinary-v1","flat-public-active-v1","UnityHostRouteService",seed,new[]{new OfflineParticipant("active-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy},PlayableMap.StaticObstacles(p),new double[,]{{0,100},{100,0}},scenario:units,scenarioBuildings:buildings,spectators:new[]{PlayableRuntime.LocalSpectatorId});
            var source=Environment.GetEnvironmentVariable("AI_ACTIVE_SOURCE_SHA");Assert.IsNotEmpty(source);
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/ai-active-strategy/ordinary-"+source+"-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
            Assert.IsNull(TestMatchLogSession.CurrentDirectory);TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            var runtime=new PlayableRuntime(config,generation,participantDifficulties:new[]{AiDifficulty.Veteran,AiDifficulty.Fighter},aiProfile:ai);var routes=new UnityHostRouteService();
            PlayableRuntime restored=null;PlayableAuthorityTick restoredAuthority=null;UnityHostRouteService restoreRoutes=null;bool originalStopped=false;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void StopOriginal()
            {
                if(originalStopped)return;
                var worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",flags).GetValue(routes);
                runtime.RequestStop();routes.Dispose();runtime.Dispose();
                Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));
                var a=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);
                a.Domain.Navigation.ReleaseCommittedHostRoot();a.Domain.DestroyOwnedNavigation();originalStopped=true;
            }
            var motion=new Dictionary<int,(NavPoint point,long tick,long action)>();var moved=new HashSet<int>();bool damage=false;var hostileHealth=new Dictionary<int,int>();
            try
            {
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);float deadline=Time.realtimeSinceStartup+120;
                while(runtime.Latest.Tick<3600&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);var f=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var own=f.Players.Single(v=>v.Owner==PlayableOwner.Player);
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    foreach(var b in f.Overview.Buildings.Where(b=>b.Owner==PlayableOwner.Enemy&&b.Health>0))
                    {if(hostileHealth.TryGetValue(b.Id,out var previous)&&b.Health<previous&&moved.Count>0)damage=true;hostileHealth[b.Id]=b.Health;}
                    foreach(var u in own.Entities.Where(u=>u.Owner==own.Owner&&u.Health>0&&u.CurrentOrder!=null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&u.OrderStamp.Source==PlayableAiOpeningComposition.SourceIdentity&&(u.OrderStamp.Kind==PlayableCommandKind.Attack||u.OrderStamp.Kind==PlayableCommandKind.AttackMove)))
                    {
                        if(!motion.TryGetValue(u.Id,out var start)||start.action!=u.OrderStamp.ActionId)motion[u.Id]=(u.Position,own.Tick,u.OrderStamp.ActionId);
                        else if(own.Tick>start.tick&&AiOpponentSelector.Distance(start.point,u.Position)>PlayableUnitRules.Radius(p,u.Kind))moved.Add(u.Id);
                    }
                    if(moved.Count>0&&damage)break;yield return null;
                }
                var capture=runtime.RequestCaptureBytes();while(!capture.IsCompleted&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.True(capture.IsCompleted,"Fixed wall deadline includes capture");Assert.False(capture.IsFaulted,capture.Exception?.ToString());File.WriteAllBytes(Path.Combine(output,"current.bin"),capture.Result);
                long tick;using(var envelope=PlayableWorldState.Decode(capture.Result)){tick=envelope.Tick;Assert.Greater(envelope.RuntimeTransport.Length,0);}
                StopOriginal();byte[] roundtrip=null;PlayableAiOwnerCheckpoint checkpoint=null;Exception restoreError=null;
                restoreRoutes=new UnityHostRouteService();restored=PlayableRuntime.RestoreBytesForTests(capture.Result,config,ai,r=>
                {
                    try{roundtrip=r.CaptureBeforeStartForTests();restoredAuthority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);checkpoint=restoredAuthority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="active-owner");}
                    catch(Exception e){restoreError=e;r.RequestStop();}
                });
                Assert.IsNull(restoreError);bool exact=capture.Result.SequenceEqual(roundtrip);
                var applied=checkpoint.Records.Where(r=>AiArmyPlanner.IsArmyPolicy(r.Policy)&&(r.Kind==PlayableCommandKind.Attack||r.Kind==PlayableCommandKind.AttackMove)&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.Generation==generation&&r.OwnerId=="active-owner").ToArray();
                bool receiptMotion=applied.Any(r=>motion.Any(m=>m.Value.action==r.ActionId&&moved.Contains(m.Key)));
                float restoreDeadline=Time.realtimeSinceStartup+10;
                while(restored.Latest.Tick<tick+30&&!restored.IsStopped&&Time.realtimeSinceStartup<restoreDeadline){restoreRoutes.Service(restored,p.NavigationRequestsPerFrame);yield return null;}
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,seed,generation,tick,p.ProfileId,p.Revision,ai.Id,ai.Hash,moved=moved.ToArray(),damage,receiptMotion,exact,continued=restored.Latest.Tick>=tick+30,checkpoint.ArmyMission,records=checkpoint.Records},Newtonsoft.Json.Formatting.Indented));
                TestContext.WriteLine("ACTIVE_STRATEGY_RESULT "+output);
                Assert.Greater(moved.Count,0,"Physical offensive movement");Assert.True(receiptMotion,"Same ordinary army Applied command produced observed physical movement");Assert.True(damage,"Army pressure damages a hostile building");Assert.True(exact,"Exact current full runtime including transport before restart");Assert.IsNull(restored.Latest.Failure);Assert.GreaterOrEqual(restored.Latest.Tick,tick+30,"Actual restored threaded continuation");
            }
            finally
            {
                if(!originalStopped)StopOriginal();
                if(restored!=null){restored.RequestStop();restored.Dispose();Assert.True(System.Threading.SpinWait.SpinUntil(()=>restored.IsStopped,5000));}
                routes.Dispose();restoreRoutes?.Dispose();restoredAuthority?.Domain.Navigation.ReleaseCommittedHostRoot();restoredAuthority?.Domain.DestroyOwnedNavigation();TestMatchLogSession.Shutdown();
            }
        }
    }
}
