using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class ArmySystemsRaceTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile A=AiProfile.Initial;
        static PlayableAiObservation View(long tick,int enemyWeapon=0,int enemyDefense=0,int ownWeapon=0,int ownDefense=0,int cash=100,int count=10,PlayableOwner enemy=PlayableOwner.Enemy,bool hidden=false,bool allied=false,bool specialized=false)
        {
            var site=TerritoryRules.Sites(P).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
            var units=Enumerable.Range(10,count).Select(i=>new PlayableEntitySnapshot(i,PlayableOwner.Player,PlayableEntityKind.Tank,site.Position,100,false,0,0,0,upgraded:specialized)).Concat(enemyWeapon+enemyDefense>0?new[]{new PlayableEntitySnapshot(100,enemy,PlayableEntityKind.Tank,site.Position,100,false,0,0,0,weaponSystemsLevel:enemyWeapon,defensiveSystemsLevel:enemyDefense)}:Array.Empty<PlayableEntitySnapshot>());
            var kinds=new[]{PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.AdvancedWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems,PlayableResearchKind.AdvancedDefensiveSystems};
            var research=kinds.Where(k=>(PlayableResearchRules.Weapon(k)?ownWeapon:ownDefense)>=(PlayableResearchRules.Previous(k).HasValue?2:1)).Concat(specialized?new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ExplorerAssaultGuns,PlayableResearchKind.ShkvalGuidance}:Array.Empty<PlayableResearchKind>()).Select((k,i)=>new PlayableResearchOrderSnapshot(i+1,k,3,PlayableResearchRules.Cost(P,k),1,1,false,true)).ToArray();
            var buildings=new[]{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,400,1,0,0,default,siteId:site.Id),new PlayableBuildingSnapshot(2,PlayableOwner.Player,PlayableBuildingKind.Factory,site.Slots[0].Position,250,1,0,0,default,siteId:site.Id,slotId:site.Slots[0].Id,parentId:1),new PlayableBuildingSnapshot(3,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,site.Slots[1].Position,250,1,0,0,default,siteId:site.Id,slotId:site.Slots[1].Id,parentId:1)};
            var vision=new PlayableVision(1,64,64,4,1.5);vision.Refresh(Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,1,7,tick+1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,cash,null,units.ToArray(),buildings,Array.Empty<PlayableProjectileSnapshot>(),null,null,vision:hidden?vision.Snapshot():null,team:allied?1:-1,participants:allied?new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,1,OfflineControl.Human)}:null,ownerResearch:research,researchAvailability:AiResearchCatalog.All.Select(d=>new PlayableResearchAvailabilitySnapshot(d.Kind,d.Cost(P),!research.Any(r=>r.Kind==d.Kind)&&PlayableResearchRules.Prerequisite(d.Kind,research))).ToArray()));
        }
        static AiArmyResearchPolicy Policy(PlayableAiObservation o,AiDifficulty d,AiDecisionArbiter m,AiGamePhase phase=AiGamePhase.Early)=>AiArmyResearchPolicy.Observe(o,P,d,phase,m);
        static AiTechnologyPlan Plan(PlayableAiObservation o,AiArmyResearchPolicy policy,AiBudgetLedger b=null,bool emergency=false)
        {b=b??new AiBudgetLedger(o.OwnerId,o.Generation);b.ObserveBank(o.Credits);return new AiTechnologyPlanner().Plan(o,P,A,b.Capture(),emergency,armyPolicy:policy);}
        [TestCase(AiDifficulty.Veteran,0)][TestCase(AiDifficulty.Fighter,1800)]
        public void SeenLevelsCreateCatchupAfterDifficultyDelayAndSurviveLostContact(AiDifficulty d,long delay)
        {
            var memory=new AiDecisionArbiter(A);var first=View(30,enemyWeapon:2);var p=Policy(first,d,memory);
            Assert.AreEqual(delay==0,p.Prioritizes(PlayableResearchKind.ImprovedWeaponSystems));
            if(delay>0)Assert.False(Policy(View(30+delay-1),d,memory).Prioritizes(PlayableResearchKind.ImprovedWeaponSystems));
            p=Policy(View(30+delay),d,memory);Assert.True(p.Prioritizes(PlayableResearchKind.ImprovedWeaponSystems));Assert.True(p.Prioritizes(PlayableResearchKind.AdvancedWeaponSystems));Assert.False(p.Prioritizes(PlayableResearchKind.ImprovedDefensiveSystems));
            var plan=Plan(View(30+delay),p);Assert.True(plan.PriorityResearch);Assert.AreEqual(PlayableResearchKind.ImprovedWeaponSystems,plan.FundingGoal.Research);Assert.IsNull(plan.Action,"not enough cash: save rather than spend on unrelated tech");
        }
        [Test] public void HiddenAndAlliedUpgradesDoNotStartCatchup()
        {
            foreach(var o in new[]{View(30,2,2,hidden:true),View(30,2,2,allied:true)})
            {var memory=new AiDecisionArbiter(A);Assert.False(Plan(o,Policy(o,AiDifficulty.Veteran,memory)).PriorityResearch);Assert.False(Policy(View(3000),AiDifficulty.Fighter,memory).Prioritizes(PlayableResearchKind.ImprovedWeaponSystems));}
        }
        [Test] public void RecruitIgnoresEnemyAndIndependentResearchHasLongDelay()
        {
            var m=new AiDecisionArbiter(A);var early=Policy(View(30,2,2),AiDifficulty.Recruit,m,AiGamePhase.Late);Assert.False(early.Autonomous);Assert.False(early.Prioritizes(PlayableResearchKind.ImprovedWeaponSystems));Assert.True(Plan(View(30,2,2),early).Branches.Where(b=>b.Descriptor.Global).All(b=>b.Utility==0),"Recruit may still fund unrelated specialized technologies");
            long due=AiProfile.SecondsToTicks(P.ArmySystemsAiDelaySeconds(AiDifficulty.Recruit),30);
            Assert.False(Policy(View(due-1),AiDifficulty.Recruit,m).Autonomous);var late=Policy(View(due),AiDifficulty.Recruit,m);Assert.True(late.Autonomous);Assert.Less(late.UtilityScale,1);Assert.False(Plan(View(due),late).PriorityResearch);
        }
        [TestCase(AiGamePhase.Mid)][TestCase(AiGamePhase.Late)] public void VeteranActivelyFundsOwnResearchWithoutOpponent(AiGamePhase phase)
        {
            var o=View(180*30,cash:300);var policy=Policy(o,AiDifficulty.Veteran,new AiDecisionArbiter(A),phase);var plan=Plan(o,policy);
            Assert.True(policy.Autonomous);Assert.True(plan.PriorityResearch);Assert.True(PlayableResearchRules.Global(plan.Action.ResearchKind));
            Assert.False(Plan(View(30,count:9),policy).PriorityResearch);
            Assert.IsNull(Plan(o,policy,emergency:true).FundingGoal);
        }
        [TestCase(AiDifficulty.Veteran,1,180)][TestCase(AiDifficulty.Veteran,2,360)]
        [TestCase(AiDifficulty.Fighter,1,300)][TestCase(AiDifficulty.Fighter,2,480)]
        [TestCase(AiDifficulty.Recruit,1,420)][TestCase(AiDifficulty.Recruit,2,600)]
        public void SeparateTierDeadlinesApplyToBothBranchesEvenInLatePhase(AiDifficulty d,int level,int seconds)
        {
            Assert.AreEqual(seconds,P.ArmySystemsAiDelaySeconds(d,level));var m=new AiDecisionArbiter(A);
            var before=View(seconds*30L-1,ownWeapon:level-1,ownDefense:level-1,cash:10000,specialized:true);
            var at=View(seconds*30L,ownWeapon:level-1,ownDefense:level-1,cash:10000,specialized:true);
            var kinds=level==1?new[]{PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems}:new[]{PlayableResearchKind.AdvancedWeaponSystems,PlayableResearchKind.AdvancedDefensiveSystems};
            var blocked=Policy(before,d,m,AiGamePhase.Late);var open=Policy(at,d,m,AiGamePhase.Late);
            foreach(var kind in kinds){Assert.False(blocked.Allows(kind),kind.ToString());Assert.True(open.Allows(kind),kind.ToString());Assert.Zero(Plan(before,blocked).Branches.Single(b=>b.Descriptor.Kind==kind).Utility);Assert.Greater(Plan(at,open).Branches.Single(b=>b.Descriptor.Kind==kind).Utility,0);}
            Assert.NotNull(Plan(at,open).Action);Assert.Contains(Plan(at,open).Action.ResearchKind,kinds);
        }
        [TestCase(AiDifficulty.Veteran,0)][TestCase(AiDifficulty.Fighter,1800)]
        public void ObservedSecondTierBypassesItsDeadlineAfterReaction(AiDifficulty d,long delay)
        {
            var m=new AiDecisionArbiter(A);Policy(View(30,enemyWeapon:2,ownWeapon:1),d,m);
            var o=View(30+delay,ownWeapon:1,cash:500);var p=Policy(o,d,m,AiGamePhase.Late);
            Assert.Less(o.Tick,AiProfile.SecondsToTicks(P.ArmySystemsAiDelaySeconds(d,2),30));
            Assert.True(p.Allows(PlayableResearchKind.AdvancedWeaponSystems));Assert.AreEqual(PlayableResearchKind.AdvancedWeaponSystems,Plan(o,p).Action.ResearchKind);
        }
        [Test] public void CatchupFundsNextLegalTierAndStopsWhenCaughtUp()
        {
            var m=new AiDecisionArbiter(A);var o=View(30,enemyDefense:2,ownDefense:1,cash:500);var plan=Plan(o,Policy(o,AiDifficulty.Veteran,m));
            Assert.AreEqual(PlayableResearchKind.AdvancedDefensiveSystems,plan.Action.ResearchKind);
            var caught=View(60,ownDefense:2);Assert.False(Plan(caught,Policy(caught,AiDifficulty.Veteran,m)).PriorityResearch);
        }
        [Test] public void PrioritySavingsUseSharedLedgerAndProtectSafetyAndScout()
        {
            var o=View(30,enemyDefense:1,cash:260);var m=new AiDecisionArbiter(A);var b=new AiBudgetLedger(o.OwnerId,o.Generation);b.ObserveBank(o.Credits);b.SetSafetyReserve(40);var plan=Plan(o,Policy(o,AiDifficulty.Veteran,m),b);
            var funding=new AiTechnologyFunding(o.OwnerId,o.Generation);funding.Observe(o,P,A,b,m,plan,true,50);
            Assert.AreEqual(170,b.Capture().Reserved.Where(AiTechnologyFunding.IsFunding).Sum(e=>e.Amount));Assert.AreEqual(50,b.Available);Assert.Zero(b.PaidTotal);
        }
        [Test] public void ObservationMemoryRestoresExactlyAndCannotBeEvictedByIntentChurn()
        {
            var m=new AiDecisionArbiter(A);Policy(View(30,2,2),AiDifficulty.Fighter,m);
            var action=new PlayableAiAction(1,"player-1",P.ProfileId,P.Revision,1,1,PlayableCommandKind.Stop);
            m.Select(Enumerable.Range(0,150).Select(i=>new AiIntent("churn-"+i,"test",action,100,0,0,Array.Empty<string>())),30,0,0,1);
            byte[] Bytes(AiDecisionArbiter a){using(var s=new MemoryStream()){using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))typeof(AiDecisionArbiter).GetMethod("WriteState",F).Invoke(a,new object[]{w});return s.ToArray();}}
            var bytes=Bytes(m);var restored=new AiDecisionArbiter(A);using(var reader=new BinaryReader(new MemoryStream(bytes)))typeof(AiDecisionArbiter).GetMethod("ReadState",F).Invoke(restored,new object[]{reader,1830L,"player-1",1L,P,A});
            CollectionAssert.AreEqual(bytes,Bytes(restored));Assert.True(Policy(View(1830),AiDifficulty.Fighter,restored).Prioritizes(PlayableResearchKind.AdvancedDefensiveSystems));
        }
        [Test] public void EachLabTuningValueParticipatesInAuthorityProfileBinding()
        {
            var wire=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.WorldWire",true);var method=wire.GetMethod("Binding",BindingFlags.Static|BindingFlags.NonPublic);
            byte[] Binding(PlayableProfile p)=>(byte[])method.Invoke(null,new object[]{p,false});var original=Binding(P);
            foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("ai.research.armySystems.")))
            {var data=P.CopyData();NativeBalanceFields.Write(field,data,field.Read(data)+field.Step);Assert.False(original.AsSpan().SequenceEqual(Binding(PlayableProfile.Create(data))),field.Path);}
        }
        [Test] public void TuningFieldsRoundtripThroughLabMetadata()
        {
            var fields=PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("ai.research.armySystems.")).ToArray();Assert.AreEqual(11,fields.Length);var data=P.CopyData();
            foreach(var f in fields){NativeBalanceFields.Write(f,data,f.Maximum);Assert.Throws<ArgumentOutOfRangeException>(()=>NativeBalanceFields.Write(f,data,f.Maximum+f.Step));}
            var tuned=PlayableProfile.Create(data);Assert.AreEqual(300,tuned.ArmySystemsAiCatchupSeconds(AiDifficulty.Fighter));
        }
    }
}
