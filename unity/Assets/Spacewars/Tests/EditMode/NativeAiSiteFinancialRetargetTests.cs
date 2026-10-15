using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiSiteFinancialRetargetTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private const int MemoMagic=0x45464d31,DormantMagic=0x45464431;
        private const string Archive="docs/native-ai/q4/original-natural-20261013-source-compiled-raw.zip";
        private const string ArchiveHash="bed495a3a55ae9d2b0507cc6f9d91c23f37bfee6e79dd9e099c51263eb81e154";
        private const string Checkpoint=".local/o6-natural/7109-checkpoint-600.world";
        private const string CheckpointHash="d6625a566d7f1845f676ca6b718e741be7f3ae495ec46d866c1716398b0a654b";

        private static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
        private static object Call(object o,string n,params object[] args){try{return o.GetType().GetMethod(n,F).Invoke(o,args);}catch(TargetInvocationException e){throw e.InnerException;}}
        private static string Root(){var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        private static PlayableProfile MapProfile()=>PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
        private static AiOpeningExecutor Opening()=>new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(41,"player-1",aiProfile:AiProfile.Initial,forcedOpening:PlayableAiOpening.GreedySafe),71);
        private static PlayableAiObservation View(long tick,int cash,int[] visibleSites,double changed9=0,bool safe=true,bool busy=false,PlayableProfile profile=null,bool homeReady=true,bool homeKnown=true)
        {
            profile=profile??MapProfile();var catalog=TerritoryRules.Sites(profile);var sites=visibleSites.Select(id=>catalog.Single(s=>s.Id==id)).ToArray();
            var first=sites.FirstOrDefault()??catalog.Single(s=>s.Id==9);var actor=new PlayableEntitySnapshot(13,PlayableOwner.Player,PlayableEntityKind.Explorer,
                new NavPoint(first.Position.X+6,first.Position.Z),profile.ExplorerHealth,busy,0,0,0);
            var units=safe?new[]{actor}:new[]{actor,new PlayableEntitySnapshot(14,PlayableOwner.Enemy,PlayableEntityKind.Tank,actor.Position,profile.TankHealth,false,0,0,0)};
            var snapshots=sites.Select(s=>new TerritorySiteSnapshot(s,null,null,s.Id==9?changed9:0,false,0,false)).ToArray();
            var objectives=catalog.Where(s=>s.Kind==PlayableBuildingKind.Mine||s.Kind==PlayableBuildingKind.Outpost).Select(s=>new PlayablePublicScoutObjective(s.Id,s.Position,
                s.Kind==PlayableBuildingKind.Mine?PlayablePublicScoutObjectiveRole.ExpansionIncome:PlayablePublicScoutObjectiveRole.ExpansionOutpost,true)).ToArray();
            var proofs=catalog.Where(s=>s.Id==9||s.Id==21).Select(s=>new PlayableRouteProof(13,PlayableOwner.Player,71,tick,1,actor.Position,profile.ExplorerCollisionRadius,
                PlayableRouteTargetKind.PublicObjective,s.Id,s.Position,s.Position,new[]{s.Position})).ToArray();
            var home=catalog.Single(s=>s.Id==1);int homeId=homeKnown?home.Id:0;
            var buildings=new System.Collections.Generic.List<PlayableBuildingSnapshot>();var allSites=snapshots.ToList();
            if(homeKnown)
            {
                int hq=1000;buildings.Add(new PlayableBuildingSnapshot(hq,PlayableOwner.Player,PlayableBuildingKind.Headquarters,home.Position,profile.HeadquartersHealth,1,0,0,default,siteId:home.Id,phase:ConstructionPhase.Ready));
                int refineryCount=homeReady?Opening().RequiredHomeRefineryCount(AiProfile.Initial):0;int id=1001;
                for(int i=0;i<refineryCount;i++){var slot=home.Slots[i];buildings.Add(new PlayableBuildingSnapshot(id++,PlayableOwner.Player,PlayableBuildingKind.Refinery,slot.Position,profile.RefineryHealth,1,0,0,default,siteId:home.Id,slotId:slot.Id,parentId:hq,phase:ConstructionPhase.Ready));}
                if(homeReady){var slot=home.Slots[refineryCount];buildings.Add(new PlayableBuildingSnapshot(id,PlayableOwner.Player,PlayableBuildingKind.Factory,slot.Position,profile.FactoryHealth,1,0,0,default,siteId:home.Id,slotId:slot.Id,parentId:hq,phase:ConstructionPhase.Ready));}
                allSites.Add(new TerritorySiteSnapshot(home,PlayableOwner.Player,PlayableOwner.Player,1,false,hq,true));
            }
            return PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,71,41,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,cash,null,
                units,buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:allSites.ToArray(),
                publicScoutObjectives:objectives,routeProofs:proofs,activeProfile:profile,homeSiteId:homeId));
        }
        private static object Start()=>Activator.CreateInstance(typeof(AiBudgetLedger).Assembly.GetType("Spacewars.Runtime.AiExpansionFinancial"),F,null,new object[]{"player-1",71L},null);
        private static void Observe(object f,AiBudgetLedger b,AiDecisionArbiter a,AiExpansionPlanner ex,PlayableAiObservation o,PlayableProfile p=null,int protect=0,bool allowed=true)
        {
            p=p??MapProfile();var opening=Opening();b.ObserveBank(o.Credits);Call(f,"Reconcile",o,p,AiProfile.Initial,opening,b,a,ex);var old=Call(f,"Detach",b);
            Call(f,"PriorityCost",o,p,AiProfile.Initial,a,ex,old,allowed);Call(f,"Observe",o,p,AiProfile.Initial,opening,b,a,ex,old,protect,allowed);
        }
        private static void Validate(object f,AiBudgetLedger b,AiDecisionArbiter a,long tick,PlayableProfile p=null)
        {p=p??MapProfile();Call(f,"Validate",p,AiProfile.Initial,b,a,tick,41,TerritoryRules.Sites(p),2);}
        private static byte[] ArbiterBytes(AiDecisionArbiter a)
        {using(var s=new MemoryStream()){using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))Call(a,"WriteState",w);return s.ToArray();}}
        private static AiDecisionArbiter RestoreArbiter(byte[] bytes,long tick,PlayableProfile p)
        {var a=new AiDecisionArbiter(AiProfile.Initial);using(var s=new MemoryStream(bytes))using(var r=new BinaryReader(s))Call(a,"ReadState",r,tick,"player-1",71L,p,AiProfile.Initial);return a;}
        private static byte[] SiteMemo(AiDecisionArbiter a)=>Convert.FromBase64String((string)Call(a,"SiteFundingMemory"));
        private static void InstallMemo(AiDecisionArbiter a,byte[] bytes)=>Call(a,"SaveSiteFundingMemory",Convert.ToBase64String(bytes));
        private static bool HasDormantTail(byte[] bytes,out bool hasBootstrap)
        {
            using(var s=new MemoryStream(bytes))using(var r=new BinaryReader(s))
            {
                r.ReadInt32();r.ReadString();r.ReadInt64();r.ReadString();
                if(r.ReadBoolean()){for(int i=0;i<5;i++)r.ReadInt32();r.ReadBoolean();r.ReadDouble();}
                int expired=r.ReadInt32();for(int n=0;n<expired;n++){for(int i=0;i<5;i++)r.ReadInt32();r.ReadBoolean();r.ReadDouble();}
                hasBootstrap=false;if(s.Position==s.Length)return false;
                int tail=r.ReadInt32();if(tail==DormantMagic)return true;
                hasBootstrap=true;r.ReadInt32();
                if(tail==1||tail==2)r.ReadBoolean();else if(tail==3){r.ReadBoolean();r.ReadBoolean();r.ReadBoolean();}
                else throw new ArgumentException("Unknown site-finance milestone version in test fixture.");
                return s.Position<s.Length&&r.ReadInt32()==DormantMagic;
            }
        }
        private static byte[] DormantMemo(string owner,long generation,string metadata,int count,int site=9,long created=0,long expires=1665,int marker=DormantMagic)
        {
            using(var s=new MemoryStream())using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))
            {
                w.Write(MemoMagic);w.Write(owner);w.Write(generation);w.Write(metadata);w.Write(false);w.Write(0);w.Write(marker);w.Write(count);
                if(count>0){w.Write(site);w.Write((int)PlayableBuildingKind.Mine);w.Write(-1);w.Write(-1);w.Write(0);w.Write(false);w.Write(0d);w.Write(created);w.Write(expires);}
                return s.ToArray();
            }
        }

        [Test]
        public void OriginalSafe7109_600To630AllocatesActualPartialMine21WitnessWithoutCommandOrClaim()
        {
            string root=Root(),archive=Path.Combine(root,Archive);Assert.AreEqual(ArchiveHash,Wire.Hash(File.ReadAllBytes(archive)));
            byte[] input;using(var zip=ZipFile.OpenRead(archive)){using(var source=zip.GetEntry(Checkpoint).Open())using(var ms=new MemoryStream()){source.CopyTo(ms);input=ms.ToArray();}}
            Assert.AreEqual(CheckpointHash,Wire.Hash(input));var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,7109,roster);
            var authority=PlayableAuthorityTick.RestoreBytes(input,config);CurrentExpansionDecisionReadback selected=null;
            PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests="ai-0";
            PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=r=>{if(r.Observation.Tick==630&&r.Observation.OwnerId=="ai-0")selected=r;};
            try
            {
                Assert.AreEqual(600,authority.Tick);Assert.True(input.SequenceEqual(authority.CaptureBytes()));
                using(var host=new UnityHostRouteService())for(int tick=600;tick<630;tick++)
                {bool advanced=false;for(int attempt=0;attempt<512;attempt++){if(authority.TryAdvance()){advanced=true;break;}host.Service(authority,64);}Assert.True(advanced,"ordinary typed-route barrier at "+authority.Tick);}
                Assert.NotNull(selected,"actual owner decision at tick 630");Assert.True(selected.AllowsExpansion);Assert.False(selected.Threatened);Assert.False(selected.UrgentRepair);
                Assert.AreEqual(21,selected.FinanceSite,"invalid zero target 9 retargets to the current typed safe Mine21 witness");
                var hold=selected.AfterFinancialAllocation.Reserved.Single(x=>x.Purpose=="expansion-financial");Assert.AreEqual(21,hold.Action.SiteId);Assert.AreEqual(100,hold.Amount);
                Assert.IsEmpty(hold.Claims);Assert.IsEmpty(hold.Action.EntityIds);Assert.AreEqual(1,selected.AfterFinancialAllocation.Reserved.Count(x=>x.Purpose=="expansion-financial"));
                Assert.IsFalse(selected.Selected.Any(x=>x.StartsWith("expansion:",StringComparison.Ordinal)),"partial finance must not create an unfunded command");
                Assert.IsNotNull(selected.Observation.Sites.Single(x=>x.Site.Id==21));Assert.IsTrue(selected.Observation.RouteProofs.Any(x=>x.TargetId==21&&x.Kind==PlayableRouteTargetKind.PublicObjective));
                byte[] world=authority.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(world,config);try{CollectionAssert.AreEqual(world,restored.CaptureBytes(),"C9 keeps the switched active goal and dormant Site9 clock deterministic");}finally{restored.Stop();}
            }
            finally
            {
                authority.Stop();PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=null;
            }
        }

        [Test]
        public void InvalidZeroRetargetReturnsToSite9WithOriginal1665DeadlineAcrossC9Rebind()
        {
            var p=MapProfile();var f=Start();var b=new AiBudgetLedger("player-1",71);var a=new AiDecisionArbiter(AiProfile.Initial);var ex=new AiExpansionPlanner("player-1");
            Observe(f,b,a,ex,View(315,0,new[]{9}),p);var original=b.Capture().Reserved.Single();Assert.AreEqual(9,original.Action.SiteId);Assert.Zero(original.Amount);Assert.AreEqual(315,original.CreatedTick);Assert.AreEqual(1665,original.ExpiresTick);
            Observe(f,b,a,ex,View(415,100,new[]{21}),p);var on21=b.Capture().Reserved.Single();Assert.AreEqual(21,on21.Action.SiteId);Assert.AreEqual(100,on21.Amount);Assert.AreEqual(415,on21.CreatedTick);Assert.AreEqual(1765,on21.ExpiresTick);
            var dormantMemo=SiteMemo(a);var restored=RestoreArbiter(ArbiterBytes(a),415,p);CollectionAssert.AreEqual(dormantMemo,SiteMemo(restored));
            var restoredBudget=AiBudgetLedger.Restore(b.Capture(),415);var rebind=Call(f,"Detach",restoredBudget);Call(f,"Rebind",p,AiProfile.Initial,415L,restoredBudget,restored,rebind);Validate(f,restoredBudget,restored,415,p);
            var restoredFinance=Start();Observe(restoredFinance,restoredBudget,restored,ex,View(500,100,new[]{21}),p);var keep21=restoredBudget.Capture().Reserved.Single();Assert.AreEqual(21,keep21.Action.SiteId);Assert.AreEqual(1765,keep21.ExpiresTick);
            Observe(restoredFinance,restoredBudget,restored,ex,View(900,100,new[]{9}),p);var returned=restoredBudget.Capture().Reserved.Single();Assert.AreEqual(9,returned.Action.SiteId);Assert.AreEqual(315,returned.CreatedTick);Assert.AreEqual(1665,returned.ExpiresTick,"switching away and back does not renew Site9");Assert.AreEqual(100,returned.Amount);
            Validate(restoredFinance,restoredBudget,restored,900,p);
            Observe(restoredFinance,restoredBudget,restored,ex,View(1664,100,new[]{9}),p);Assert.AreEqual(1665,restoredBudget.Capture().Reserved.Single().ExpiresTick);
            Observe(restoredFinance,restoredBudget,restored,ex,View(1665,100,new[]{9}),p);Assert.IsEmpty(restoredBudget.Capture().Reserved);Assert.True((bool)Call(restoredFinance,"IsExpiredFingerprint",View(1665,100,new[]{9}).Sites.Single(s=>s.Site.Id==9),restored));Validate(restoredFinance,restoredBudget,restored,1665,p);
        }

        [Test]
        public void DormantExpiryBecomesTombstoneAndMaterialChangeCanReopenIt()
        {
            var p=MapProfile();var f=Start();var b=new AiBudgetLedger("player-1",71);var a=new AiDecisionArbiter(AiProfile.Initial);var ex=new AiExpansionPlanner("player-1");
            Observe(f,b,a,ex,View(315,0,new[]{9}),p);Observe(f,b,a,ex,View(415,100,new[]{21}),p);
            Observe(f,b,a,ex,View(1665,100,new[]{21}),p);var active=b.Capture().Reserved.Single();Assert.AreEqual(21,active.Action.SiteId);Assert.True((bool)Call(f,"IsExpiredFingerprint",View(1665,100,new[]{9}).Sites.Single(s=>s.Site.Id==9),a));
            Observe(f,b,a,ex,View(1666,100,new[]{9}),p);var blocked=b.Capture().Reserved.Single();Assert.AreEqual(21,blocked.Action.SiteId);Assert.Zero(blocked.Amount,"unchanged expired dormant target cannot reopen");Validate(f,b,a,1666,p);
            Observe(f,b,a,ex,View(1667,100,new[]{9},changed9:.1),p);var reopened=b.Capture().Reserved.Single();Assert.AreEqual(9,reopened.Action.SiteId);Assert.AreEqual(1667,reopened.CreatedTick);Assert.AreEqual(3017,reopened.ExpiresTick);
            Validate(f,b,a,1667,p);
        }

        [Test]
        public void ReturnedTargetKeepsSwitchClockUntilRealAmountGrowthRenewsIt()
        {
            var p=MapProfile();var f=Start();var b=new AiBudgetLedger("player-1",71);var a=new AiDecisionArbiter(AiProfile.Initial);var ex=new AiExpansionPlanner("player-1");
            Observe(f,b,a,ex,View(315,0,new[]{9}),p);Observe(f,b,a,ex,View(415,100,new[]{21}),p);Observe(f,b,a,ex,View(900,100,new[]{9}),p);
            var returned=b.Capture().Reserved.Single();Assert.AreEqual(315,returned.CreatedTick);Assert.AreEqual(1665,returned.ExpiresTick);Assert.AreEqual(100,returned.Amount);
            Observe(f,b,a,ex,View(1000,100,new[]{9}),p);var unchanged=b.Capture().Reserved.Single();Assert.AreEqual(315,unchanged.CreatedTick);Assert.AreEqual(1665,unchanged.ExpiresTick,"same amount does not renew the restored target clock");
            Observe(f,b,a,ex,View(1100,101,new[]{9}),p);var grown=b.Capture().Reserved.Single();Assert.AreEqual(101,grown.Amount);Assert.AreEqual(1100,grown.CreatedTick);Assert.AreEqual(2450,grown.ExpiresTick,"real held-money growth keeps existing renewal semantics");Validate(f,b,a,1100,p);
        }

        [Test]
        public void DormantClockRebindUsesExistingLifetimeClampAndExpiresNormally()
        {
            var p=MapProfile();var f=Start();var b=new AiBudgetLedger("player-1",71);var a=new AiDecisionArbiter(AiProfile.Initial);var ex=new AiExpansionPlanner("player-1");
            Observe(f,b,a,ex,View(315,0,new[]{9}),p);Observe(f,b,a,ex,View(415,100,new[]{21}),p);
            b.RebindLifetime(900);var previous=(AiBudgetEntry)Call(f,"Detach",b);Call(f,"Rebind",p,AiProfile.Initial,500L,b,a,previous);Validate(f,b,a,500,p);
            Observe(f,b,a,ex,View(600,100,new[]{9}),p);var returned=b.Capture().Reserved.Single();Assert.AreEqual(9,returned.Action.SiteId);Assert.AreEqual(315,returned.CreatedTick);Assert.AreEqual(1215,returned.ExpiresTick,"rebind clamps without renewal");

            var f2=Start();var b2=new AiBudgetLedger("player-1",71);var a2=new AiDecisionArbiter(AiProfile.Initial);var ex2=new AiExpansionPlanner("player-1");
            Observe(f2,b2,a2,ex2,View(315,0,new[]{9}),p);Observe(f2,b2,a2,ex2,View(415,100,new[]{21}),p);
            b2.RebindLifetime(100);var old=(AiBudgetEntry)Call(f2,"Detach",b2);Call(f2,"Rebind",p,AiProfile.Initial,1200L,b2,a2,old);
            Assert.IsEmpty(b2.Capture().Reserved);Assert.True((bool)Call(f2,"IsExpiredFingerprint",View(1200,100,new[]{9}).Sites.Single(s=>s.Site.Id==9),a2));Validate(f2,b2,a2,1200,p);
        }

        [Test]
        public void ExistingZeroGoalKeepsItsClockAndTombstoneWhileBootstrapIsClosed()
        {
            var p=MapProfile();var a=new AiDecisionArbiter(AiProfile.Initial);string metadata=p.ProfileId+"@"+p.Revision+":"+AiProfile.Initial.Hash;
            using(var s=new MemoryStream())using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))
            {
                w.Write(MemoMagic);w.Write("player-1");w.Write(71L);w.Write(metadata);w.Write(true);
                w.Write(9);w.Write((int)PlayableBuildingKind.Mine);w.Write(-1);w.Write(-1);w.Write(0);w.Write(false);w.Write(0d);w.Write(0);
                InstallMemo(a,s.ToArray());
            }
            var f=Start();var b=new AiBudgetLedger("player-1",71);var ex=new AiExpansionPlanner("player-1");
            var priorAction=new PlayableAiAction(1,"player-1",p.ProfileId,p.Revision,71,315,PlayableCommandKind.BuildAt,siteId:9,buildingKind:PlayableBuildingKind.Mine,seed:41,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var prior=new AiBudgetEntry("expansion-financial:9","expansion-financial",priorAction,0,1,315,1665,"current site safety / actual full admission / no-progress expiry",Array.Empty<string>(),p.ProfileId+"@"+p.Revision);
            Assert.True(b.Reserve(prior));Assert.True((bool)Call(f,"BootstrapActive",a));
            foreach(long tick in new[]{500L,900L})
            {
                Observe(f,b,a,ex,View(tick,0,new[]{9},profile:p,homeReady:false),p);
                var preserved=b.Capture().Reserved.Single();Assert.AreEqual(9,preserved.Action.SiteId);Assert.Zero(preserved.Amount);
                Assert.AreEqual(315,preserved.CreatedTick);Assert.AreEqual(1665,preserved.ExpiresTick);Validate(f,b,a,tick,p);
            }
            Observe(f,b,a,ex,View(1665,0,new[]{9},profile:p,homeReady:false),p);
            Assert.AreEqual(0,(int)Call(f,"ActiveSite",a));Assert.IsEmpty(b.Capture().Reserved);
            Assert.True((bool)Call(f,"IsExpiredFingerprint",View(1665,0,new[]{9},profile:p,homeReady:false).Sites.Single(s=>s.Site.Id==9),a));
            Validate(f,b,a,1665,p);
            Observe(f,b,a,ex,View(1700,500,new[]{9,21},profile:p,homeReady:true),p);
            Assert.False((bool)Call(f,"BootstrapActive",a));Assert.AreNotEqual(9,(int)Call(f,"ActiveSite",a));
            Assert.True((bool)Call(f,"IsExpiredFingerprint",View(1700,500,new[]{9},profile:p,homeReady:true).Sites.Single(s=>s.Site.Id==9),a));
            var fresh=b.Capture().Reserved.Single();Assert.AreEqual(1700,fresh.CreatedTick);Assert.AreEqual(b.ReservationLifetimeTicks,fresh.ExpiresTick-fresh.CreatedTick);Validate(f,b,a,1700,p);
        }

        [Test]
        public void LegacyMemoMigratesOnKnownHomeIdentityAndKeepsQ4HistoryUnknown()
        {
            var p=MapProfile();var a=new AiDecisionArbiter(AiProfile.Initial);var metadata=p.ProfileId+"@"+p.Revision+":"+AiProfile.Initial.Hash;
            using(var s=new MemoryStream())using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))
            {w.Write(MemoMagic);w.Write("player-1");w.Write(71L);w.Write(metadata);w.Write(false);w.Write(0);InstallMemo(a,s.ToArray());}
            byte[] before=SiteMemo(a);var f=Start();var b=new AiBudgetLedger("player-1",71);var ex=new AiExpansionPlanner("player-1");
            Assert.True((bool)Call(f,"BootstrapActive",a),"absent v0 history does not imply home readiness");
            Observe(f,b,a,ex,View(0,0,new[]{9},profile:p,homeReady:false,homeKnown:false),p,allowed:false);CollectionAssert.AreEqual(before,SiteMemo(a),"without known home identity v0 bytes remain untouched");
            Observe(f,b,a,ex,View(315,0,new[]{9},profile:p,homeReady:false),p);Assert.AreEqual(0,(int)Call(f,"ActiveSite",a));Assert.IsEmpty(b.Capture().Reserved);
            Assert.True((bool)Call(f,"BootstrapActive",a));Assert.False((bool)Call(f,"FirstIncomePriorityActive",a),"unknown v0 Q4 history stays unknown while Q7 remains closed");
            Assert.IsFalse(HasDormantTail(SiteMemo(a),out var firstBootstrap));Assert.True(firstBootstrap,"known home identity is serialized separately from readiness");
            Observe(f,b,a,ex,View(415,100,new[]{21},homeReady:true),p);var held21=b.Capture().Reserved.Single();Assert.AreEqual(21,held21.Action.SiteId);Assert.AreEqual(100,held21.Amount);Assert.AreEqual(415,held21.CreatedTick);Assert.AreEqual(b.ReservationLifetimeTicks,held21.ExpiresTick-held21.CreatedTick);
            Assert.False((bool)Call(f,"BootstrapActive",a),"actual READY home facts open Q7");Assert.False((bool)Call(f,"FirstIncomePriorityActive",a));
            var bytes=SiteMemo(a);Assert.IsFalse(HasDormantTail(bytes,out var hasBootstrap),"the first post-home goal has no dormant predecessor because no target was created before home READY");Assert.True(hasBootstrap,"home readiness is retained while Q4 history stays unknown");
            var restored=RestoreArbiter(ArbiterBytes(a),415,p);CollectionAssert.AreEqual(bytes,SiteMemo(restored));var restoredBudget=AiBudgetLedger.Restore(b.Capture(),415);Validate(f,restoredBudget,restored,415,p);
        }

        [TestCase("marker")][TestCase("catalog-bound")][TestCase("fingerprint")][TestCase("clock")][TestCase("future-clock")]
        public void DormantExtensionRejectsMalformedAndUnboundState(string corruption)
        {
            var p=MapProfile();var a=new AiDecisionArbiter(AiProfile.Initial);string metadata=p.ProfileId+"@"+p.Revision+":"+AiProfile.Initial.Hash;
            long futureCreated=corruption=="future-clock"?101:0;long futureExpires=futureCreated+AiProfile.SecondsToTicks(AiProfile.Initial.Value("economy.reservationExpirySeconds"),30);
            byte[] payload=corruption=="marker"?DormantMemo("player-1",71,metadata,0,marker:123):
                corruption=="catalog-bound"?DormantMemo("player-1",71,metadata,99):
                DormantMemo("player-1",71,metadata,1,site:corruption=="fingerprint"?999:9,created:corruption=="clock"?1665:futureCreated,expires:corruption=="future-clock"?futureExpires:1665);
            InstallMemo(a,payload);var f=Start();var b=new AiBudgetLedger("player-1",71);
            Assert.Throws<ArgumentException>(()=>Validate(f,b,a,100,p),corruption);
        }
    }
}
