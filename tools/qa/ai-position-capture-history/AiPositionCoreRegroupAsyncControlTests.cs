using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiArmyTests
    {
        private static OfflineMatchConfiguration PositionRepairReadbackConfiguration()
        {
            var basis=A4NativeConfig();var p=basis.Profile;var ai=AiProfile.Initial;
            var added=new[]{new TerritorySlot(3,new NavPoint(-24,9),0),new TerritorySlot(4,new NavPoint(-14,9),0),new TerritorySlot(5,new NavPoint(-14,-9),0)};
            var sites=basis.Sites.Select(s=>s.Id==1?new TerritorySite(s.Id,s.Kind,s.Position,s.Slots.Concat(added).ToArray()):s).ToArray();
            var victimAt=new NavPoint(1,-8);double repairRange=AiPositionDevelopmentPlanner.Range(p,PlayableBuildingKind.RepairTower);
            var pad=sites.Single(s=>s.Id==1).DefenseSlots(p).Where(s=>AiPositionDevelopmentPlanner.Distance(s.Position,victimAt)>repairRange+PlayableUnitRules.Radius(p,PlayableEntityKind.Tank))
                .OrderBy(s=>AiPositionDevelopmentPlanner.Distance(s.Position,victimAt)).ThenBy(s=>s.Id).First();
            var buildings=basis.ScenarioBuildings.Concat(new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.ScientificCenter,1,3,added[0].Position),
                new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,4,added[1].Position),new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,5,added[2].Position),
                new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,1,pad.Id,pad.Position,pad.Heading)}).ToArray();
            var units=basis.ScenarioUnits.Concat(new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-1,-8)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,victimAt),
                new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(1,-2))}).ToArray();
            return new OfflineMatchConfiguration(p,"ai-position-repair-runtime-v1",basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),sites,basis.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},
                spectators:new[]{PlayableRuntime.LocalSpectatorId},scenario:units,scenarioBuildings:buildings);
        }

        [Test,Timeout(120000)]
        public void PositionRepair_AsyncOriginalCoreRegroupKeepsRecoveringClient()
        {
            var basis=PositionRepairReadbackConfiguration();var p=basis.Profile;var ai=AiProfile.Initial;
            // Controlled small precondition: one healthy line core plus one repair
            // client, with occupied production pads. Original witness is unchanged.
            var selected=basis.ScenarioUnits.Where(u=>u.LogicalPlayer!=1||u.Kind!=PlayableEntityKind.Tank||u.Position.Equals(new NavPoint(-7,-8))||u.Position.Equals(new NavPoint(1,-8))).ToArray();
            var buildings=basis.ScenarioBuildings.Select(b=>b.Kind==PlayableBuildingKind.Factory?new OfflineScenarioBuilding(b.LogicalPlayer,PlayableBuildingKind.Refinery,b.SiteId,b.SlotId,b.Position,b.Heading):b).ToArray();
            var config=new OfflineMatchConfiguration(p,basis.SourceIdentity+"-small-core-loss",basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},spectators:basis.Spectators.ToArray(),scenario:selected,scenarioBuildings:buildings);
            var runtime=new PlayableRuntime(config,107,aiDifficulty:AiDifficulty.Veteran,spectator:false,aiProfile:ai);var routes=new UnityHostRouteService();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);
            string source=Environment.GetEnvironmentVariable("AI_POSITION_SOURCE_SHA")??"runner-bound";
            var output=Path.GetFullPath(Path.Combine(".local/ai-position-ready","core-regroup-"+source));Directory.CreateDirectory(output);
            bool retired=false,moved=false,returnForCore=false,coreAttack=false,coreInjured=false;int coreId=0;object last=null;long originalArmy=0,lastTick=-1;object proof=null;
            var starts=new System.Collections.Generic.Dictionary<int,NavPoint>();
            try
            {
                var own=runtime.ParticipantView("player-1");var enemy=runtime.ParticipantView("enemy-1");
                int victim=own.Entities.Single(u=>u.Owner==own.Owner&&u.Kind==PlayableEntityKind.Tank&&u.Position.Equals(new NavPoint(1,-8))).Id;
                int hawk=enemy.Entities.Single(u=>u.Owner==enemy.Owner&&u.Kind==PlayableEntityKind.Hawk).Id;
                Assert.True(enemy.Entities.Any(u=>u.Id==victim));
                Assert.True(runtime.TrySubmit(new PlayableCommand(107,1,"enemy-1",PlayableCommandKind.Attack,new[]{hawk},targetId:victim).AsHuman()).Accepted);
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);var clock=Stopwatch.StartNew();
                while(runtime.Latest.Tick<1400&&clock.Elapsed.TotalSeconds<45&&!moved)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);var view=runtime.ParticipantView("player-1");
                    Assert.IsNull(view.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    if(view.Tick==lastTick){Thread.Sleep(1);continue;}lastTick=view.Tick;
                    if(view.Outcome!=PlayableMatchOutcome.Playing)break;
                    var client=view.Entities.SingleOrDefault(u=>u.Id==victim&&u.Health>0);if(client==null)break;
                    if(!retired&&PlayableUnitRules.HealthRatio(p,client)<ai.Value("positionDevelopment.repairHealth"))
                    {Assert.True(runtime.TrySubmit(new PlayableCommand(107,2,"enemy-1",PlayableCommandKind.Move,new[]{hawk},config.Starts[1].ExplorerAnchor).AsHuman()).Accepted);retired=true;}
                    var cp=authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="player-1");
                    var mission=cp.ArmyMission;var army=cp.Armies?.Armies.SingleOrDefault(a=>a.Id==mission?.ArmyId);
                    bool memo=mission?.RepairClients?.Keys.Any(k=>AiRepairUsePolicy.Parse(k,out var unit,out _,out _)&&unit==victim)==true;
                    if(army==null||!memo)continue;
                    var observation=PlayableAiObservation.From(view);
                    if(originalArmy==0&&AiRepairUsePolicy.Recovering(observation,p,ai,mission.RepairClients,army.TacticalOwner,cp.Records).Contains(victim))originalArmy=army.Id;
                    if(originalArmy>0&&army.Id==originalArmy)
                    {
                        last=new{view.Tick,mission,army,client,units=view.Entities};
                        if(!returnForCore){Assert.True(runtime.TrySubmit(new PlayableCommand(107,3,"enemy-1",PlayableCommandKind.Move,new[]{hawk},new NavPoint(1,-8)).AsHuman()).Accepted);returnForCore=true;coreId=army.LeaderId;Assert.AreNotEqual(victim,coreId);}
                        var visible=runtime.ParticipantView("enemy-1");
                        if(!coreAttack&&visible.Entities.Any(u=>u.Id==coreId&&u.Health>0)){Assert.True(runtime.TrySubmit(new PlayableCommand(107,4,"enemy-1",PlayableCommandKind.Attack,new[]{hawk},targetId:coreId).AsHuman()).Accepted);coreAttack=true;}
                        var core=view.Entities.SingleOrDefault(u=>u.Id==coreId&&u.Health>0);
                        if(coreAttack&&!coreInjured&&core!=null&&PlayableUnitRules.HealthRatio(p,core)<1){Assert.True(runtime.TrySubmit(new PlayableCommand(107,5,"enemy-1",PlayableCommandKind.Move,new[]{hawk},config.Starts[1].ExplorerAnchor).AsHuman()).Accepted);coreInjured=true;}
                    }
                    if(originalArmy==0||army.Id!=originalArmy||army.Phase!=AiArmyPhase.Retreating)continue;
                    foreach(var unit in view.Entities.Where(u=>u.Owner==view.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&u.Id!=victim))
                    {
                        if(!AiTacticalExecutor.EffectiveWithdrawal(observation,army,unit,cp.Records,p))continue;
                        if(!starts.TryGetValue(unit.Id,out var from)){starts.Add(unit.Id,unit.Position);continue;}
                        if(AiPositionDevelopmentPlanner.Distance(from,unit.Position)<=PlayableUnitRules.Radius(p,unit.Kind)||
                            AiPositionDevelopmentPlanner.Distance(unit.Position,unit.CurrentOrder.Destination)>=AiPositionDevelopmentPlanner.Distance(from,unit.CurrentOrder.Destination))continue;
                        moved=true;proof=new{view.Tick,originalArmy,army,client,unit,from,mission};break;
                    }
                    Thread.Sleep(1);
                }
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,config.Seed,generation=107,retired,returnForCore,coreAttack,coreInjured,originalArmy,moved,proof,last,tick=runtime.Latest.Tick,wallSeconds=clock.Elapsed.TotalSeconds,qualification="Small actual async core-regroup motion proof; does not replace original heal/Follow/join/exact scenario"},Newtonsoft.Json.Formatting.Indented));
                Assert.True(retired);Assert.True(coreInjured,"real human attack must create the small core-loss precondition");Assert.Greater(originalArmy,0,"actual repair-client binding required");Assert.True(moved,"same live original core must physically withdraw on exact certified authority while retaining its recovering client");
            }
            finally
            {
                var worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",flags).GetValue(routes);
                runtime.RequestStop();routes.Dispose();runtime.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));
                authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();
            }
        }
    }
}
