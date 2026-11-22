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
    public sealed class AiTurretBypassThreadedTests
    {
        [UnityTest,Timeout(150000)]
        public IEnumerator OrdinaryExpertMovesToUncoveredSideAndAttacks()
        {
            const int seed=7;const long generation=407;
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/ai-turret-bypass/ordinary-"+Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(output);
            var riskLog=new object();AiTurretBypassPolicy.RejectedForTests=(tick,reason)=>{lock(riskLog)File.AppendAllText(Path.Combine(output,"risk-rejections.jsonl"),Newtonsoft.Json.JsonConvert.SerializeObject(new{tick,reason})+"\n");};
            var baseline=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text));
            var store=NativeBalanceStore.ForApplication(baseline,Path.Combine(output,"balance.json"));
            var p=store.Active;
            var aiData=store.ActiveAiProfile.CopyData();aiData.id="turret-bypass-ordinary-opening-v1";
            void Set(string key,double value){aiData.fields.Single(f=>f.path==key).value=value;}
            // Isolate a complete available ground group from the independently
            // tested two-unit mixed early opening. Combat and bypass values stay released.
            Set("difficulty.expert.opening.earlyGroundShare",0);
            foreach(var name in new[]{"aggressive","doubleIncome","greedy","allIn","air"})Set("openingsMidgame.opening."+name,name=="doubleIncome"?1:0);
            Set("openingsMidgame.reinforcedMin",8);Set("openingsMidgame.reinforcedMax",8);Set("openingsMidgame.openingExplorerShare",0);
            var ai=new AiProfile(aiData);
            TestContext.WriteLine($"BYPASS_PROFILE {store.SelectedId}@{store.SelectedVersion} gameplay={p.ProfileId}@{p.Revision} ai={ai.Id}@{ai.Revision} hash={ai.Hash}");
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-19,0),new NavPoint(-19,-12),pin:1),new OfflineStart("east",2,new NavPoint(19,0),new NavPoint(19,-12),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);
            var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p,Math.PI);
            var tower=enemy.DefenseSlots(p).OrderBy(s=>AiOpponentSelector.Distance(s.Position,new NavPoint(7,-6))).First();
            var buildings=new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Factory,home.Id,home.Slots[0].Id,home.Slots[0].Position,home.Slots[0].Heading),new OfflineScenarioBuilding(2,PlayableBuildingKind.SentryTurret,enemy.Id,tower.Id,tower.Position,tower.Heading)}
                .Concat(home.Slots.Skip(1).Take(2).Select(slot=>new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,home.Id,slot.Id,slot.Position,slot.Heading))).ToArray();
            // A compact already available force still receives all its commands from
            // the ordinary owner planner, arbiter, delivery and threaded host.
            var units=Enumerable.Range(0,8).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(9+(i%2)*3,8+(i/2)*2))).ToArray();
            var config=new OfflineMatchConfiguration(p,"turret-bypass-ordinary-v1","flat-public-v1","UnityHostRouteService",seed,
                new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("player-2",2,2,OfflineControl.Human)},starts,new[]{home,enemy},PlayableMap.StaticObstacles(p),new double[,]{{0,38},{38,0}},scenario:units,scenarioBuildings:buildings,spectators:new[]{PlayableRuntime.LocalSpectatorId});
            TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            var runtime=new PlayableRuntime(config,generation,aiDifficulty:AiDifficulty.Expert,aiProfile:ai);var routes=new UnityHostRouteService();
            bool originalStopped=false;
            void Stop(PlayableRuntime r,UnityHostRouteService service)
            {
                r.RequestStop();service?.Dispose();Assert.True(System.Threading.SpinWait.SpinUntil(()=>r.IsStopped,5000));
                var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(r);
                authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();
            }
            bool commanded=false,moved=false,arrived=false,damaged=false;long action=0;int target=0;double health=-1;NavPoint first=default;int actor=0;long moveTick=0;
            try
            {
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                float deadline=Time.realtimeSinceStartup+120;
                while(runtime.Latest.Tick<2400&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);var view=runtime.ParticipantView("player-1");
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    foreach(var u in view.Entities.Where(u=>u.Owner==view.Owner&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move))
                    {
                        var stamp=u.OrderStamp;
                        var enemyTarget=view.Buildings.FirstOrDefault(b=>view.IsHostile(b.Owner)&&b.Health>0&&!TerritoryRules.ArmedDefense(b.Kind)&&AiOpponentSelector.Distance(u.CurrentOrder.Destination,b.Position)<=p.TankRange);
                        if(enemyTarget==null)continue;
                        if(!commanded){commanded=true;actor=u.Id;first=u.Position;action=stamp.ActionId;moveTick=view.Tick;target=enemyTarget.Id;health=enemyTarget.Health;}
                    }
                    var selected=view.Entities.FirstOrDefault(u=>u.Id==actor);
                    if(commanded&&selected?.OrderStamp?.ActionId==action&&selected.OrderStamp.Kind==PlayableCommandKind.Move&&view.Tick>moveTick&&AiOpponentSelector.Distance(first,selected.Position)>p.TankCollisionRadius)moved=true;
                    if(moved&&selected!=null&&selected.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&selected.CurrentOrder.TargetId==target)
                    {
                        var defense=view.Buildings.FirstOrDefault(b=>b.Kind==PlayableBuildingKind.SentryTurret&&view.IsHostile(b.Owner));
                        arrived=defense==null||!AiPositionDevelopmentPlanner.Covers(PlayableAiObservation.From(view),p,defense.Position,defense.Kind,selected.Position,false,defense.Id,selected.Id);
                    }
                    if(arrived&&view.Buildings.Any(b=>b.Id==target&&b.Health<health))damaged=true;
                    if(damaged)break;yield return null;
                }
                runtime.RequestPause(true);var capture=runtime.RequestCaptureBytes();float until=Time.realtimeSinceStartup+10;
                while(!capture.IsCompleted&&Time.realtimeSinceStartup<until){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.True(capture.IsCompleted);var bytes=capture.GetAwaiter().GetResult();File.WriteAllBytes(Path.Combine(output,"current.bin"),bytes);
                var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
                var checkpoint=authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="player-1");
                bool receipted=checkpoint.Records.Any(r=>r.ActionId==action&&AiArmyPlanner.IsArmyPolicy(r.Policy)&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.Generation==generation);
                Stop(runtime,routes);originalStopped=true;
                byte[] roundtrip=null;PlayableRuntime restored=null;
                try
                {
                    restored=PlayableRuntime.RestoreBytesForTests(bytes,config,ai,r=>roundtrip=r.CaptureBeforeStartForTests());
                    CollectionAssert.AreEqual(bytes,roundtrip,"Current world, shared battle ledger and passage state restore exactly before continuation");
                }
                finally{if(restored!=null)Stop(restored,null);}
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source=Environment.GetEnvironmentVariable("AI_BYPASS_SOURCE_SHA"),seed,generation,store.SelectedId,store.SelectedVersion,p.ProfileId,p.Revision,ai.Id,ai.Hash,commanded,moved,arrived,damaged,receipted,action,target,actor,moveTick,tick=runtime.Latest.Tick},Newtonsoft.Json.Formatting.Indented));
                TestContext.WriteLine("BYPASS_RESULT "+output);
                Assert.True(commanded,"Ordinary group passage Move");Assert.True(moved,"Applied passage command produces physical motion");Assert.True(receipted,"The same moving action has an ordinary army Applied receipt");Assert.True(arrived,"Group attacks from the uncovered side");Assert.True(damaged,"Attack after passage damages its hostile building");
            }
            finally{if(!originalStopped)Stop(runtime,routes);TestMatchLogSession.Shutdown();AiTurretBypassPolicy.RejectedForTests=null;}
        }
    }
}
