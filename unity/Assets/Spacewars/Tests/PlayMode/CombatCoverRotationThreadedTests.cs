using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class CombatCoverRotationThreadedTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        [UnityTest,Timeout(180000)] public IEnumerator EarlyTankRotation()=>Run("early-tank",0,450);
        [UnityTest,Timeout(180000)] public IEnumerator BasicAttackControl()=>Run("control",-1,900);
        [UnityTest,Timeout(180000)] public IEnumerator MicroWest()=>Run("micro-west",0,900);
        [UnityTest,Timeout(180000)] public IEnumerator MicroEast()=>Run("micro-east",1,900);
        [UnityTest,Timeout(180000)] public IEnumerator HawkRotation()=>Run("hawk-under-pressure",0,600,PlayableEntityKind.Hawk);
        [UnityTest,Timeout(180000)] public IEnumerator HealthyShkvalCover()=>Run("shkval-cover",0,450,PlayableEntityKind.Shkval);
        [UnityTest,Timeout(180000)] public IEnumerator FocusedControl()=>Run("focused-control",-1,900,focused:true);
        [UnityTest,Timeout(180000)] public IEnumerator FocusedMicroWest()=>Run("focused-west",0,900,focused:true);
        [UnityTest,Timeout(180000)] public IEnumerator FocusedMicroEast()=>Run("focused-east",1,900,focused:true);
        [UnityTest,Timeout(180000)] public IEnumerator TenTankControl()=>Run("ten-control",-1,900,count:10);
        [UnityTest,Timeout(180000)] public IEnumerator TenTankMicroWest()=>Run("ten-west",0,900,count:10);
        [UnityTest,Timeout(180000)] public IEnumerator TenTankMicroEast()=>Run("ten-east",1,900,count:10);
        [UnityTest,Timeout(180000)] public IEnumerator ScreenedTenTankControl()=>Run("screened-ten-control",-1,900,focused:true,count:10);
        [UnityTest,Timeout(180000)] public IEnumerator ScreenedTenTankMicroWest()=>Run("screened-ten-west",0,900,focused:true,count:10);
        [UnityTest,Timeout(180000)] public IEnumerator ScreenedTenTankMicroEast()=>Run("screened-ten-east",1,900,focused:true,count:10);
        static object FrameState(PlayableDomain domain,PlayableEntitySnapshot actor)
        {
            var units=(IDictionary)typeof(PlayableDomain).GetField("units",F).GetValue(domain);
            var u=units[actor.Id];var t=u.GetType();
            object Read(string field)=>t.GetField(field,F).GetValue(u);
            return new{actor.Id,Owner=(int)actor.Owner,actor.Position,actor.Health,actor.TargetId,actor.Moving,actor.HullHeading,actor.TurretHeading,
                Weapon=new{Reload=Read("Reload"),Stopped=Read("StoppedSeconds"),Burst=Read("BurstRemaining"),BurstDelay=Read("BurstDelay")},
                Order=((PlayableTacticalOrderSnapshot)Read("CurrentOrder"))?.Kind.ToString(),Action=((PlayableOrderStamp)Read("LastOrder"))?.ActionId};
        }
        static void AssertWithdrawalReturnsToFire(JObject[] facts,JObject[] withdraws,bool requireTransfer)
        {
            var shots=facts.Where(e=>(string)e["Event"]=="shot").ToLookup(e=>(int)e["OwnerId"]);
            var frames=facts.Where(e=>(string)e["Event"]=="frame")
                .SelectMany(f=>f["Units"].Select(u=>(Tick:(long)f["Tick"],Unit:u)))
                .ToLookup(f=>(int)f.Unit["Id"]);
            bool returned=false,transferred=false;
            foreach(var withdrawal in withdraws)
            {
                int actor=withdrawal["Ids"].Values<int>().Single();
                long start=(long)withdrawal["Tick"],action=(long)withdrawal["ActionId"];
                var origin=withdrawal["Health"].Single()["Position"];
                var move=frames[actor].FirstOrDefault(f=>f.Tick>start&&(long?)f.Unit["Action"]==action&&
                    (string)f.Unit["Order"]=="Move"&&(bool)f.Unit["Moving"]&&!JToken.DeepEquals(origin,f.Unit["Position"]));
                if(move.Unit==null)continue;
                var stop=frames[actor].FirstOrDefault(f=>f.Tick>move.Tick&&(long?)f.Unit["Action"]==action&&!(bool)f.Unit["Moving"]);
                if(stop.Unit==null)continue;
                var shot=shots[actor].FirstOrDefault(s=>(long)s["Tick"]>=stop.Tick&&
                    frames[actor].Any(f=>f.Tick==(long)s["Tick"]&&!(bool)f.Unit["Moving"]));
                if(shot==null)continue;
                returned=true;
                if(!requireTransfer)break;

                var team=frames.Where(row=>row.First().Unit["Owner"].Equals(move.Unit["Owner"])).Select(row=>row.Key).ToHashSet();
                var attackers=shots.Where(s=>!team.Contains(s.Key)&&
                    (int?)s.LastOrDefault(e=>(long)e["Tick"]<move.Tick)?["TargetId"]==actor);
                transferred|=attackers.Any(s=>frames[s.Key].Any(f=>f.Tick>=move.Tick&&f.Tick<=(long)shot["Tick"]&&
                    (int)f.Unit["TargetId"]!=actor&&team.Contains((int)f.Unit["TargetId"])));
            }
            Assert.True(returned,"A paid withdrawal must move, stop and fire again while stationary");
            if(requireTransfer)Assert.True(transferred,"An attacker must transfer its target to a teammate before the withdrawn unit's return shot");
        }

        static IEnumerator Run(string name,int enabled,int horizon,PlayableEntityKind kind=PlayableEntityKind.Tank,bool focused=false,int count=4)
        {
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/combat-cover-rotation/bouts",name));Directory.CreateDirectory(root);
            var normal=(PlayableProfile)typeof(PlayableBootstrap).GetMethod("LoadNormalProfile",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            var store=NativeBalanceStore.ForApplication(normal,Path.Combine(root,"balance.json"));
            store.Select(NativeBalanceStore.DefaultId,12);
            Assert.AreEqual("spacewars-default",store.SelectedId);Assert.AreEqual(12,store.SelectedVersion);
            var aiData=store.ActiveAiProfile.CopyData();
            foreach(var f in aiData.fields)if(f.path.EndsWith(".micro.airFocusEnabled")||f.path.EndsWith(".micro.focusEnabled")||f.path.EndsWith(".micro.repairEnabled")||f.path.EndsWith(".micro.chaseEnabled"))f.value=0;
            var ai=new AiProfile(aiData);var setup=new ProvingGroundSetup(store.Active);
            for(int side=0;side<2;side++)for(int i=0;i<(kind==PlayableEntityKind.Hawk&&side==0?2:count);i++){
                var actorKind=kind==PlayableEntityKind.Shkval&&i>0?PlayableEntityKind.Tank:kind;
                var at=kind==PlayableEntityKind.Shkval?new NavPoint((side==0?-1:1)*(i==0?2.5:4.5),i==0?0:(i==1?-3:i==2?3:6)):new NavPoint(side==0?-3.5:3.5,(i-(count-1)/2d)*2);
                if(focused)at=new NavPoint((side==0?-1:1)*(i==0?3:3.5),i==0?0:count>4?(i%2==1?-(i+1):i):i==1?-2:i==2?2:4);
                if(kind==PlayableEntityKind.Hawk){double spacing=2*store.Active.HawkRadius+store.Active.HawkSeparation+2*store.Active.Navigation.ArrivalTolerance;at=new NavPoint(side==0?-3.5:3.5,(i-(side==0?.5:1.5))*spacing);if(side==1)actorKind=PlayableEntityKind.Explorer;}
                Assert.IsNull(setup.PlaceUnit(side,actorKind,at));
            }
            var c=setup.Configuration();const int seed=149;
            var config=new OfflineMatchConfiguration(c.Profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,seed,
                c.Roster.Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,OfflineControl.Ai)).ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),new double[,]{{0,80},{80,0}},
                scenario:c.ScenarioUnits.ToArray(),terrain:c.MapIdentity,scenarioBuildings:c.ScenarioBuildings.ToArray(),provingGround:c.ProvingGround);
            var events=new ConcurrentQueue<object>();var launched=new HashSet<int>();var health=new Dictionary<int,double>();
            PlayableRuntime runtime=null;PlayableAuthorityTick authority=null;
            using var routes=new UnityHostRouteService();
            try
            {
                PlayableAiOwnerLoop.IsolatedRotationAppliedForTests=(action,phase,tick)=>events.Enqueue(new{Event="micro",Tick=tick,Phase=phase.ToString(),action.PlayerId,action.ActionId,Ids=action.CopyEntityIds(),Health=authority?.Domain.Snapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed).Entities.Where(u=>action.EntityIds.Contains(u.Id)).Select(u=>new{u.Id,u.Health,u.MaxHealth,u.Kind,u.Position}).ToArray(),action.Target});
                PlayableAiOwnerLoop.IsolatedRotationForTests=true;
                PlayableAiOwnerLoop.IsolatedRotationOwnerForTests=enabled<0?null:config.Roster[enabled].Id;
                TestMatchLogSession.StartDefault(Path.Combine(root,"TestLogs"));
                runtime=new PlayableRuntime(config,1,startPaused:true,participantDifficulties:new[]{AiDifficulty.Veteran,AiDifficulty.Veteran},spectator:true,aiProfile:ai);
                authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",F).GetValue(runtime);
                double deadline=Time.realtimeSinceStartupAsDouble+100;
                while((!runtime.IsPrepared||!runtime.Latest.Paused)&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline){routes.Service(runtime,c.Profile.NavigationRequestsPerFrame);yield return null;}
                Assert.True(runtime.IsPrepared&&runtime.Latest.Paused,runtime.Latest.Failure);
                var initial=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;
                var scheduler=(AiAuthorityScheduler)typeof(PlayableRuntime).GetField("aiScheduler",F).GetValue(runtime);
                foreach(var u in initial.Entities)health[u.Id]=u.ExactHealth??u.Health;
                long firstCombatTick=0;
                authority.Domain.CombatStepForQa=()=>
                {
                    if(firstCombatTick==0)System.Threading.Interlocked.CompareExchange(ref firstCombatTick,authority.Domain.Tick,0);
                    var v=authority.Domain.Snapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed);
                    if(count==10&&enabled>=0)
                    {
                        var motor=authority.Domain.Navigation.ObserveRotationMotorForTests(
                            initial.Entities.First(u=>(int)u.Owner==enabled).Id,
                            initial.Entities.Where(u=>(int)u.Owner==enabled).Skip(1).First().Id);
                        if(motor!=null)events.Enqueue(new{Event="motor",v.Tick,Motor=motor});
                    }
                    foreach(var p in v.Projectiles)if(launched.Add(p.Id))events.Enqueue(new{Event="shot",v.Tick,p.Id,p.OwnerId,p.TargetId});
                    foreach(var u in v.Entities){double hp=u.ExactHealth??u.Health;if(health.TryGetValue(u.Id,out var old)&&hp<old)events.Enqueue(new{Event="damage",v.Tick,u.Id,Owner=(int)u.Owner,Before=old,Health=hp});health[u.Id]=hp;}
                    foreach(var dead in health.Keys.Where(id=>!v.Entities.Any(u=>u.Id==id)).ToArray()){events.Enqueue(new{Event="death",v.Tick,Id=dead,Before=health[dead]});health.Remove(dead);}
                    events.Enqueue(new{Event="frame",v.Tick,Units=v.Entities.Select(u=>FrameState(authority.Domain,u)).ToArray()});
                };
                // Use the Proving Ground's existing route-settlement barrier
                // with the two ordinary group commands used by this comparison.
                var openingActors=initial.Entities.Where(u=>u.Kind!=PlayableEntityKind.Hawk).Select(u=>u.Id).ToArray();
                var openings=config.Roster.Select((owner,side)=>new PlayableCommand(1,side+1,owner.Id,
                    PlayableCommandKind.AttackMove,initial.Entities.Where(u=>(int)u.Owner==side).Select(u=>u.Id).ToArray(),
                    new NavPoint(side==0?10:-10,0),origin:PlayableOrderOrigin.Ai,
                    source:PlayableAiOpeningComposition.SourceIdentity,jobId:side+1,actionId:side+1)).ToArray();
                foreach(var owner in scheduler.Owners)owner.CombatAttention.Charge(0,ai,AiDifficulty.Veteran,false);
                PlayableCommandReceipt[] OpeningReceipts()=>runtime.ReceiptsAfter(0)
                    .Where(r=>openings.Any(c=>c.PlayerId==r.OwnerId&&c.Sequence==r.Sequence)&&r.Status==PlayableCommandStatus.Applied).ToArray();
                authority.Domain.AwaitRotationOpeningForTests(openingActors,()=>OpeningReceipts().Length==openings.Length);
                runtime.RequestPause(false);
                while(runtime.Latest.Paused&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline){routes.Service(runtime,c.Profile.NavigationRequestsPerFrame);yield return null;}
                Assert.False(runtime.Latest.Paused,"Opening commands require the acknowledged unpaused state");
                foreach(var command in openings)
                {
                    var submit=runtime.TrySubmitAi(command);
                    Assert.True(submit.Accepted,submit.Status.ToString());
                }
                while(OpeningReceipts().Length<2&&!runtime.IsStopped&&runtime.Latest.Failure==null&&Time.realtimeSinceStartupAsDouble<deadline){routes.Service(runtime,c.Profile.NavigationRequestsPerFrame);yield return null;}
                var openingReceipts=OpeningReceipts();
                Assert.AreEqual(2,openingReceipts.Length,JsonConvert.SerializeObject(runtime.ReceiptsAfter(0)));
                Assert.AreEqual(2,openingReceipts.Select(r=>r.OwnerId).Distinct().Count());
                Assert.True(openingReceipts.All(r=>r.AppliedTick==0),"Both opening orders must apply before the first gameplay tick");

                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                while(!runtime.IsStopped&&(runtime.Latest.Tick<System.Threading.Interlocked.Read(ref firstCombatTick)+horizon-1||System.Threading.Interlocked.Read(ref firstCombatTick)==0)&&runtime.Latest.Failure==null&&Time.realtimeSinceStartupAsDouble<deadline){routes.Service(runtime,c.Profile.NavigationRequestsPerFrame);yield return null;}
                var final=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;
                runtime.RequestStop();double stop=Time.realtimeSinceStartupAsDouble+10;while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<stop)yield return null;
                Assert.True(runtime.IsStopped);
                File.WriteAllLines(Path.Combine(root,"combat.jsonl"),events.Select(JsonConvert.SerializeObject));
                var applied=scheduler.Owners.SelectMany(o=>o.Checkpoint.Records).Where(x=>x.Status==PlayableAiDeliveryStatus.Applied).ToArray();
                File.WriteAllText(Path.Combine(root,"result.json"),JsonConvert.SerializeObject(new{name,seed,enabled,firstCombatTick,openingReceipts,Profile=store.ActiveIdentity,Ai=ai.CopyData(),ai.Hash,Initial=initial.Entities,Final=final.Entities,final.Tick,runtime.Latest.Failure,Applied=applied,Records=scheduler.Owners.SelectMany(o=>o.Checkpoint.Records).ToArray()},Formatting.Indented));
                Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);Assert.Greater(firstCombatTick,0);Assert.GreaterOrEqual(final.Tick-firstCombatTick+1,horizon);
                Assert.Greater(launched.Count,0);
                if(count==10&&enabled>=0)
                    Assert.Greater(final.Entities.Where(u=>(int)u.Owner==enabled).Sum(u=>u.Health),
                        final.Entities.Where(u=>(int)u.Owner!=enabled).Sum(u=>u.Health),"Rotation must improve force exchange against one initial AttackMove");
                var facts=events.Select(JObject.FromObject).ToArray();
                var withdraws=facts.Where(e=>(string)e["Event"]=="micro"&&(string)e["Phase"]=="Withdraw").ToArray();
                if(kind==PlayableEntityKind.Shkval)Assert.True(withdraws.Any(e=>e["Health"].Any(u=>(int)u["Kind"]==2&&(double)u["Health"]>=(double)u["MaxHealth"]*.6)),"Shkval must actually seek cover before the wounded threshold");
                if(enabled>=0)
                {
                    Assert.True(applied.Any(x=>x.OwnerId==config.Roster[enabled].Id&&x.Policy==AiCombatMicroPolicy.Policy),"Witness needs an actually applied paid micro command");
                    AssertWithdrawalReturnsToFire(facts,withdraws,count==10);
                }
            }
            finally
            {
                if(runtime!=null){runtime.RequestStop();bool stopped=System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000);runtime.Dispose();if(stopped&&authority!=null){authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();}}
                TestMatchLogSession.Shutdown();PlayableAiOwnerLoop.IsolatedRotationForTests=false;PlayableAiOwnerLoop.IsolatedRotationOwnerForTests=null;PlayableAiOwnerLoop.IsolatedRotationAppliedForTests=null;
            }
        }
    }
}
