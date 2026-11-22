using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class AiTerritorialDeploymentThreadedTests
    {
        private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
        private const int Seed=-1879992285;
        private const long Generation=117;

        [UnityTest, Timeout(165000)]
        public IEnumerator LostCoreReleasesAndReassignedTanksReachDistinctPositions()
        {
            var data=PlayableProfile.Default.CopyData();
            data.arenaHalfExtent=64;
            data.groundRouteBackend=1;
            var p=PlayableProfile.Create(data);
            var ai=AiProfile.Initial;
            var config=Configuration(p);
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/territory-recovery/focused-"+Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(output);
            using var logs=new TestMatchLogSession(output,new TestMatchLoggingProfile(),new{Fixture=TestContext.CurrentContext.Test.Name});
            PlayableRuntime.JournalFactory=c=>logs.CreateMatch(c);
            PlayableRuntime runtime=null;
            UnityHostRouteService routes=null;
            try
            {
                double deadline=Time.realtimeSinceStartupAsDouble+135;
                runtime=new PlayableRuntime(config,Generation,startPaused:true,aiProfile:ai);
                routes=new UnityHostRouteService();
                runtime.RequestPause(false);
                while(runtime.Latest.Tick<2&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)
                {routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.IsNull(runtime.Latest.Failure);
                Assert.GreaterOrEqual(runtime.Latest.Tick,2);
                runtime.RequestPause(true);
                var initial=runtime.RequestCaptureBytes();
                while(!initial.IsCompleted&&Time.realtimeSinceStartupAsDouble<deadline)
                {routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.True(initial.IsCompletedSuccessfully,initial.Exception?.ToString());
                Stop(runtime,routes);
                long lostArmy=0;
                int[] surviving=Array.Empty<int>();
                runtime=PlayableRuntime.RestoreBytesForTests(initial.Result,config,ai,r=>
                    SeedCoreLoss(r, out lostArmy, out surviving));
                routes=new UnityHostRouteService();
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                runtime.RequestPause(false);

                long regrouped=-1,released=-1;
                var motion=new Dictionary<(long Army,int Unit,long Action),(NavPoint Origin,NavPoint Rally)>();
                var arrivals=new Dictionary<string,(long Tick,int Unit,long Action,NavPoint Point)>();
                while(runtime.Latest.Tick<2400&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);
                    var view=runtime.Latest;
                    var checkpoint=runtime.AiCheckpoint;
                    Assert.IsNull(view.Failure);
                    Assert.IsNull(routes.LastProjectionFailure);
                    if(checkpoint?.Armies!=null)
                    {
                        if(checkpoint.ArmyMission.ArmyId==lostArmy&&checkpoint.ArmyMission.Reason==AiReinforcementPolicy.RegroupReason)
                            regrouped=regrouped<0?view.Tick:regrouped;
                        if(regrouped>=0&&released<0&&checkpoint.Armies.Armies.Any(g=>g.Id==lostArmy&&g.Phase==AiArmyPhase.Disbanded))
                            released=view.Tick;
                        if(released>=0)
                        foreach(var group in checkpoint.Armies.Armies.Where(a=>a.Id!=lostArmy&&a.Phase!=AiArmyPhase.Disbanded&&AiRepairUsePolicy.GuardMission(a)&&a.Rally.HasValue))
                        foreach(var unit in view.Entities.Where(u=>group.Members.Contains(u.Id)&&surviving.Contains(u.Id)&&u.Health>0))
                        {
                            var stamp=unit.OrderStamp;
                            if(stamp==null||stamp.Kind!=PlayableCommandKind.AttackMove||stamp.Origin!=PlayableOrderOrigin.Ai||stamp.Tick<group.CreatedTick)continue;
                            bool applied=checkpoint.Records.Any(r=>r.Policy==group.TacticalOwner&&r.Status==PlayableAiDeliveryStatus.Applied&&
                                r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence);
                            if(!applied)continue;
                            var key=(group.Id,unit.Id,stamp.ActionId);
                            if(unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove&&unit.CurrentOrder.Destination.Equals(group.Rally.Value)&&
                                !motion.ContainsKey(key))motion[key]=(unit.Position,group.Rally.Value);
                            if(unit.NavigationOutcome==NavigationOutcome.Arrived&&!unit.Moving&&motion.TryGetValue(key,out var start)&&
                                AiOpponentSelector.Distance(unit.Position,start.Origin)>PlayableUnitRules.Radius(p,unit.Kind)&&
                                AiOpponentSelector.Distance(unit.Position,start.Rally)<=ai.Value("safeReinforcements.coreRadius"))
                                arrivals[group.Objective]=(view.Tick,unit.Id,stamp.ActionId,unit.Position);
                        }
                    }
                    if(arrivals.Count>=2)break;
                    yield return null;
                }
                Assert.IsNull(runtime.Latest.Failure);
                File.WriteAllText(Path.Combine(output,"readback.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new {
                    Seed,Generation,lostArmy,regrouped,released,tick=runtime.Latest.Tick,
                    moves=motion.Count,arrivals=arrivals.Select(a=>new{a.Key,a.Value.Tick,a.Value.Unit,a.Value.Action,a.Value.Point})
                },Newtonsoft.Json.Formatting.Indented));
                TestContext.WriteLine("TERRITORIAL_DEPLOYMENT "+output);
                Assert.GreaterOrEqual(regrouped,0,"Observed real core-loss regroup.");
                Assert.Greater(released,regrouped,"Old mission retired after regroup.");
                Assert.GreaterOrEqual(arrivals.Count,2,"Recovered tanks moved and arrived under applied ordinary orders at two distinct positions.");

                var capture=runtime.RequestCaptureBytes();
                while(!capture.IsCompleted&&Time.realtimeSinceStartupAsDouble<deadline)
                {routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.True(capture.IsCompletedSuccessfully,capture.Exception?.ToString());
                using(var envelope=PlayableWorldState.Decode(capture.Result))
                {Assert.AreEqual(Seed,envelope.Seed);Assert.Greater(envelope.RuntimeTransport.Length,0);}
                File.WriteAllBytes(Path.Combine(output,"current.bin"),capture.Result);
                Stop(runtime,routes);
                byte[] roundtrip=null;
                runtime=PlayableRuntime.RestoreBytesForTests(capture.Result,config,ai,r=>roundtrip=r.CaptureBeforeStartForTests());
                routes=new UnityHostRouteService();
                Assert.True(capture.Result.SequenceEqual(roundtrip),"Exact current checkpoint, including owner AI and route state.");
                long continuation=runtime.Latest.Tick+30;
                while(runtime.Latest.Tick<continuation&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)
                {routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.IsNull(runtime.Latest.Failure);
                Assert.IsNull(routes.LastProjectionFailure);
                Assert.GreaterOrEqual(runtime.Latest.Tick,continuation,"Restored threaded host continues play.");
            }
            finally
            {
                if(runtime!=null&&!runtime.IsStopped)Stop(runtime,routes);
                PlayableRuntime.JournalFactory=null;
            }
        }

        [UnityTest, Timeout(85000)]
        public IEnumerator QuietTerritorialGuardMarchesWithAnAppliedAttackMove()
        {
            var data=PlayableProfile.Default.CopyData();
            data.arenaHalfExtent=64;
            data.groundRouteBackend=1;
            var profile=PlayableProfile.Create(data);
            var ai=AiProfile.Initial;
            var config=Configuration(profile,quietGuard:true);
            PlayableRuntime runtime=null;
            UnityHostRouteService routes=null;
            try
            {
                double deadline=Time.realtimeSinceStartupAsDouble+75;
                runtime=new PlayableRuntime(config,Generation,startPaused:true,aiProfile:ai);
                routes=new UnityHostRouteService();
                runtime.RequestPause(false);
                while(runtime.Latest.Tick<2&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)
                {routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
                Assert.GreaterOrEqual(runtime.Latest.Tick,2);
                runtime.RequestPause(true);
                var initial=runtime.RequestCaptureBytes();
                while(!initial.IsCompleted&&Time.realtimeSinceStartupAsDouble<deadline)
                {routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
                Assert.True(initial.IsCompletedSuccessfully,initial.Exception?.ToString());
                Stop(runtime,routes);
                runtime=PlayableRuntime.RestoreBytesForTests(initial.Result,config,ai,r=>SeedMidgame(Authority(r)));
                routes=new UnityHostRouteService();
                var tank=runtime.Latest.Entities.Single(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank);
                runtime.RequestPause(false);
                bool marched=false;
                while(runtime.Latest.Tick<600&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)
                {
                    routes.Service(runtime,profile.NavigationRequestsPerFrame);
                    var view=runtime.Latest;
                    var actor=view.Entities.Single(u=>u.Id==tank.Id);
                    var stamp=actor.OrderStamp;
                    var checkpoint=runtime.AiCheckpoint;
                    Assert.IsNull(view.Failure);
                    Assert.IsNull(routes.LastProjectionFailure);
                    if(stamp?.Kind==PlayableCommandKind.AttackMove&&checkpoint?.Armies!=null&&
                        checkpoint.Armies.Armies.Any(g=>AiRepairUsePolicy.GuardMission(g)&&g.Members.Contains(tank.Id))&&
                        checkpoint.Records.Any(r=>r.Policy==AiArmyPlanner.Policy&&r.ActionId==stamp.ActionId&&
                            r.CommandSequence==stamp.Sequence&&r.Status==PlayableAiDeliveryStatus.Applied)&&
                        AiOpponentSelector.Distance(tank.Position,actor.Position)>PlayableUnitRules.Radius(profile,tank.Kind))
                    {marched=true;break;}
                    yield return null;
                }
                Assert.True(marched,"A live guard order is applied and produces physical movement through the ordinary host.");
            }
            finally{if(runtime!=null&&!runtime.IsStopped)Stop(runtime,routes);}
        }

        [UnityTest, Timeout(95000)]
        public IEnumerator MineGuardInterceptsFromTheEnemySide() => InterceptAtObjective(false);

        [UnityTest, Timeout(95000)]
        public IEnumerator RelayCapturerInterceptsFromTheEnemySide() => InterceptAtObjective(true);

        private static IEnumerator InterceptAtObjective(bool relay)
        {
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.local/front", "match-"+Guid.NewGuid().ToString("N")));
            var store = new NativeBalanceStore(PlayableProfile.Default, Path.Combine(directory,"catalog.json"), authoring:false, startOnRelease:true);
            Assert.True(store.HasActive, store.Problem);
            var p = store.Active;
            var ai = store.ActiveAiProfile;
            var kind = relay ? PlayableBuildingKind.RelayTower : PlayableBuildingKind.Mine;
            int side = relay ? -1 : 1;
            var starts = new[] {
                new OfflineStart("ally",1,new NavPoint(-22*side,-10),new NavPoint(-23*side,-18),pin:1),
                new OfflineStart("opponent",2,new NavPoint(23*side,12),new NavPoint(23*side,6),pin:2) };
            var center = new NavPoint(0,0);
            var sites = starts.Select(start => new TerritorySite(start.SiteId,PlayableBuildingKind.Headquarters,
                start.Position,Array.Empty<TerritorySlot>())).Append(new TerritorySite(3,kind,center,Array.Empty<TerritorySlot>())).ToArray();
            var config = new OfflineMatchConfiguration(p,"objective-front",kind.ToString(),"ordinary-threaded",Seed,
                new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},
                starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},spectators:new[]{PlayableRuntime.LocalSpectatorId},
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-13*side,-6))},
                scenarioBuildings:relay?Array.Empty<OfflineScenarioBuilding>():new[]{new OfflineScenarioBuilding(1,kind,3,0,center,0)});
            PlayableRuntime runtime=null;
            UnityHostRouteService routes=null;
            TestMatchLogSession.StartDefault(directory);
            try
            {
                runtime=new PlayableRuntime(config,Generation,startPaused:true,aiProfile:ai);
                routes=new UnityHostRouteService();
                double deadline=Time.realtimeSinceStartupAsDouble+80;
                runtime.RequestPause(false);
                while(runtime.Latest.Tick<2 && Time.realtimeSinceStartupAsDouble<deadline)
                {routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                runtime.RequestPause(true);
                var capture=runtime.RequestCaptureBytes();
                while(!capture.IsCompleted&&Time.realtimeSinceStartupAsDouble<deadline){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.True(capture.IsCompletedSuccessfully);
                Stop(runtime,routes);
                runtime=PlayableRuntime.RestoreBytesForTests(capture.Result,config,ai,r=>SeedMidgame(Authority(r)));
                routes=new UnityHostRouteService();
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                runtime.RequestPause(false);
                int guard=0, intruder=0, health=0;
                long arrived=0,shot=0;
                bool hit=false,secured=false,attackIssued=false;
                NavPoint at=default;
                while(runtime.Latest.Tick<3600 && !runtime.IsStopped && Time.realtimeSinceStartupAsDouble<deadline)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);
                    Assert.IsNull(runtime.Latest.Failure);
                    var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);
                    var view=frame.Overview;
                    var site=view.Sites.First(s=>s.Site.Id==3);
                    secured=site.Owner==PlayableOwner.Player;
                    if(guard==0 && secured)
                    {
                        var unit=view.Entities.FirstOrDefault(u=>u.Owner==PlayableOwner.Player && u.OrderStamp?.Origin==PlayableOrderOrigin.Ai &&
                            (relay||u.Kind==PlayableEntityKind.Tank) && !u.Moving &&
                            u.Position.X*starts[1].Position.X+u.Position.Z*starts[1].Position.Z>PlayableUnitRules.Radius(p,u.Kind)*Math.Sqrt(23*23+12*12) &&
                            AiOpponentSelector.Distance(center,u.Position)<=(relay?p.RelayCaptureRadius:TerritoryRules.Radius(p,kind)+p.FollowDistance+2*p.TankCollisionRadius));
                        if(unit!=null)
                        {
                            guard=unit.Id; at=unit.Position; arrived=view.Tick;
                            var opponent=view.Entities.First(u=>u.Owner==PlayableOwner.Enemy && u.Kind==PlayableEntityKind.Explorer);
                            intruder=opponent.Id;
                            Assert.True(runtime.TrySubmit(new PlayableCommand(Generation,1,"human-owner",relay?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,
                                new[]{intruder},relay?center:new NavPoint(5*side,3))).Accepted);
                        }
                    }
                    if(guard!=0)
                    {
                        if (!relay && !attackIssued && frame.Players.First(v=>v.Owner==PlayableOwner.Enemy).Buildings
                            .Any(b=>b.Id==site.CenterId && b.Owner==PlayableOwner.Player))
                        {
                            attackIssued=runtime.TrySubmit(new PlayableCommand(Generation,2,"human-owner",PlayableCommandKind.Attack,
                                new[]{intruder},targetId:site.CenterId)).Accepted;
                        }
                        var target=view.Entities.FirstOrDefault(u=>u.Id==intruder);
                        if(shot==0 && target!=null && view.Projectiles.Any(j=>j.OwnerId==guard&&j.TargetId==intruder))
                        {
                            shot=view.Tick;
                            health=target.Health;
                        }
                        hit=shot>0 && view.Tick>shot && (target==null || target.Health<health);
                        if(hit&&secured)break;
                    }
                    yield return null;
                }
                TestContext.WriteLine($"OBJECTIVE_FRONT kind={kind} profile={p.ProfileId}@{p.Revision} ai={ai.Id}@{ai.Revision} tick={runtime.Latest.Tick} guard={guard} position=({at.X:F2},{at.Z:F2}) arrival={arrived} enemy={intruder} shot={shot} hit={hit} secured={secured}");
                Assert.True(arrived>0&&hit&&secured,"Guard must reach the enemy-facing side and fire on the approaching enemy while retaining the objective.");
            }
            finally{try{if(runtime!=null&&!runtime.IsStopped)Stop(runtime,routes);}finally{TestMatchLogSession.Shutdown();}}
        }

        private static OfflineMatchConfiguration Configuration(PlayableProfile p,bool quietGuard=false)
        {
            var starts=new[]{
                new OfflineStart("west",1,new NavPoint(-44,-18),new NavPoint(-13,7),pin:1),
                new OfflineStart("east",2,new NavPoint(43,30),new NavPoint(41,19),pin:2)
            };
            var sites=new[]{
                new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,Array.Empty<TerritorySlot>()),
                new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>()),
                new TerritorySite(3,PlayableBuildingKind.Mine,new NavPoint(-20,20),Array.Empty<TerritorySlot>()),
                new TerritorySite(4,PlayableBuildingKind.RelayTower,new NavPoint(-10,7),Array.Empty<TerritorySlot>())
            };
            var core=Enumerable.Range(0,3).Select(i=>new NavPoint(-17+i*3,-28));
            var remnant=Enumerable.Range(0,9).Select(i=>new NavPoint(-32+i%3*3,-9+i/3*3));
            return new OfflineMatchConfiguration(p,"territorial-recovery-v1","flat-territorial-recovery-v1","ordinary-threaded-recast",Seed,
                new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},
                starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},spectators:new[]{PlayableRuntime.LocalSpectatorId},
                scenario:(quietGuard?remnant.Take(1):core.Concat(remnant)).Select(point=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,point)).ToArray(),
                scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Mine,3,0,sites[2].Position,0)});
        }

        private static void SeedCoreLoss(PlayableRuntime runtime,out long army,out int[] remnant)
        {
            var authority=Authority(runtime);
            var loop=authority.Scheduler.Owners.Single();
            var registry=(AiArmyRegistry)Field(loop,"armies");
            var planner=(AiArmyPlanner)Field(loop,"armyPlanner");
            // Set a midgame precondition with a finished opening and no live
            // producer. All post-restore decisions and receipts are ordinary.
            var o=PlayableAiObservation.From(authority.ParticipantView("player-1"));
            SeedMidgame(authority);
            var tanks=o.Entities.Where(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Tank).OrderBy(u=>u.Id).ToArray();
            Assert.AreEqual(12,tanks.Length);
            int[] core=tanks.Take(3).Select(u=>u.Id).ToArray();
            remnant=tanks.Skip(3).Select(u=>u.Id).ToArray();
            var oldTarget=o.PublicScoutObjectives.Single(t=>t.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart);
            var action=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,
                PlayableCommandKind.AttackMove,core,oldTarget.Approach,siteId:oldTarget.SiteId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            planner.Commit(o,action,registry);
            army=planner.Capture().ArmyId;
            Assert.True(registry.TryReinforce(o,army,AiArmyPlanner.Policy,remnant));
            foreach(int unit in core)
                typeof(PlayableDomain).GetMethod("Damage",Fields).Invoke(authority.Domain,new object[]{unit,10000});
            planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,1,0,0,0,PlayableAiDeliveryStatus.Cancelled,
                PlayableCommandStatus.Cancelled,"fixture core destroyed before delivery",o.OwnerId,action.SourceIdentity,
                new AiReceiptIdentity(Generation,o.OwnerId,1,1),AiArmyPlanner.Policy,action.Kind));
        }

        private static void SeedMidgame(PlayableAuthorityTick authority)
        {
            var loop=authority.Scheduler.Owners.Single();
            var o=PlayableAiObservation.From(authority.ParticipantView("player-1"));
            var composition=Field(loop,"composition");
            SetField(composition,"phase",AiGamePhase.Mid);
            SetField(composition,"captured",true);
            SetField(composition,"startingIncome",o.IncomePerSecond);
            SetField(composition,"entered",o.Tick);
            SetField(composition,"observed",o.Tick);
            SetField(Field(loop,"openingExecutor"),"phase",PlayableAiOpeningPhase.Aborted);
        }

        private static object Field(object target,string field)=>target.GetType().GetField(field,Fields).GetValue(target);
        private static void SetField(object target,string field,object value)=>target.GetType().GetField(field,Fields).SetValue(target,value);
        private static PlayableAuthorityTick Authority(PlayableRuntime runtime)=>(PlayableAuthorityTick)Field(runtime,"authorityTick");
        private static void Stop(PlayableRuntime runtime,UnityHostRouteService routes)
        {
            var worker=routes==null?null:(CertifiedRouteWorker)Field(routes,"worker");
            runtime.RequestStop();
            routes?.Dispose();
            runtime.Dispose();
            Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));
            var authority=Authority(runtime);
            authority.Domain.Navigation.ReleaseCommittedHostRoot();
            authority.Domain.DestroyOwnedNavigation();
        }
    }
}
