using System;
using System.Linq;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiOrbitalPolicyTests
    {
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile AI=AiProfile.Initial;
        static readonly NavPoint Target=new NavPoint(10,0);
        static PlayableEntitySnapshot Unit(int id,PlayableOwner owner,NavPoint point,double hp=40,PlayableEntityKind kind=PlayableEntityKind.Tank)=>new PlayableEntitySnapshot(id,owner,kind,point,(int)Math.Ceiling(hp),false,0,0,0,maxHealth:100,exactHealth:hp);
        static OrbitalEscalationSnapshot Orbital(int level,params int[] chosen)=>OrbitalEscalationState.Restore(level,0,0,chosen.Concat(new int[5-chosen.Length]).ToArray(),new double[5],P.CopyData()).Snapshot(P.CopyData());
        static PlayableSnapshot Snapshot(OrbitalEscalationSnapshot orbital,PlayableEntitySnapshot[] units=null,PlayableBuildingSnapshot[] buildings=null,PlayablePublicScoutObjective[] objectives=null,OrbitalMineSnapshot[] mines=null,bool visible=true)
        {
            var vision=new PlayableVision(0,100,100,1,1);vision.Refresh(visible?new[]{new VisionSource(default,100)}:Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());
            return new PlayableSnapshot(P.ProfileId,P.Revision,1,7,1,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units??Array.Empty<PlayableEntitySnapshot>(),buildings??Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),publicScoutObjectives:objectives).WithOrbital(orbital).WithMines(mines??Array.Empty<OrbitalMineSnapshot>(),Array.Empty<OrbitalMineBlastSnapshot>());
        }
        static AiKnowledgeState Knowledge(PlayableAiObservation o,params AiVisitedArea[] areas)=>new AiKnowledgeState(o.OwnerId,o.Generation,o.Tick,Array.Empty<AiKnownContact>(),areas);
        static PlayableAiAction Plan(PlayableSnapshot snapshot,AiKnowledgeState known=null,AiPersonality personality=AiPersonality.Aggressive)
        {var o=PlayableAiObservation.From(snapshot);return AiOrbitalPolicy.Plan(o,P,AI,known??Knowledge(o),personality,Array.Empty<PlayableAiTraceRecord>(),PlayableAiOpeningComposition.SourceIdentity);}
        static PlayablePublicScoutObjective Objective(int id,NavPoint p)=>new PlayablePublicScoutObjective(id,p,PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true);
        [Test] public void UnlockedAbilityIsRequiredAndPlaceholdersAreNeverActivated()
        {
            var targets=new[]{Unit(101,PlayableOwner.Enemy,Target)};
            Assert.IsNull(Plan(Snapshot(Orbital(0),targets)));
            Assert.IsNull(Plan(Snapshot(Orbital(3,1,2,3),new[]{Unit(101,PlayableOwner.Enemy,Target,40,PlayableEntityKind.Hawk)})));
        }
        [Test] public void MissingInformationChoosesScanAndSecondChoiceIsDifferent()
        {
            var first=Plan(Snapshot(Orbital(1),objectives:new[]{Objective(2,Target)},visible:false),personality:AiPersonality.Harassing);
            Assert.AreEqual(PlayableCommandKind.ChooseOrbital,first.Kind);Assert.AreEqual(1,first.TargetId);
            var second=Plan(Snapshot(Orbital(2,1),new[]{Unit(101,PlayableOwner.Enemy,Target)},objectives:new[]{Objective(2,Target)}));
            Assert.AreEqual(PlayableCommandKind.ChooseOrbital,second.Kind);Assert.AreNotEqual(1,second.TargetId);
        }
        [TestCase(49.999,true)][TestCase(50,false)][TestCase(0,false)]
        public void HackUsesStrictExactHealthBoundary(double health,bool valid)
        {
            var o=PlayableAiObservation.From(Snapshot(Orbital(1,8),new[]{Unit(101,PlayableOwner.Enemy,Target,health)}));
            Assert.AreEqual(valid,AiOrbitalPolicy.HackValid(o,P,101));
        }
        [Test] public void HiddenAndOwnedTargetsCannotBeHacked()
        {
            foreach(var s in new[]{Snapshot(Orbital(1,8),new[]{Unit(101,PlayableOwner.Enemy,Target)},visible:false),Snapshot(Orbital(1,8),new[]{Unit(101,PlayableOwner.Player,Target)})})
                Assert.False(AiOrbitalPolicy.HackValid(PlayableAiObservation.From(s),P,101));
        }
        [Test] public void MineBuildingsAlsoRequireVulnerabilityAndVisibility()
        {
            PlayableSnapshot View(double health,bool visible)=>Snapshot(Orbital(1,8),buildings:new[]{new PlayableBuildingSnapshot(501,PlayableOwner.Enemy,PlayableBuildingKind.Mine,Target,(int)Math.Ceiling(health),1,0,0,default,maxHealth:100,exactHealth:health)},visible:visible);
            Assert.True(AiOrbitalPolicy.HackValid(PlayableAiObservation.From(View(40,true)),P,501));
            Assert.False(AiOrbitalPolicy.HackValid(PlayableAiObservation.From(View(50,true)),P,501));
            Assert.False(AiOrbitalPolicy.HackValid(PlayableAiObservation.From(View(40,false)),P,501));
        }
        [Test] public void CommonIntervalAndTargetLossPreventDelivery()
        {
            var initial=Snapshot(Orbital(1,8),new[]{Unit(101,PlayableOwner.Enemy,Target)});var action=Plan(initial);Assert.NotNull(action);
            var live=PlayableAiObservation.From(Snapshot(Orbital(1,8),new[]{Unit(101,PlayableOwner.Enemy,Target,50)}));
            Assert.False(AiOrbitalPolicy.CanDeliver(live,P,AI,Knowledge(live),action));
            var cooldown=new OrbitalEscalationSnapshot(1,0,250,0,1,new[]{new OrbitalAbilitySnapshot(8,0,30,1)},Array.Empty<int>());
            Assert.IsNull(Plan(Snapshot(cooldown,new[]{Unit(101,PlayableOwner.Enemy,Target)})));
        }
        [Test] public void ScanHasPublicPurposeAndSkipsVisibleOrRecentlyCoveredArea()
        {
            var hidden=Snapshot(Orbital(1,1),objectives:new[]{Objective(2,Target)},visible:false);var action=Plan(hidden);Assert.AreEqual(1,action?.TargetId);
            var o=PlayableAiObservation.From(hidden);Assert.IsNull(Plan(hidden,Knowledge(o,new AiVisitedArea(2,90,true,false))));
            var live=PlayableAiObservation.From(Snapshot(Orbital(1,1),objectives:new[]{Objective(2,Target)}));
            Assert.False(AiOrbitalPolicy.CanDeliver(live,P,AI,Knowledge(live),action),"allied scan already reveals this point");
            Assert.IsNull(Plan(Snapshot(Orbital(1,1),visible:false)),"no hidden entity lookup without public or remembered target");
        }
        [Test] public void MineInterceptsGroundButDoesNotExplodeBesideFriendlies()
        {
            var ground=Unit(101,PlayableOwner.Enemy,Target,100);var action=Plan(Snapshot(Orbital(1,2),new[]{ground}));Assert.AreEqual(2,action?.TargetId);
            var danger=Snapshot(Orbital(1,2),new[]{ground,Unit(1,PlayableOwner.Player,new NavPoint(11,0),100)});Assert.IsNull(Plan(danger));
            var live=PlayableAiObservation.From(danger);Assert.False(AiOrbitalPolicy.CanDeliver(live,P,AI,Knowledge(live),action));
            Assert.IsNull(Plan(Snapshot(Orbital(1,2),new[]{Unit(101,PlayableOwner.Enemy,Target,100,PlayableEntityKind.Hawk)})));
        }
        [Test] public void CapacityDoesNotReplaceEquallyUsefulOldestMineOrDuplicateAllyPosition()
        {
            var d=P.CopyData();var mines=Enumerable.Range(1,(int)d.orbitalMineCapacity).Select(id=>new OrbitalMineSnapshot(id,PlayableOwner.Player,new NavPoint(-10,0),d.orbitalMineModelRadius,d.orbitalMineModelHeight,d.orbitalMineBlastRadius)).ToArray();
            var targets=new[]{Unit(101,PlayableOwner.Enemy,Target,100),Unit(102,PlayableOwner.Enemy,new NavPoint(-10,0),100)};
            var view=Snapshot(Orbital(1,2),targets,objectives:new[]{Objective(2,Target),Objective(3,new NavPoint(-10,0))},mines:mines);Assert.IsNull(Plan(view));
            var same=new[]{new OrbitalMineSnapshot(1,PlayableOwner.Player,Target,d.orbitalMineModelRadius,d.orbitalMineModelHeight,d.orbitalMineBlastRadius)};
            Assert.IsNull(Plan(Snapshot(Orbital(1,2),new[]{targets[0]},mines:same)));
        }
        [Test] public void SuccessfulScanMemorySuppressesRepeatsAndRestoresExactly()
        {
            var arbiter=new AiDecisionArbiter(AI);arbiter.RememberOrbitalScan(Target,90);
            var o=PlayableAiObservation.From(Snapshot(Orbital(1,1),objectives:new[]{Objective(2,Target)},visible:false));
            Assert.IsNull(AiOrbitalPolicy.Plan(o,P,AI,Knowledge(o),AiPersonality.Harassing,Array.Empty<PlayableAiTraceRecord>(),PlayableAiOpeningComposition.SourceIdentity,point=>arbiter.LastOrbitalScan(point,10)));
            byte[] Write(AiDecisionArbiter a){using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);a.WriteState(writer);return stream.ToArray();}
            var bytes=Write(arbiter);var restored=new AiDecisionArbiter(AI);using(var reader=new BinaryReader(new MemoryStream(bytes)))restored.ReadState(reader,100,o.OwnerId,o.Generation,P,AI);
            CollectionAssert.AreEqual(bytes,Write(restored));Assert.AreEqual(90,restored.LastOrbitalScan(Target,10));
            Assert.NotNull(AiOrbitalPolicy.Plan(o,P,AI,Knowledge(o),AiPersonality.Harassing,Array.Empty<PlayableAiTraceRecord>(),PlayableAiOpeningComposition.SourceIdentity,_=>-1));
        }
        [Test] public void PendingOrbitalChoiceRestoresExactCurrentAuthority()
        {
            var map=new AiTestMap();var p=PlayableProfile.Create(P.CopyData(),map);var config=map.Configuration(p,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var authority=new PlayableAuthorityTick(config,907,difficulty:AiDifficulty.Veteran);PlayableAuthorityTick restored=null;
            try
            {
                var state=(OrbitalEscalationState)authority.Domain.GetType().GetMethod("Orbital",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(authority.Domain,new object[]{PlayableOwner.Player});state.Damage(p.CopyData().orbitalThreshold1,p.CopyData());
                for(int i=0;i<60&&!authority.CaptureDiagnosticCheckpoints().Any(c=>c.Records.Any(r=>r.Policy==AiOrbitalPolicy.Policy&&r.Status==PlayableAiDeliveryStatus.Scheduled));i++)
                {while(authority.Requests.TryDequeue(out var request))Assert.True(authority.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>())));authority.TryAdvance();}
                Assert.True(authority.CaptureDiagnosticCheckpoints().Any(c=>c.Records.Any(r=>r.Policy==AiOrbitalPolicy.Policy&&r.Status==PlayableAiDeliveryStatus.Scheduled)));
                var bytes=authority.CaptureBytes();restored=PlayableAuthorityTick.RestoreBytes(bytes,config,AI);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
                for(int i=0;i<60;i++){while(restored.Requests.TryDequeue(out var request))Assert.True(restored.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>())));restored.TryAdvance();}
                Assert.True(restored.CaptureDiagnosticCheckpoints().Any(c=>c.Records.Any(r=>r.Policy==AiOrbitalPolicy.Policy&&r.Status==PlayableAiDeliveryStatus.Applied)),"restored pending choice executes normally");
            }
            finally{restored?.Stop();authority.Stop();restored?.Domain.DestroyOwnedNavigation();authority.Domain.DestroyOwnedNavigation();}
        }
    }
}
