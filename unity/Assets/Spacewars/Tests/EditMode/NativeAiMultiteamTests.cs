using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiMultiteamTests
    {
        private static AiOpponentAssessment Target(int id,double score=1,double eta=10)=>new AiOpponentAssessment{Team=7,TargetId=id,Score=score,ArrivalSeconds=eta};
        private static AiTeamPublication Publication(string owner,int team=3,long tick=10)=>new AiTeamPublication{Owner=owner,Team=team,Tick=tick,Mission=new AiArmyMissionState(),Targets=new[]{Target(90)},RequestedSites=new[]{21,22}};
        [Test] public void BoardStableOrderingKeepsClaimsAndSharedObjectivePrivateToTeam()
        {
            var a=Publication("a");var b=Publication("b");var enemy=Publication("enemy",7);
            a.ExpansionSite=21;a.ExpansionExpires=100;
            var x=AiTeamCoordinator.Resolve(new[]{a,b,enemy},AiProfile.Initial);
            var y=AiTeamCoordinator.Resolve(new[]{enemy,b,a},AiProfile.Initial);
            foreach(var id in x.Keys){CollectionAssert.AreEquivalent(x[id].DeniedSites,y[id].DeniedSites);Assert.AreEqual(x[id].TargetId,y[id].TargetId);}
            Assert.True(x["b"].DeniedSites.Contains(21));Assert.False(x["a"].DeniedSites.Contains(21));Assert.IsEmpty(x["enemy"].DeniedSites);
            Assert.AreEqual(90,x["a"].TargetId);Assert.AreEqual(90,x["b"].TargetId);Assert.Zero(x["enemy"].TargetId);
            var removed=AiTeamCoordinator.Resolve(new[]{b,enemy},AiProfile.Initial);Assert.IsEmpty(removed["b"].DeniedSites,"eliminated owner releases claims");
        }
        [Test] public void ArrivalWaitIsBoundedByDurableMissionClockAndSupportKeepsReserveReason()
        {
            var a=Publication("a",tick:100);var b=Publication("b",tick:100);a.HomeThreatened=true;a.ThreatTargets=new[]{90};
            a.Mission=new AiArmyMissionState{ArmyId=1,TargetKind=PlayableRouteTargetKind.VisibleEnemy,TargetId=90,StartedTick=90,ProgressTick=100};
            a.Targets=new[]{Target(90,eta:100)};b.Targets=new[]{Target(90,eta:1)};
            var assignment=AiTeamCoordinator.Resolve(new[]{a,b},AiProfile.Initial)["b"];
            Assert.True(assignment.Helping);Assert.AreEqual(90+450,assignment.WaitUntilTick);
            a.Tick=b.Tick=541;a.Mission.ProgressTick=541;
            assignment=AiTeamCoordinator.Resolve(new[]{a,b},AiProfile.Initial)["b"];Assert.Zero(assignment.WaitUntilTick);StringAssert.Contains("fallback",assignment.Reason);
        }
        [Test] public void HelpSelectsTheObservedAttackerAndDoesNotCallUnrelatedPressureHelp()
        {
            var a=Publication("a");var b=Publication("b");a.HomeThreatened=true;a.ThreatTargets=new[]{91};
            a.Targets=b.Targets=new[]{Target(90,score:1),Target(91,score:0)};
            var board=AiTeamCoordinator.Resolve(new[]{a,b},AiProfile.Initial);Assert.AreEqual(91,board["b"].TargetId);Assert.True(board["b"].Helping);
            b.Targets=new[]{Target(90,score:1)};board=AiTeamCoordinator.Resolve(new[]{a,b},AiProfile.Initial);Assert.AreEqual(90,board["b"].TargetId);Assert.False(board["b"].Helping);
            a.Targets=new[]{Target(91)};board=AiTeamCoordinator.Resolve(new[]{a,b},AiProfile.Initial);Assert.Zero(board["b"].TargetId);StringAssert.Contains("unreachable",board["b"].Reason);
        }
        [TestCase("1,2")][TestCase("1,2,3")][TestCase("1,2,3,4")][TestCase("1,1,2,2")]
        [TestCase("1,1,2,2,3,3")][TestCase("1,2,2,3,3,3")][TestCase("1,2,3,4,5,6,7,8")]
        public void AuthoredModeMatrixUsesRealHostAndOwnerLocalCommands(string teams)
        {
            var map=new AiTestMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var roster=teams.Split(',').Select((team,i)=>new OfflineParticipant("ai-"+i,i+1,int.Parse(team),OfflineControl.Ai)).ToArray();
            var c=map.Configuration(p,7108,roster);var a=new PlayableAuthorityTick(c,71);
            using(var routes=new UnityHostRouteService())
            {
                for(int tick=0;tick<120;tick++)Step(a,routes);
                Assert.AreEqual(roster.Length,a.CaptureDiagnosticCheckpoints().Length);
                foreach(var owner in roster)
                {
                    var view=a.ParticipantView(owner.Id);Assert.IsNull(view.Failure);Assert.AreEqual(0,view.Metrics.Errors);
                    Assert.AreEqual(owner.Team,view.Team);Assert.Greater(view.Buildings.Count(b=>b.Owner==view.Owner),0);
                    var checkpoint=a.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==owner.Id);
                    Assert.Greater(checkpoint.DecisionTick,0);Assert.True(checkpoint.Records.Any(x=>x.Status==PlayableAiDeliveryStatus.Applied),"every AI executes ordinary owner-local commands");
                }
                var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
                var before=a.Tick;Assert.True(a.TryAdvance(true));Assert.AreEqual(before,a.Tick);Assert.True(restored.TryAdvance(true));
                Step(a,routes);using(var other=new UnityHostRouteService()){Step(restored,other);}
                CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes(),"phase board reconstructed from durable owner state");
                TestContext.WriteLine("M5 mode="+teams+" ticks="+a.Tick+" owners="+roster.Length);
            }
        }
        private static PlayableAiObservation Combat(bool human=false,int count=5,bool battle=false,long tick=10)
        {
            var p=PlayableProfile.Default;var near=new NavPoint(6,0);var far=new NavPoint(35,0);
            var own=Enumerable.Range(1,count).Select(i=>new PlayableEntitySnapshot(i,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(0,i*.1),p.TankHealth,false,0,0,0)).ToArray();
            var units=own.Concat(new[]{new PlayableEntitySnapshot(201,PlayableOwner.Enemy,PlayableEntityKind.Tank,near,p.TankHealth,false,battle?101:0,0,0),new PlayableEntitySnapshot(101,PlayableOwner.Third,PlayableEntityKind.Tank,far,p.TankHealth,false,0,0,0)}).ToArray();
            var vision=new PlayableVision(1,100,100,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),100)},Array.Empty<KnownBuilding>());
            var buildings=new[]{new PlayableBuildingSnapshot(50,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(-2,0),p.HeadquartersHealth,1,0,0,default),new PlayableBuildingSnapshot(51,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(-3,0),100,1,0,0,default),new PlayableBuildingSnapshot(52,PlayableOwner.Player,PlayableBuildingKind.Refinery,new NavPoint(-4,0),100,1,0,0,default)};
            var proofs=own.SelectMany(u=>new[]{new PlayableRouteProof(u.Id,u.Owner,71,tick,1,u.Position,p.TankCollisionRadius,PlayableRouteTargetKind.VisibleEnemy,201,near,near,new[]{u.Position,near}),new PlayableRouteProof(u.Id,u.Owner,71,tick,1,u.Position,p.TankCollisionRadius,PlayableRouteTargetKind.VisibleEnemy,101,far,far,new[]{u.Position,far})}).ToArray();
            var participants=new[]{new OfflineParticipant("ai-0",1,1,OfflineControl.Ai),new OfflineParticipant("other",2,2,human?OfflineControl.Human:OfflineControl.Ai),new OfflineParticipant("third",3,3,OfflineControl.Ai)};
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:proofs,ownerId:"ai-0",team:1,participants:participants));
        }
        [Test] public void OpponentsRemainSeparateControlKindIsIgnoredAndObservedWarCreatesOpportunity()
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;var a=AiOpponentSelector.Assess(Combat(),p,ai);var b=AiOpponentSelector.Assess(Combat(human:true),p,ai);
            Assert.AreEqual(2,a.Select(x=>x.Team).Distinct().Count());Assert.AreEqual(201,a[0].TargetId,"observed proximity outranks low target ID");
            CollectionAssert.AreEqual(a.Select(x=>x.Score),b.Select(x=>x.Score));
            var war=AiOpponentSelector.Assess(Combat(battle:true),p,ai);Assert.AreEqual(1,war.Single(x=>x.Team==2).Opportunity);Assert.AreEqual(0,war.Single(x=>x.Team==3).Opportunity);
            var active=AiOpponentSelector.Assess(Combat(),p,ai,101);Assert.AreEqual(1,active.Single(x=>x.Team==2).NewFrontCost);Assert.Zero(active.Single(x=>x.Team==3).NewFrontCost);
        }
        [Test] public void ActualMissionChoosesOpponentAndAlliedHelpPreservesOwnerBodiesAndCap()
        {
            var o=Combat();var ai=AiProfile.Initial;var p=PlayableProfile.Default;
            var opening=PlayableAiOpeningComposition.Initialize(7,"ai-0",forcedOpening:PlayableAiOpening.Safe,aiProfile:ai);
            var planner=new AiArmyPlanner("ai-0",71);var registry=new AiArmyRegistry("ai-0",71,ai,AiDifficulty.Fighter);
            var action=planner.Propose(o,opening,p,ai,registry);Assert.NotNull(action);Assert.AreEqual(201,action.TargetId);
            planner.TeamAssignment=new AiTeamAssignment{TargetId=201,Helping=true};
            action=planner.Propose(o,opening,p,ai,registry);Assert.NotNull(action);Assert.AreEqual(4,action.EntityIds.Count);planner.Commit(o,action,registry);
            Assert.AreEqual(1,registry.Capture().Armies.Count(a=>a.Major));Assert.AreEqual(1,o.Entities.Count(u=>u.Owner==o.Owner&&registry.ArmyFor(u.Id)==0));
            var small=new AiArmyPlanner("ai-0",71){TeamAssignment=new AiTeamAssignment{TargetId=201,Helping=true}};
            Assert.IsNull(small.Propose(Combat(count:3),opening,p,ai,new AiArmyRegistry("ai-0",71,ai,AiDifficulty.Fighter)),"help must not consume home reserve or fragment a major army");
        }
        [Test] public void ActualThreeTeamCombatPublishesAlliedObjectivesAndEliminatesOwnersBeforeTeamVictory()
        {
            var map=new AiTestMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var roster=new[]{new OfflineParticipant("ai-0",1,1,OfflineControl.Ai),new OfflineParticipant("ai-1",2,1,OfflineControl.Ai),new OfflineParticipant("human-2",3,2,OfflineControl.Human),new OfflineParticipant("human-3",4,3,OfflineControl.Human)};
            int seed=Enumerable.Range(1,10000).First(n=>Enumerable.Range(0,2).All(i=>PlayableAiOpeningComposition.Initialize(n,"ai-"+i,aiProfile:AiProfile.Initial).Opening==PlayableAiOpening.BlindRush));
            var basis=map.Configuration(p,seed,roster);var costs=new double[8,8];for(int i=0;i<8;i++)for(int j=0;j<8;j++)costs[i,j]=basis.RouteCost(i,j);
            var units=Enumerable.Range(0,2).SelectMany(owner=>Enumerable.Range(0,5).Select(i=>new OfflineScenarioUnit(owner+1,PlayableEntityKind.Tank,new NavPoint(-4-i*2,owner==0?-2:2))))
                .Concat(new[]{new OfflineScenarioUnit(3,PlayableEntityKind.Tank,new NavPoint(7,0)),new OfflineScenarioUnit(4,PlayableEntityKind.Tank,new NavPoint(7,8))}).ToArray();
            var buildings=Enumerable.Range(0,2).SelectMany(i=>new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery}.Select((kind,slot)=>new OfflineScenarioBuilding(i+1,kind,i+1,slot+1,basis.Sites[i].Slots[slot].Position,basis.Sites[i].Slots[slot].Heading))).ToArray();
            var c=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,seed,roster,basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,scenario:units,terrain:map.Id,scenarioBuildings:buildings);
            var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                // Checkpoint while the actual allied mission/commands are live, then
                // replay the unchanged 150-tick scenario through the same host path.
                for(int tick=0;tick<75;tick++)Step(a,host);
                var mid=a.CaptureBytes();var fork=PlayableAuthorityTick.RestoreBytes(mid,c);CollectionAssert.AreEqual(mid,fork.CaptureBytes());
                using(var restoredHost=new UnityHostRouteService())
                {
                    for(int tick=75;tick<150;tick++){Step(a,host);Step(fork,restoredHost);CollectionAssert.AreEqual(a.CaptureBytes(),fork.CaptureBytes(),"live team board/owner state replay at tick "+a.Tick);}
                }
                fork.Stop();
                foreach(var owner in roster.Take(2))
                {
                    var check=a.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==owner.Id);
                    Assert.True(check.Records.Any(x=>x.Policy==AiArmyPlanner.Policy&&x.Status==PlayableAiDeliveryStatus.Applied),owner.Id+" must attack through the real authority/route path");
                    TestContext.WriteLine(owner.Id+" "+string.Join(" | ",check.Records.Where(x=>x.Policy==AiArmyPlanner.Policy&&x.Status==PlayableAiDeliveryStatus.Scheduled).Select(x=>x.Message)));
                }
                // Diagnostic terminal trigger tests authority elimination, not natural AI strength.
                var domain=typeof(PlayableAuthorityTick).GetField("domain",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(a);
                var damage=domain.GetType().GetMethod("Damage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                void Eliminate(string id){var v=a.ParticipantView(id);foreach(var center in v.Buildings.Where(b=>b.Owner==v.Owner&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost)))damage.Invoke(domain,new object[]{center.Id,10000});Step(a,host);}
                Eliminate("ai-0");Assert.IsNull(a.WinnerTeam);var stopped=a.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId=="ai-0");Assert.Zero(stopped.PendingActionId);
                Eliminate("human-2");Assert.IsNull(a.WinnerTeam,"third team remains alive");Eliminate("human-3");Assert.AreEqual(1,a.WinnerTeam);
                var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);Assert.AreEqual(1,restored.WinnerTeam);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());a.Stop();restored.Stop();
            }
        }
        private static void Step(PlayableAuthorityTick a,UnityHostRouteService routes)
        {for(int retries=0;!a.TryAdvance();retries++){Assert.Less(retries,4096);routes.Service(a,64);}}
    }
}
