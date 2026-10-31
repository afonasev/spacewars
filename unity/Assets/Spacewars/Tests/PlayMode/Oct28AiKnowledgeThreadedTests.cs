using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.PlayMode
{
    public sealed class Oct28AiKnowledgeThreadedTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static object Field(object o,string key)=>o.GetType().GetField(key,F).GetValue(o);
        private static OfflineMatchConfiguration Configuration(PlayableProfile p,bool existingEmptyMission)
        {
            const int seed=7;
            var starts=new[]{new OfflineStart("home",1,new NavPoint(-24,-12),new NavPoint(-19,-12),pin:1),new OfflineStart("empty",2,new NavPoint(-2,0),new NavPoint(-7,0)),new OfflineStart("enemy",3,new NavPoint(23,18),new NavPoint(18,18),pin:2)};
            var sites=starts.Select((s,i)=>new TerritorySite(i+1,PlayableBuildingKind.Headquarters,s.Position,Array.Empty<TerritorySlot>())).ToArray();
            var scenario=Enumerable.Range(0,3).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,existingEmptyMission?new NavPoint(-4+i*3,-3):new NavPoint(-22+i*3,-17),heading:Math.PI/2)).ToArray();
            return new OfflineMatchConfiguration(p,"oct28-ai-knowledge","three-public-zones","ordinary-flat-threaded",seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,25,55},{25,0,35},{55,35,0}},scenario:scenario);
        }
        private static PlayableAiOwnerLoop MidgameOwner(PlayableProfile p)
        {
            const int seed=7;const long generation=81;
            var owner=new PlayableAiOwnerLoop(p,PlayableAiOpeningComposition.Initialize(seed,"player-1",forcedOpening:PlayableAiOpening.GreedySafe),generation);
            // Authored midgame, using the current complete milestone contract.
            var executor=Field(owner,"openingExecutor");executor.GetType().GetField("phase",F).SetValue(executor,PlayableAiOpeningPhase.Complete);executor.GetType().GetField("initialForceFulfilled",F).SetValue(executor,true);
            var milestones=(HashSet<string>)Field(executor,"completed");foreach(var value in new[]{"income","factory","force","pressure"})milestones.Add(value);
            return owner;
        }
        [TestCase(false)][TestCase(true)][Timeout(65000)] public void ScoutEvidenceOrExistingEmptyMissionLeadsToUsefulArmyMotion(bool existingEmptyMission)
        {
            const int seed=7;const long generation=81;var p=PlayableProfile.Default;
            var config=Configuration(p,existingEmptyMission);var domain=new PlayableDomain(p,generation,config);var owner=MidgameOwner(p);
            var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(new[]{owner}),seed,config);CertifiedRouteWorker worker=null;
            try{
                var initial=authority.ParticipantView("player-1");var scout=initial.Entities.Single(e=>e.Owner==initial.Owner&&e.Kind==PlayableEntityKind.Explorer);var tanks=initial.Entities.Where(e=>e.Owner==initial.Owner&&e.Kind==PlayableEntityKind.Tank).ToArray();Assert.AreEqual(3,tanks.Length);
                Assert.AreEqual(2,initial.PublicScoutObjectives.Count(o=>o.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart));
                var planner=(AiArmyPlanner)Field(owner,"armyPlanner");var registry=(AiArmyRegistry)Field(owner,"armies");
                if(existingEmptyMission){
                    // A previously applied ordinary AI command is a valid current
                    // save precondition, even though the repaired planner no longer selects it.
                    var observation=PlayableAiObservation.From(initial);var target=initial.PublicScoutObjectives.Single(o=>o.SiteId==2);
                    var action=new PlayableAiAction(1,initial.OwnerId,p.ProfileId,p.Revision,generation,initial.Sequence,PlayableCommandKind.AttackMove,tanks.Select(e=>e.Id).ToArray(),target.Approach,siteId:2,seed:seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                    planner.Commit(observation,action,registry);
                    var receipt=authority.Apply(new PlayableCommand(generation,1,initial.OwnerId,PlayableCommandKind.AttackMove,action.CopyEntityIds(),action.Target,siteId:2,origin:PlayableOrderOrigin.Ai,source:action.SourceIdentity,jobId:1,actionId:1));Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status);
                    planner.ObserveReceipt(new PlayableAiTraceRecord(observation.Identity,1,0,receipt.Sequence,0,PlayableAiDeliveryStatus.Applied,receipt.Status,"authored existing ordinary mission",initial.OwnerId,action.SourceIdentity,new AiReceiptIdentity(generation,initial.OwnerId,1,1),AiArmyPlanner.Policy,PlayableCommandKind.AttackMove));
                }else Assert.False(PlayableAiObservation.From(initial).Intel.Areas.Single(a=>a.Envelope.AreaId==2).Full,"Unknown alternative must require actual scouting");
                var bytes=authority.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,config);try{CollectionAssert.AreEqual(bytes,restored.CaptureBytes(),"Exact current format and authored map/profile binding before witness");}finally{restored.Stop();}
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                bool scoutMoved=false,empty=false,offensive=false,armyMoved=false,leftEmpty=!existingEmptyMission;long coverageTick=-1,orderTick=-1;var clock=Stopwatch.StartNew();
                while(authority.Tick<1800&&clock.Elapsed.TotalSeconds<55){
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}Assert.IsNull(worker.Publication.Failure);
                    var view=authority.ParticipantView("player-1");var intel=((AiKnowledgeTracker)Field(owner,"knowledge")).Capture();var mission=planner.Capture();
                    var sc=view.Entities.FirstOrDefault(e=>e.Id==scout.Id);scoutMoved|=sc!=null&&!sc.Position.Equals(scout.Position);
                    if(intel.Areas.Any(a=>a.AreaId==2&&a.EmptyAtLastCoverage)){empty=true;if(coverageTick<0)coverageTick=authority.Tick;}
                    bool emptyAttack=mission.ArmyId>0&&mission.TargetKind==PlayableRouteTargetKind.PublicObjective&&mission.TargetId==2;
                    if(!existingEmptyMission||empty)Assert.False(emptyAttack,"An ambiguous/known-empty public zone cannot retain an attacking army");
                    bool useful=mission.ArmyId>0&&(mission.TargetKind==PlayableRouteTargetKind.PublicObjective&&mission.TargetId==3||mission.TargetKind==PlayableRouteTargetKind.VisibleEnemy);
                    if(empty&&useful){offensive=true;if(orderTick<0)orderTick=authority.Tick;}
                    if(offensive){
                        armyMoved=tanks.All(t=>view.Entities.Any(e=>e.Id==t.Id&&Math.Sqrt(Math.Pow(e.Position.X-t.Position.X,2)+Math.Pow(e.Position.Z-t.Position.Z,2))>p.GroupMarchMaximumStretch));
                        leftEmpty|=tanks.All(t=>view.Entities.Any(e=>e.Id==t.Id&&Math.Sqrt(Math.Pow(e.Position.X-config.Starts[1].Position.X,2)+Math.Pow(e.Position.Z-config.Starts[1].Position.Z,2))>p.ExplorerVision));
                    }
                    if(empty&&offensive&&armyMoved&&leftEmpty&&(existingEmptyMission||scoutMoved))break;Thread.Sleep(1);
                }
                TestContext.WriteLine($"AI_KNOWLEDGE seed={seed} legacy={existingEmptyMission} tick={authority.Tick} scoutMotion={scoutMoved} empty={coverageTick} offensive={orderTick} armyMotion={armyMoved} leftEmpty={leftEmpty} pumps={worker.Publication.Pumps}");
                Assert.True(empty&&offensive&&armyMoved&&leftEmpty&&(existingEmptyMission||scoutMoved),"Genuine knowledge must produce actual useful army movement and clear an empty-pad camp");Assert.Greater(worker.Publication.Pumps,0);registry.AssertInvariants();
            }finally{worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));authority.Stop();}
        }
        [Test,Timeout(65000)] public void CurrentKnownEmptyStateRestoresAndContinuesExactlyWithRealKernel()
        {
            var p=PlayableProfile.Default;var config=Configuration(p,false);var domain=new PlayableDomain(p,81,config);var owner=MidgameOwner(p);
            var a=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(new[]{owner}),7,config);PlayableAuthorityTick b=null,c=null;
            var hot=new CertifiedRouteKernel();var cold=new CertifiedRouteKernel();
            void Advance(PlayableAuthorityTick authority,CertifiedRouteKernel kernel){
                for(int pump=0;pump<4096;pump++){if(authority.TryAdvance())return;kernel.Pump(authority.NavigationBinding,4);}Assert.Fail("Current fixed quantum must complete");
            }
            try{
                for(int i=0;i<180;i++)Advance(a,hot);
                var knowledge=a.CaptureDiagnosticCheckpoints().Single().Knowledge;
                TestContext.WriteLine($"CURRENT_KNOWLEDGE tick={a.Tick} observed={knowledge.ObservationTick} areas={knowledge.Areas.Count}");
                Assert.True(knowledge.Areas.Any(x=>x.AreaId==2&&x.EmptyAtLastCoverage),"Current real scout coverage must exist before capture");
                var saved=a.CaptureBytes();b=PlayableAuthorityTick.RestoreBytes(saved,config);CollectionAssert.AreEqual(saved,b.CaptureBytes());
                Assert.AreEqual(knowledge.ObservationTick,b.CaptureDiagnosticCheckpoints().Single().Knowledge.ObservationTick);
                for(int i=0;i<45;i++){Advance(a,hot);Advance(b,cold);}CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes(),"Exact current cold continuation including actual knowledge and mission decisions");
                for(int i=0;i<60;i++){Assert.True(a.TryAdvance(paused:true));Assert.True(b.TryAdvance(paused:true));}Assert.AreEqual(225,a.Tick);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                var data=AiProfile.Initial.CopyData();data.revision++;data.fields.Single(f=>f.path=="scouting.contactHalfLifeSeconds").value=120;data.fields.Single(f=>f.path=="scouting.revisitSeconds").value=90;var next=new AiProfile(data);
                Assert.IsNull(a.ApplyProfile(p,next));Assert.IsNull(b.ApplyProfile(p,next));CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                c=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),config,next);CollectionAssert.AreEqual(a.CaptureBytes(),c.CaptureBytes());
            }finally{hot.Clear();cold.Clear();a.Stop();b?.Stop();c?.Stop();}
        }
    }
}
